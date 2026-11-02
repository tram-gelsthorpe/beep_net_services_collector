-- Fresh Unified Database Schema
-- Consolidates Website Collection and Management in a single, clean schema

SET XACT_ABORT ON;
GO

-- Drop existing objects if they exist (in dependency order)
IF OBJECT_ID('dbo.WebsiteSelectorsCollectedData', 'U') IS NOT NULL DROP TABLE dbo.WebsiteSelectorsCollectedData;
GO
IF OBJECT_ID('dbo.WebsiteSelectorsExecutions', 'U') IS NOT NULL DROP TABLE dbo.WebsiteSelectorsExecutions;
GO
IF OBJECT_ID('dbo.WebsiteSelectorsMonitor', 'U') IS NOT NULL DROP TABLE dbo.WebsiteSelectorsMonitor;
GO
IF OBJECT_ID('dbo.WebsiteSelectors', 'U') IS NOT NULL DROP TABLE dbo.WebsiteSelectors;
GO
IF OBJECT_ID('dbo.Websites', 'U') IS NOT NULL DROP TABLE dbo.Websites;
GO

-- Drop existing procedures
IF OBJECT_ID('dbo.ClaimWebsiteSelectors', 'P') IS NOT NULL DROP PROCEDURE dbo.ClaimWebsiteSelectors;
GO
IF OBJECT_ID('dbo.CompleteWebsiteSelector', 'P') IS NOT NULL DROP PROCEDURE dbo.CompleteWebsiteSelector;
GO
IF OBJECT_ID('dbo.RenewWebsiteSelectorLease', 'P') IS NOT NULL DROP PROCEDURE dbo.RenewWebsiteSelectorLease;
GO
IF OBJECT_ID('dbo.StartWebsiteSelectorExecution', 'P') IS NOT NULL DROP PROCEDURE dbo.StartWebsiteSelectorExecution;
GO

-- Create Websites table - Main parent table
CREATE TABLE dbo.Websites (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	Name nvarchar(200) NOT NULL,
	Url nvarchar(2048) NOT NULL,
	IsEnabled bit NOT NULL DEFAULT 1,
	CreatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	UpdatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

-- Create WebsiteSelectors table - Associates selectors/endpoints with websites
CREATE TABLE dbo.WebsiteSelectors (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	WebsitesId uniqueidentifier NOT NULL REFERENCES dbo.Websites(Id) ON DELETE CASCADE,
	CollectionUrl nvarchar(2048) NOT NULL,
	CollectionType varchar(10) NOT NULL 
		CONSTRAINT CK_Selector_Type CHECK (CollectionType IN ('HTML', 'JSON', 'XML', 'RAW')),
	ContentSelector nvarchar(1000) NULL,
	TableSelector nvarchar(1000) NULL,
	IntervalSeconds int NOT NULL 
		CONSTRAINT CK_Selector_Interval CHECK (IntervalSeconds BETWEEN 10 AND 2592000),
	IsEnabled bit NOT NULL DEFAULT 1,
	CreatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	CONSTRAINT UQ_WebsiteSelectors_Unique UNIQUE (WebsitesId, CollectionUrl, CollectionType, ContentSelector, TableSelector)
);
GO

CREATE INDEX IX_WebsiteSelectors_Website ON dbo.WebsiteSelectors(WebsitesId) WHERE IsEnabled = 1;
GO

-- Create WebsiteSelectorsMonitor table - 1:1 with WebsiteSelectors, manages monitoring state
CREATE TABLE dbo.WebsiteSelectorsMonitor (
	Id uniqueidentifier PRIMARY KEY REFERENCES dbo.WebsiteSelectors(Id) ON DELETE CASCADE,
	NextRunAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	TimeoutSeconds int NOT NULL DEFAULT 30 
		CONSTRAINT CK_Monitor_Timeout CHECK (TimeoutSeconds BETWEEN 1 AND 300),
	MaxAttempts int NOT NULL DEFAULT 5 
		CONSTRAINT CK_Monitor_Attempts CHECK (MaxAttempts BETWEEN 1 AND 20),
	AttemptCount int NOT NULL DEFAULT 0,
	LeaseOwner uniqueidentifier NULL,
	LeaseToken uniqueidentifier NULL,
	LeaseUntil datetimeoffset(7) NULL,
	LastStartedAt datetimeoffset(7) NULL,
	LastSucceededAt datetimeoffset(7) NULL,
	LastError nvarchar(2000) NULL,
	CreatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	RowVer rowversion NOT NULL
);
GO

CREATE INDEX IX_WebsiteSelectorsMonitor_Due ON dbo.WebsiteSelectorsMonitor(NextRunAt, Id) 
WHERE LeaseOwner IS NULL;
GO

-- Create WebsiteSelectorsExecutions table - Tracks individual collection attempts
CREATE TABLE dbo.WebsiteSelectorsExecutions (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	WebsiteSelectorsId uniqueidentifier NOT NULL REFERENCES dbo.WebsiteSelectors(Id) ON DELETE CASCADE,
	LeaseToken uniqueidentifier NOT NULL UNIQUE,
	WorkerId uniqueidentifier NOT NULL,
	StartedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	FinishedAt datetimeoffset(7) NULL,
	Status varchar(16) NOT NULL 
		CONSTRAINT CK_Exec_Status CHECK (Status IN ('Running', 'Succeeded', 'Failed', 'Abandoned')),
	AttemptNumber int NOT NULL,
	HttpStatus int NULL,
	ErrorMessage nvarchar(2000) NULL
);
GO

CREATE INDEX IX_Exec_WebsiteSelectorsTime ON dbo.WebsiteSelectorsExecutions(WebsiteSelectorsId, StartedAt DESC);
GO

-- Create WebsiteSelectorsCollectedData table - Stores collected content
CREATE TABLE dbo.WebsiteSelectorsCollectedData (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	WebsiteSelectorsExecutionsId uniqueidentifier NOT NULL REFERENCES dbo.WebsiteSelectorsExecutions(Id) ON DELETE CASCADE,
	ContentHash binary(32) NOT NULL,
	ResponseContent nvarchar(max) NOT NULL,
	ExtractedJson nvarchar(max) NULL,
	CollectedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	CONSTRAINT UQ_CollectedData_Hash UNIQUE(WebsiteSelectorsExecutionsId, ContentHash)
);
GO

CREATE INDEX IX_CollectedData_Execution ON dbo.WebsiteSelectorsCollectedData(WebsiteSelectorsExecutionsId, CollectedAt DESC);
GO

-- ============================================================
-- Stored Procedures
-- ============================================================

-- Claim website selectors for processing
CREATE OR ALTER PROCEDURE dbo.ClaimWebsiteSelectors
	@WorkerId uniqueidentifier,
	@BatchSize int,
	@LeaseSeconds int
AS
BEGIN
	SET NOCOUNT ON;
	SET XACT_ABORT ON;

	IF @BatchSize NOT BETWEEN 1 AND 500 OR @LeaseSeconds NOT BETWEEN 60 AND 3600 
		THROW 50001, 'Invalid claim settings', 1;

	DECLARE @now datetimeoffset(7) = SYSUTCDATETIME();

	;WITH due AS (
		SELECT TOP (@BatchSize) m.Id, s.*, m.IntervalSeconds, m.TimeoutSeconds, m.MaxAttempts, m.AttemptCount
		FROM dbo.WebsiteSelectorsMonitor m
		INNER JOIN dbo.WebsiteSelectors s ON m.Id = s.Id
		WHERE s.IsEnabled = 1 
			AND m.NextRunAt <= @now 
			AND (m.LeaseOwner IS NULL OR m.LeaseUntil <= @now)
		ORDER BY m.NextRunAt, m.Id
	)
	UPDATE due 
	SET LeaseOwner = @WorkerId, 
		LeaseToken = NEWID(), 
		LeaseUntil = DATEADD(second, @LeaseSeconds, @now),
		LastStartedAt = @now, 
		AttemptCount = AttemptCount + 1
	OUTPUT inserted.Id as WebsiteSelectorsId,
		   inserted.WebsitesId,
		   inserted.CollectionUrl,
		   inserted.CollectionType,
		   inserted.ContentSelector,
		   inserted.TableSelector,
		   inserted.IntervalSeconds,
		   inserted.TimeoutSeconds,
		   inserted.MaxAttempts,
		   inserted.AttemptCount,
		   inserted.LeaseToken;
END;
GO

-- Complete a website selector collection
CREATE OR ALTER PROCEDURE dbo.CompleteWebsiteSelector
	@WebsiteSelectorsId uniqueidentifier,
	@LeaseToken uniqueidentifier,
	@Succeeded bit,
	@HttpStatus int = NULL,
	@Error nvarchar(2000) = NULL,
	@ContentHash binary(32) = NULL,
	@ResponseContent nvarchar(max) = NULL,
	@ExtractedJson nvarchar(max) = NULL
AS
BEGIN
	SET NOCOUNT ON;
	SET XACT_ABORT ON;

	BEGIN TRAN;

	DECLARE @now datetimeoffset(7) = SYSUTCDATETIME();
	DECLARE @interval int, @attempt int, @max int;

	SELECT @interval = IntervalSeconds, @attempt = AttemptCount, @max = MaxAttempts
	FROM dbo.WebsiteSelectorsMonitor WITH(UPDLOCK, HOLDLOCK)
	WHERE Id = @WebsiteSelectorsId AND LeaseToken = @LeaseToken AND LeaseUntil > @now;

	IF @interval IS NULL 
	BEGIN 
		ROLLBACK;
		RETURN -1;
	END;

	IF @Succeeded = 1
	BEGIN
		IF @ContentHash IS NOT NULL 
		BEGIN
			INSERT dbo.WebsiteSelectorsCollectedData(WebsiteSelectorsExecutionsId, ContentHash, ResponseContent, ExtractedJson)
			SELECT Id, @ContentHash, @ResponseContent, @ExtractedJson 
			FROM dbo.WebsiteSelectorsExecutions 
			WHERE LeaseToken = @LeaseToken;
		END;

		UPDATE dbo.WebsiteSelectorsMonitor 
		SET AttemptCount = 0, 
			LastSucceededAt = @now,
			LastError = NULL,
			NextRunAt = DATEADD(second, @interval, @now),
			LeaseOwner = NULL,
			LeaseToken = NULL,
			LeaseUntil = NULL 
		WHERE Id = @WebsiteSelectorsId;
	END
	ELSE
	BEGIN
		DECLARE @delay int;
		IF @attempt >= @max 
			SET @delay = @interval;
		ELSE 
			SET @delay = CONVERT(int, POWER(CONVERT(float, 2), IIF(@attempt > 10, 10, @attempt - 1)) * 15) + ABS(CHECKSUM(NEWID()) % 15);

		UPDATE dbo.WebsiteSelectorsMonitor 
		SET AttemptCount = IIF(@attempt >= @max, 0, @attempt),
			LastError = @Error,
			NextRunAt = DATEADD(second, @delay, @now),
			LeaseOwner = NULL,
			LeaseToken = NULL,
			LeaseUntil = NULL 
		WHERE Id = @WebsiteSelectorsId;
	END;

	UPDATE dbo.WebsiteSelectorsExecutions 
	SET FinishedAt = @now,
		Status = IIF(@Succeeded = 1, 'Succeeded', 'Failed'),
		HttpStatus = @HttpStatus,
		ErrorMessage = @Error 
	WHERE LeaseToken = @LeaseToken AND Status = 'Running';

	COMMIT;
	RETURN 0;
END;
GO

-- Renew lease for a website selector
CREATE OR ALTER PROCEDURE dbo.RenewWebsiteSelectorLease
	@WebsiteSelectorsId uniqueidentifier,
	@LeaseToken uniqueidentifier,
	@LeaseSeconds int
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @now datetimeoffset(7) = SYSUTCDATETIME();

	UPDATE dbo.WebsiteSelectorsMonitor 
	SET LeaseUntil = DATEADD(second, @LeaseSeconds, @now)
	WHERE Id = @WebsiteSelectorsId 
		AND LeaseToken = @LeaseToken 
		AND LeaseUntil > @now;

	RETURN @@ROWCOUNT;
END;
GO

-- Start execution for a website selector
CREATE OR ALTER PROCEDURE dbo.StartWebsiteSelectorExecution
	@WebsiteSelectorsId uniqueidentifier,
	@LeaseToken uniqueidentifier,
	@WorkerId uniqueidentifier
AS
BEGIN
	SET NOCOUNT ON;
	SET XACT_ABORT ON;

	DECLARE @now datetimeoffset(7) = SYSUTCDATETIME();

	-- Mark any abandoned running executions
	UPDATE dbo.WebsiteSelectorsExecutions 
	SET Status = 'Abandoned',
		FinishedAt = @now,
		ErrorMessage = 'Lease expired' 
	WHERE WebsiteSelectorsId = @WebsiteSelectorsId 
		AND Status = 'Running' 
		AND LeaseToken <> @LeaseToken;

	-- Create new execution record
	INSERT dbo.WebsiteSelectorsExecutions(WebsiteSelectorsId, LeaseToken, WorkerId, Status, AttemptNumber)
	SELECT Id, @LeaseToken, @WorkerId, 'Running', AttemptCount 
	FROM dbo.WebsiteSelectorsMonitor
	WHERE Id = @WebsiteSelectorsId 
		AND LeaseToken = @LeaseToken 
		AND LeaseUntil > @now;
END;
GO
