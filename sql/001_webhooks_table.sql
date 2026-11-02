-- Webhook Delivery Schema - Option B
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.WebsiteSelectorsWebhookDeliveries', 'U') IS NOT NULL DROP TABLE dbo.WebsiteSelectorsWebhookDeliveries;
GO
IF OBJECT_ID('dbo.WebsiteSelectorsWebhookMonitor', 'U') IS NOT NULL DROP TABLE dbo.WebsiteSelectorsWebhookMonitor;
GO
IF OBJECT_ID('dbo.WebsiteSelectorsWebhooks', 'U') IS NOT NULL DROP TABLE dbo.WebsiteSelectorsWebhooks;
GO

TEXT_PLACEHOLDER_CREATE_TABLE WebsiteSelectorsWebhooks (--placeholder)

CREATE TABLE dbo.WebsiteSelectorsWebhooks (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	WebsiteSelectorsId uniqueidentifier NOT NULL REFERENCES dbo.WebsiteSelectors(Id) ON DELETE CASCADE,
	WebhookUrl nvarchar(2048) NOT NULL,
	IsEnabled bit NOT NULL DEFAULT 1,
	TimeoutSeconds int NOT NULL DEFAULT 30
		CONSTRAINT CK_Webhook_Timeout CHECK (TimeoutSeconds BETWEEN 5 AND 300),
	MaxRetryAttempts int NOT NULL DEFAULT 3
		CONSTRAINT CK_Webhook_MaxRetries CHECK (MaxRetryAttempts BETWEEN 1 AND 10),
	CreatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	UpdatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	CONSTRAINT UQ_WebsiteSelectorsWebhooks_Unique UNIQUE (WebsiteSelectorsId, WebhookUrl),
	CONSTRAINT CK_Webhook_MaxRetries CHECK (MaxRetryAttempts BETWEEN 1 AND 10)
);
);
GO

CREATE TABLE dbo.WebsiteSelectorsWebhookMonitor (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	WebsiteSelectorsWebhooksId uniqueidentifier NOT NULL UNIQUE REFERENCES dbo.WebsiteSelectorsWebhooks(Id) ON DELETE CASCADE,
	LastDeliveredAt datetimeoffset(7) NULL,
	LastStatusCode int NULL,
	LastErrorMessage nvarchar(2000) NULL,
	ConsecutiveFailures int NOT NULL DEFAULT 0,
	IsCircuitBreakerOpen bit NOT NULL DEFAULT 0,
	UpdatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

CREATE TABLE dbo.WebsiteSelectorsWebhookDeliveries (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	WebsiteSelectorsWebhooksId uniqueidentifier NOT NULL REFERENCES dbo.WebsiteSelectorsWebhooks(Id) ON DELETE CASCADE,
	CollectedDataId uniqueidentifier NOT NULL REFERENCES dbo.WebsiteSelectorsCollectedData(Id) ON DELETE CASCADE,
	Status varchar(16) NOT NULL DEFAULT 'PENDING',
	AttemptNumber int NOT NULL DEFAULT 1,
	NextRetryAt datetimeoffset(7) NULL,
	HttpStatusCode int NULL,
	ErrorMessage nvarchar(2000) NULL,
	CreatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	QueuedAt datetimeoffset(7) NULL,
	DeliveredAt datetimeoffset(7) NULL,
	CONSTRAINT UQ_Delivery_Unique UNIQUE (WebsiteSelectorsWebhooksId, CollectedDataId, AttemptNumber),
	CONSTRAINT CK_Delivery_Status CHECK (Status IN ('PENDING', 'IN_PROGRESS', 'SUCCESS', 'FAILED', 'ABANDONED'))
);
GO

CREATE INDEX IX_WebhookDeliveries_Pending ON dbo.WebsiteSelectorsWebhookDeliveries(WebsiteSelectorsWebhooksId, NextRetryAt, Status) WHERE Status IN ('PENDING', 'IN_PROGRESS');
CREATE INDEX IX_WebhookDeliveries_Webhook ON dbo.WebsiteSelectorsWebhookDeliveries(WebsiteSelectorsWebhooksId, DeliveredAt DESC);
CREATE INDEX IX_WebhookDeliveries_CollectedData ON dbo.WebsiteSelectorsWebhookDeliveries(CollectedDataId, CreatedAt DESC);
GO

-- Stored Procedure: Claim pending webhook delivery for processing
-- Uses UPDLOCK, READPAST to prevent duplicate processing across multiple workers
CREATE PROCEDURE dbo.ClaimWebhookDelivery
	@MaxResults int = 50
AS
BEGIN
	SET XACT_ABORT ON;

	-- Atomically claim deliveries and mark as IN_PROGRESS
	-- UPDLOCK: Acquire exclusive lock on selected rows
	-- READPAST: Skip already locked rows (claimed by other workers)
	DECLARE @ClaimedIds TABLE (Id uniqueidentifier);

	-- Step 1: Identify deliveries to claim and lock them
	INSERT INTO @ClaimedIds
	SELECT TOP (@MaxResults) d.Id
	FROM dbo.WebsiteSelectorsWebhookDeliveries d WITH (UPDLOCK, READPAST)
	WHERE d.Status = 'PENDING'
		AND d.NextRetryAt <= SYSUTCDATETIME()
	ORDER BY d.NextRetryAt ASC, d.CreatedAt ASC;

	-- Step 2: Atomically update status to IN_PROGRESS (prevents other workers from claiming)
	UPDATE dbo.WebsiteSelectorsWebhookDeliveries
	SET Status = 'IN_PROGRESS'
	WHERE Id IN (SELECT Id FROM @ClaimedIds);

	-- Step 3: Return complete delivery information for processing
	SELECT 
		d.Id as DeliveryId,
		d.WebsiteSelectorsWebhooksId,
		d.CollectedDataId,
		d.AttemptNumber,
		w.WebhookUrl,
		w.TimeoutSeconds,
		w.MaxRetryAttempts,
		cd.RawBody
	FROM dbo.WebsiteSelectorsWebhookDeliveries d
	INNER JOIN dbo.WebsiteSelectorsWebhooks w ON d.WebsiteSelectorsWebhooksId = w.Id
	INNER JOIN dbo.WebsiteSelectorsCollectedData cd ON d.CollectedDataId = cd.Id
	WHERE d.Id IN (SELECT Id FROM @ClaimedIds)
		AND w.IsEnabled = 1;
END;
GO

-- Stored Procedure: Complete a webhook delivery (release the claim)
-- Updates delivery status based on success/failure and handles retries with exponential backoff
CREATE PROCEDURE dbo.CompleteWebhookDelivery
	@DeliveryId uniqueidentifier,
	@Success bit,
	@HttpStatusCode int = NULL,
	@ErrorMessage nvarchar(2000) = NULL
AS
BEGIN
	SET XACT_ABORT ON;

	DECLARE @WebsiteSelectorsWebhooksId uniqueidentifier;
	DECLARE @AttemptNumber int;
	DECLARE @MaxRetryAttempts int;
	DECLARE @NextRetryAt datetimeoffset(7);
	DECLARE @now datetimeoffset(7) = SYSUTCDATETIME();

	BEGIN TRANSACTION;

	-- Get delivery details
	SELECT @WebsiteSelectorsWebhooksId = WebsiteSelectorsWebhooksId, 
		   @AttemptNumber = AttemptNumber
	FROM dbo.WebsiteSelectorsWebhookDeliveries
	WHERE Id = @DeliveryId;

	-- Get max retries from webhook config
	SELECT @MaxRetryAttempts = MaxRetryAttempts
	FROM dbo.WebsiteSelectorsWebhooks
	WHERE Id = @WebsiteSelectorsWebhooksId;

	IF @Success = 1
	BEGIN
		-- SUCCESS: Release claim by marking as SUCCESS
		UPDATE dbo.WebsiteSelectorsWebhookDeliveries
		SET Status = 'SUCCESS', 
			HttpStatusCode = @HttpStatusCode, 
			DeliveredAt = @now
		WHERE Id = @DeliveryId;

		-- Update monitor: reset consecutive failures
		UPDATE dbo.WebsiteSelectorsWebhookMonitor
		SET LastDeliveredAt = @now, 
			LastStatusCode = @HttpStatusCode,
			ConsecutiveFailures = 0,
			IsCircuitBreakerOpen = 0,
			UpdatedAt = @now
		WHERE WebsiteSelectorsWebhooksId = @WebsiteSelectorsWebhooksId;
	END
	ELSE
	BEGIN
		-- FAILURE: Check if we should retry or abandon
		IF @AttemptNumber < @MaxRetryAttempts
		BEGIN
			-- Calculate exponential backoff: 1s, 2s, 4s, 8s, etc. (capped at 1 hour)
			SET @NextRetryAt = DATEADD(second, 
				CASE WHEN POWER(2, @AttemptNumber) > 3600 THEN 3600 ELSE POWER(2, @AttemptNumber) END,
				@now);

			-- Mark current attempt as FAILED, release claim
			UPDATE dbo.WebsiteSelectorsWebhookDeliveries
			SET Status = 'FAILED', 
				HttpStatusCode = @HttpStatusCode, 
				ErrorMessage = @ErrorMessage
			WHERE Id = @DeliveryId;

			-- Create next retry attempt as PENDING (will be picked up by next ClaimWebhookDelivery)
			INSERT INTO dbo.WebsiteSelectorsWebhookDeliveries
				(WebsiteSelectorsWebhooksId, CollectedDataId, Status, AttemptNumber, NextRetryAt, ErrorMessage)
			SELECT WebsiteSelectorsWebhooksId, CollectedDataId, 'PENDING', 
				   @AttemptNumber + 1, @NextRetryAt, @ErrorMessage
			FROM dbo.WebsiteSelectorsWebhookDeliveries
			WHERE Id = @DeliveryId;
		END
		ELSE
		BEGIN
			-- Max retries exceeded: ABANDON delivery, release claim
			UPDATE dbo.WebsiteSelectorsWebhookDeliveries
			SET Status = 'ABANDONED', 
				HttpStatusCode = @HttpStatusCode, 
				ErrorMessage = @ErrorMessage,
				DeliveredAt = @now
			WHERE Id = @DeliveryId;

			-- Update monitor: increment consecutive failures, potentially open circuit breaker
			UPDATE dbo.WebsiteSelectorsWebhookMonitor
			SET ConsecutiveFailures = ConsecutiveFailures + 1,
				IsCircuitBreakerOpen = CASE WHEN (ConsecutiveFailures + 1) >= 5 THEN 1 ELSE 0 END,
				LastErrorMessage = @ErrorMessage,
				UpdatedAt = @now
			WHERE WebsiteSelectorsWebhooksId = @WebsiteSelectorsWebhooksId;
		END
	END

	COMMIT TRANSACTION;
END;
GO
