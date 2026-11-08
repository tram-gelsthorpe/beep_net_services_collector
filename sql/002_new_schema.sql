SET XACT_ABORT ON;
GO

-- Drop old tables if they exist (in dependency order)
IF OBJECT_ID('dbo.CollectedData', 'U') IS NOT NULL DROP TABLE dbo.CollectedData;
IF OBJECT_ID('dbo.WebsiteExecutions', 'U') IS NOT NULL DROP TABLE dbo.WebsiteExecutions;
IF OBJECT_ID('dbo.WebsiteSelectorsMonitor', 'U') IS NOT NULL DROP TABLE dbo.WebsiteSelectorsMonitor;
IF OBJECT_ID('dbo.WebsiteSelectors', 'U') IS NOT NULL DROP TABLE dbo.WebsiteSelectors;
IF OBJECT_ID('dbo.Websites', 'U') IS NOT NULL DROP TABLE dbo.Websites;
GO

-- Create Websites table
CREATE TABLE dbo.Websites (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	Name nvarchar(200) NOT NULL,
	Url nvarchar(2048) NOT NULL,
	IsEnabled bit NOT NULL DEFAULT 1
);
GO

-- Create WebsiteSelectors table
CREATE TABLE dbo.WebsiteSelectors (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	WebsitesId uniqueidentifier NOT NULL REFERENCES dbo.Websites(Id),
	CollectionUrl nvarchar(2048) NOT NULL,
	CollectionType varchar(10) NOT NULL CONSTRAINT CK_Selector_Type CHECK (CollectionType IN ('HTML','JSON','XML','RAW')),
	ContentSelector nvarchar(1000) NULL,
	TableSelector nvarchar(1000) NULL,
	IntervalSeconds int NOT NULL CONSTRAINT CK_Selector_Interval CHECK (IntervalSeconds BETWEEN 10 AND 2592000),
	IsEnabled bit NOT NULL DEFAULT 1,
	CONSTRAINT UQ_WebsiteSelectors_Unique UNIQUE (WebsitesId, CollectionUrl, CollectionType, ContentSelector, TableSelector)
);
GO

-- Create WebsiteSelectorsMonitor table (1:1 with WebsiteSelectors)
CREATE TABLE dbo.WebsiteSelectorsMonitor (
	Id uniqueidentifier PRIMARY KEY REFERENCES dbo.WebsiteSelectors(Id),
	NextRunAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	TimeoutSeconds int NOT NULL DEFAULT 30 CONSTRAINT CK_Monitor_Timeout CHECK (TimeoutSeconds BETWEEN 1 AND 300),
	MaxAttempts int NOT NULL DEFAULT 5 CONSTRAINT CK_Monitor_Attempts CHECK (MaxAttempts BETWEEN 1 AND 20),
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

CREATE INDEX IX_WebsiteSelectorsMonitor_Due ON dbo.WebsiteSelectorsMonitor(NextRunAt, Id);
GO

-- Create WebsiteExecutions table
CREATE TABLE dbo.WebsiteExecutions (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	WebsiteSelectorsId uniqueidentifier NOT NULL REFERENCES dbo.WebsiteSelectors(Id),
	LeaseToken uniqueidentifier NOT NULL UNIQUE,
	WorkerId uniqueidentifier NOT NULL,
	StartedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	FinishedAt datetimeoffset(7) NULL,
	Status varchar(16) NOT NULL CONSTRAINT CK_Exec_Status CHECK (Status IN ('Running','Succeeded','Failed','Abandoned')),
	AttemptNumber int NOT NULL,
	HttpStatus int NULL,
	ErrorMessage nvarchar(2000) NULL
);
GO

CREATE INDEX IX_Exec_WebsiteSelectorsTime ON dbo.WebsiteExecutions(WebsiteSelectorsId, StartedAt DESC);
GO

-- Create CollectedData table
CREATE TABLE dbo.CollectedData (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	WebsiteExecutionsId uniqueidentifier NOT NULL REFERENCES dbo.WebsiteExecutions(Id),
	ContentHash binary(32) NOT NULL,
	ResponseContent nvarchar(max) NOT NULL,
	ExtractedJson nvarchar(max) NULL,
	CollectedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	CONSTRAINT UQ_CollectedData_Hash UNIQUE(WebsiteExecutionsId, ContentHash)
);
GO

-- Create stored procedure for claiming selectors
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

-- Create stored procedure for completing collection
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
	FROM dbo.WebsiteSelectorsMonitor
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
			INSERT dbo.CollectedData(WebsiteExecutionsId, ContentHash, ResponseContent, ExtractedJson)
			SELECT Id, @ContentHash, @ResponseContent, @ExtractedJson 
			FROM dbo.WebsiteExecutions 
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

	UPDATE dbo.WebsiteExecutions 
	SET FinishedAt = @now,
		Status = IIF(@Succeeded = 1, 'Succeeded', 'Failed'),
		HttpStatus = @HttpStatus,
		ErrorMessage = @Error 
	WHERE LeaseToken = @LeaseToken AND Status = 'Running';

	COMMIT;
	RETURN 0;
END;
GO

-- Create stored procedure for renewing lease
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

-- Create stored procedure for starting execution
CREATE OR ALTER PROCEDURE dbo.StartWebsiteSelectorExecution
	@WebsiteSelectorsId uniqueidentifier,
	@LeaseToken uniqueidentifier,
	@WorkerId uniqueidentifier
AS
BEGIN
	SET NOCOUNT ON;
	SET XACT_ABORT ON;

	DECLARE @now datetimeoffset(7) = SYSUTCDATETIME();

	UPDATE dbo.WebsiteExecutions 
	SET Status = 'Abandoned',
		FinishedAt = @now,
		ErrorMessage = 'Lease expired' 
	WHERE WebsiteSelectorsId = @WebsiteSelectorsId 
		AND Status = 'Running' 
		AND LeaseToken <> @LeaseToken;

	INSERT dbo.WebsiteExecutions(WebsiteSelectorsId, LeaseToken, WorkerId, Status, AttemptNumber)
	SELECT Id, @LeaseToken, @WorkerId, 'Running', AttemptCount 
	FROM dbo.WebsiteSelectorsMonitor
	WHERE Id = @WebsiteSelectorsId 
		AND LeaseToken = @LeaseToken 
		AND LeaseUntil > @now;
END;
GO
