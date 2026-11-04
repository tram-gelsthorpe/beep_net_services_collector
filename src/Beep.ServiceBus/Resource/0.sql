-- =====================================================
-- WorkerFramework Schema - Generic Work Item Tracking
-- =====================================================
-- This schema provides a unified, domain-agnostic system
-- for any background worker to claim, process, and release work items.
-- All workers (Collector, Webhook, etc.) use the same procedures and status tracking.

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- =====================================================
-- Database Version Tracking
-- =====================================================
-- Tracks the schema version and deployment information
CREATE TABLE dbo.DatabaseVersion (
	Id int PRIMARY KEY IDENTITY(1,1),
	Version nvarchar(20) NOT NULL UNIQUE,  -- e.g., '0', '1'
	CreatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	UpdatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

-- Insert initial database version
INSERT INTO dbo.DatabaseVersion (Version)
VALUES ('0', 'Initial ServiceBus schema with WorkItem, Attempt, and Heartbeat tables', NULL);

-- WorkItem Statuses - Standard state machine for all workers
CREATE TABLE dbo.WorkItemStatuses (
	Id int PRIMARY KEY,
	Name nvarchar(50) NOT NULL UNIQUE,
	Description nvarchar(500)
);

-- Insert standard status values
SET IDENTITY_INSERT dbo.WorkItemStatuses ON;
INSERT INTO dbo.WorkItemStatuses (Id, Name, Description) VALUES
	(1, 'PENDING', 'Waiting to be claimed by a worker'),
	(2, 'CLAIMED', 'Actively leased by a worker'),
	(3, 'RUNNING', 'Currently being processed'),
	(4, 'SUCCEEDED', 'Completed successfully, no retry needed'),
	(5, 'FAILED', 'Attempt failed, will be retried'),
	(6, 'ABANDONED', 'Exceeded max retries or marked unrecoverable'),
	(7, 'DEFERRED', 'Temporarily paused (e.g., circuit breaker open)');
SET IDENTITY_INSERT dbo.WorkItemStatuses OFF;

-- Generic WorkItems table - tracks any work item across all workers
CREATE TABLE dbo.WorkItems (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	WorkerType nvarchar(100) NOT NULL,  -- 'SelectorCollector', 'WebhookDelivery', etc.
	DomainId uniqueidentifier NOT NULL,  -- Foreign key to domain-specific table (WebsiteSelectorsId, DeliveryId, etc.)
	DomainType nvarchar(100) NOT NULL,  -- 'WebsiteSelector', 'WebhookDelivery', etc.
	StatusId int NOT NULL DEFAULT 1,  -- PENDING
	LeaseOwner uniqueidentifier NULL,  -- Worker ID that owns the current lease
	LeaseToken uniqueidentifier NULL,  -- Token proving ownership (used without worker ID for security)
	LeaseUntil datetimeoffset(7) NULL,  -- When this lease expires
	AttemptCount int NOT NULL DEFAULT 0,
	MaxAttempts int NOT NULL DEFAULT 5,
	NextRunAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),  -- When to retry or next execute
	LastError nvarchar(2000) NULL,
	CreatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	StartedAt datetimeoffset(7) NULL,  -- First processing attempt
	CompletedAt datetimeoffset(7) NULL,  -- Final completion (success, abandoned, or deferred)

	FOREIGN KEY (StatusId) REFERENCES dbo.WorkItemStatuses(Id),
	INDEX IX_WorkItems_Status_NextRun (StatusId, NextRunAt),
	INDEX IX_WorkItems_Lease (LeaseOwner, LeaseUntil),
	INDEX IX_WorkItems_Domain (DomainType, DomainId, StatusId)
);

-- Attempt history - audit trail of each try including heartbeats
CREATE TABLE dbo.WorkItemAttempts (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	WorkItemId uniqueidentifier NOT NULL,
	AttemptNumber int NOT NULL,
	StartedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	FinishedAt datetimeoffset(7) NULL,
	StatusSnapshot nvarchar(50),  -- Last known status for this attempt (RUNNING, SUCCEEDED, FAILED, etc.)
	ErrorMessage nvarchar(2000) NULL,
	ResultData nvarchar(max) NULL,  -- JSON for heartbeat/completion data
	DurationMs int NULL,  -- Elapsed time in milliseconds

	FOREIGN KEY (WorkItemId) REFERENCES dbo.WorkItems(Id),
	INDEX IX_WorkItemAttempts_WorkItem (WorkItemId, AttemptNumber),
	INDEX IX_WorkItemAttempts_Started (StartedAt)
);

-- Heartbeat log - tracks renewals during long processing
CREATE TABLE dbo.WorkItemHeartbeats (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	WorkItemId uniqueidentifier NOT NULL,
	LeaseToken uniqueidentifier NOT NULL,
	RenewedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	LeaseExtendedUntil datetimeoffset(7) NOT NULL,

	FOREIGN KEY (WorkItemId) REFERENCES dbo.WorkItems(Id),
	INDEX IX_WorkItemHeartbeats_WorkItem (WorkItemId, RenewedAt DESC)
);

GO

-- =====================================================
-- Generic Stored Procedures
-- =====================================================

-- Claim a batch of work items atomically
CREATE OR ALTER PROCEDURE dbo.ClaimWorkItems
	@WorkerType nvarchar(100),
	@WorkerId uniqueidentifier,
	@BatchSize int,
	@LeaseSeconds int
AS
BEGIN
	SET NOCOUNT ON;
	SET XACT_ABORT ON;
	SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

	IF @BatchSize NOT BETWEEN 1 AND 500 OR @LeaseSeconds NOT BETWEEN 60 AND 3600
		THROW 50001, 'Invalid claim settings: BatchSize must be 1-500, LeaseSeconds 60-3600', 1;

	DECLARE @now datetimeoffset(7) = SYSUTCDATETIME();

	-- Atomically claim up to @BatchSize items that are ready and not leased
	;WITH due AS (
		SELECT TOP (@BatchSize) 
			Id, 
			DomainId, 
			DomainType,
			AttemptCount,
			MaxAttempts
		FROM dbo.WorkItems
		WHERE WorkerType = @WorkerType
			AND StatusId IN (1, 5)  -- PENDING or FAILED (ready to retry)
			AND (LeaseOwner IS NULL OR LeaseUntil <= @now)  -- Not leased or lease expired
			AND NextRunAt <= @now  -- Due for processing
		ORDER BY AttemptCount DESC, NextRunAt ASC, Id  -- Prioritize retries, then by due time
	)
	UPDATE due
	SET LeaseOwner = @WorkerId,
		LeaseToken = NEWID(),
		LeaseUntil = DATEADD(second, @LeaseSeconds, @now),
		StatusId = 2,  -- CLAIMED
		StartedAt = CASE WHEN StartedAt IS NULL THEN @now ELSE StartedAt END,
		AttemptCount = AttemptCount + 1
	OUTPUT inserted.Id as WorkItemId,
		   inserted.DomainId,
		   inserted.DomainType,
		   inserted.LeaseToken,
		   inserted.AttemptCount,
		   inserted.MaxAttempts;
END;
GO

-- Renew an active lease (called during heartbeat)
CREATE OR ALTER PROCEDURE dbo.RenewWorkItemLease
	@WorkItemId uniqueidentifier,
	@LeaseToken uniqueidentifier,
	@LeaseSeconds int
AS
BEGIN
	SET NOCOUNT ON;
	SET XACT_ABORT ON;

	DECLARE @now datetimeoffset(7) = SYSUTCDATETIME();
	DECLARE @newLeaseUntil datetimeoffset(7) = DATEADD(second, @LeaseSeconds, @now);

	UPDATE dbo.WorkItems
	SET LeaseUntil = @newLeaseUntil
	WHERE Id = @WorkItemId 
		AND LeaseToken = @LeaseToken
		AND LeaseUntil > @now;  -- Only renew if not already expired

	-- Record in heartbeat log
	IF @@ROWCOUNT > 0
	BEGIN
		INSERT INTO dbo.WorkItemHeartbeats (WorkItemId, LeaseToken, LeaseExtendedUntil)
		VALUES (@WorkItemId, @LeaseToken, @newLeaseUntil);
		SELECT CAST(1 as bit) as Success;
	END
	ELSE
	BEGIN
		-- Lease not found or expired
		SELECT CAST(0 as bit) as Success;
	END
END;
GO

-- Complete a work item (record final status)
CREATE OR ALTER PROCEDURE dbo.CompleteWorkItem
	@WorkItemId uniqueidentifier,
	@LeaseToken uniqueidentifier,
	@Succeeded bit,
	@Error nvarchar(2000) = NULL,
	@ResultData nvarchar(max) = NULL,
	@BackoffStrategyName nvarchar(50) = 'exponential'  -- 'exponential', 'linear', 'fixed'
AS
BEGIN
	SET NOCOUNT ON;
	SET XACT_ABORT ON;

	DECLARE @now datetimeoffset(7) = SYSUTCDATETIME();
	DECLARE @delay int;
	DECLARE @attempt int;
	DECLARE @maxAttempts int;
	DECLARE @nextRunAt datetimeoffset(7);

	BEGIN TRANSACTION;

	-- Verify lease ownership and get attempt info
	SELECT @attempt = AttemptCount, @maxAttempts = MaxAttempts
	FROM dbo.WorkItems WITH (UPDLOCK, HOLDLOCK)
	WHERE Id = @WorkItemId AND LeaseToken = @LeaseToken;

	IF @attempt IS NULL
	BEGIN
		ROLLBACK;
		THROW 50002, 'Work item not found or lease token invalid', 1;
	END;

	-- Determine next status and delay
	IF @Succeeded = 1
	BEGIN
		-- SUCCESS: No more retries needed
		UPDATE dbo.WorkItems
		SET StatusId = 4,  -- SUCCEEDED
			LeaseOwner = NULL,
			LeaseToken = NULL,
			LeaseUntil = NULL,
			CompletedAt = @now,
			LastError = NULL
		WHERE Id = @WorkItemId;

		-- Record attempt
		INSERT INTO dbo.WorkItemAttempts (WorkItemId, AttemptNumber, StatusSnapshot, FinishedAt)
		VALUES (@WorkItemId, @attempt, 'SUCCEEDED', @now);
	END
	ELSE
	BEGIN
		-- FAILURE: Check if we should retry
		IF @attempt >= @maxAttempts
		BEGIN
			-- Max retries exceeded: ABANDON
			UPDATE dbo.WorkItems
			SET StatusId = 6,  -- ABANDONED
				LeaseOwner = NULL,
				LeaseToken = NULL,
				LeaseUntil = NULL,
				CompletedAt = @now,
				LastError = @Error
			WHERE Id = @WorkItemId;

			INSERT INTO dbo.WorkItemAttempts (WorkItemId, AttemptNumber, StatusSnapshot, ErrorMessage, FinishedAt)
			VALUES (@WorkItemId, @attempt, 'ABANDONED', @Error, @now);
		END
		ELSE
		BEGIN
			-- Calculate backoff delay based on strategy
			IF @BackoffStrategyName = 'exponential'
			BEGIN
				-- 2^n seconds with jitter (capped at 1 hour)
				SET @delay = CONVERT(int, POWER(2, IIF(@attempt > 10, 10, @attempt - 1)) * 1) 
					+ ABS(CHECKSUM(NEWID()) % 30);
				IF @delay > 3600 SET @delay = 3600;
			END
			ELSE IF @BackoffStrategyName = 'linear'
			BEGIN
				-- n * 60 seconds (linear growth)
				SET @delay = @attempt * 60;
				IF @delay > 3600 SET @delay = 3600;
			END
			ELSE IF @BackoffStrategyName = 'fixed'
			BEGIN
				-- Always 60 seconds
				SET @delay = 60;
			END
			ELSE
			BEGIN
				-- Default to exponential
				SET @delay = CONVERT(int, POWER(2, IIF(@attempt > 10, 10, @attempt - 1)) * 1) 
					+ ABS(CHECKSUM(NEWID()) % 30);
				IF @delay > 3600 SET @delay = 3600;
			END;

			SET @nextRunAt = DATEADD(second, @delay, @now);

			-- Schedule retry: mark FAILED now, will return to PENDING for next claim
			UPDATE dbo.WorkItems
			SET StatusId = 5,  -- FAILED (temporarily; will be PENDING on next claim)
				LeaseOwner = NULL,
				LeaseToken = NULL,
				LeaseUntil = NULL,
				NextRunAt = @nextRunAt,
				LastError = @Error
			WHERE Id = @WorkItemId;

			INSERT INTO dbo.WorkItemAttempts (WorkItemId, AttemptNumber, StatusSnapshot, ErrorMessage, FinishedAt)
			VALUES (@WorkItemId, @attempt, 'FAILED', @Error, @now);
		END
	END;

	COMMIT TRANSACTION;
	SELECT CAST(1 as bit) as Success;
END;
GO

-- Record a heartbeat / status update without changing the lease
CREATE OR ALTER PROCEDURE dbo.RecordWorkItemAttempt
	@WorkItemId uniqueidentifier,
	@AttemptNumber int,
	@StatusSnapshot nvarchar(50),
	@ErrorMessage nvarchar(2000) = NULL,
	@ResultData nvarchar(max) = NULL
AS
BEGIN
	SET NOCOUNT ON;

	INSERT INTO dbo.WorkItemAttempts (WorkItemId, AttemptNumber, StatusSnapshot, ErrorMessage, ResultData, FinishedAt)
	VALUES (@WorkItemId, @AttemptNumber, @StatusSnapshot, @ErrorMessage, @ResultData, SYSUTCDATETIME());
END;
GO

-- Release a lease without changing status (for graceful timeout/cancellation)
CREATE OR ALTER PROCEDURE dbo.ReleaseWorkItemLease
	@WorkItemId uniqueidentifier,
	@LeaseToken uniqueidentifier
AS
BEGIN
	SET NOCOUNT ON;
	SET XACT_ABORT ON;

	UPDATE dbo.WorkItems
	SET LeaseOwner = NULL,
		LeaseToken = NULL,
		LeaseUntil = NULL
	WHERE Id = @WorkItemId AND LeaseToken = @LeaseToken;

	SELECT CAST(@@ROWCOUNT as bit) as Success;
END;
GO

-- Get work item details for display/debugging
CREATE OR ALTER PROCEDURE dbo.GetWorkItem
	@WorkItemId uniqueidentifier
AS
BEGIN
	SET NOCOUNT ON;

	SELECT 
		w.Id,
		w.WorkerType,
		w.DomainId,
		w.DomainType,
		s.Name as Status,
		w.LeaseOwner,
		w.AttemptCount,
		w.MaxAttempts,
		w.NextRunAt,
		w.LastError,
		w.CreatedAt,
		w.StartedAt,
		w.CompletedAt
	FROM dbo.WorkItems w
	LEFT JOIN dbo.WorkItemStatuses s ON w.StatusId = s.Id
	WHERE w.Id = @WorkItemId;

	-- Attempt history
	SELECT 
		AttemptNumber,
		StartedAt,
		FinishedAt,
		StatusSnapshot,
		ErrorMessage
	FROM dbo.WorkItemAttempts
	WHERE WorkItemId = @WorkItemId
	ORDER BY AttemptNumber DESC;
END;
GO
