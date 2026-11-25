-- =====================================================
-- Beep.Scheduler Database Schema
-- =====================================================
-- Provides reusable scheduling infrastructure for registering work items
-- Supports multiple worker types with different frequencies

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- =====================================================
-- Schedule Frequency Types
-- =====================================================
-- Defines when schedules should run

EXEC(N'
CREATE OR ALTER VIEW dbo.ScheduleFrequencies AS
SELECT 
	CAST(1 AS INT) AS Id, 
	''Hourly'' AS Name,
	''Run every hour'' AS Description
UNION ALL
SELECT 2, ''Daily'', ''Run once per day at specified time''
UNION ALL
SELECT 3, ''Weekly'', ''Run once per week on specified day at time''
UNION ALL
SELECT 4, ''Monthly'', ''Run once per month on specified day at time''
UNION ALL
SELECT 5, ''Yearly'', ''Run once per year on specified month/day at time''
UNION ALL
SELECT 6, ''Never'', ''Disabled - will not run''
');

-- =====================================================
-- Database Version Tracking
-- =====================================================

CREATE TABLE dbo.DatabaseVersion (
    Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
    Version nvarchar(20) NOT NULL UNIQUE,
    CreatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

-- Insert initial database version
INSERT INTO dbo.DatabaseVersion (Version)
VALUES ('0');

-- =====================================================
-- WorkerSchedules Table
-- =====================================================
-- Defines schedules for each worker type

CREATE TABLE dbo.WorkerSchedules (
	Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),

	-- Worker identification
	WorkerType NVARCHAR(100) NOT NULL,  -- 'WebsiteCollector', 'BigDataProcess', etc.
	Description NVARCHAR(500) NULL,

	-- Frequency configuration
	Frequency NVARCHAR(50) NOT NULL,    -- 'Daily', 'Weekly', 'Monthly', 'Yearly', 'Hourly'
	StartTime TIME NOT NULL,            -- Time to start (for daily/weekly/monthly/yearly)
	DayOfWeek INT NULL,                 -- 0-6 for weekly (0=Sunday)
	DayOfMonth INT NULL,                -- 1-31 for monthly
	DayOfYear INT NULL,                 -- 1-366 for yearly (1=Jan 1, 32=Feb 1, etc.)

	-- Control
	IsActive BIT NOT NULL DEFAULT 1,
	MaxConcurrentRegistrations INT NOT NULL DEFAULT 10,  -- Limit to prevent overload

	-- Audit
	CreatedAt DATETIMEOFFSET(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	UpdatedAt DATETIMEOFFSET(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	LastRunAt DATETIMEOFFSET(7) NULL,   -- When the last completed run started (UTC)

	-- Claim: set by ClaimDueSchedules so only one worker instance runs a due schedule.
	-- Cleared by LogScheduleExecution; if the instance dies mid-run the claim expires
	-- at ClaimedUntil and the schedule becomes due again.
	ClaimedAt DATETIMEOFFSET(7) NULL,
	ClaimedUntil DATETIMEOFFSET(7) NULL,

	-- One schedule per worker type (CreateOrUpdateSchedule upserts on it)
	CONSTRAINT UQ_WorkerSchedules_WorkerType UNIQUE (WorkerType),
	INDEX IX_WorkerSchedules_Active (IsActive, Frequency)
);

-- =====================================================
-- ScheduleExecutionLogs Table
-- =====================================================
-- Audit trail of schedule executions

CREATE TABLE dbo.ScheduleExecutionLogs (
	Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),

	-- Reference
	ScheduleId UNIQUEIDENTIFIER NOT NULL,

	-- Execution details
	StartedAt DATETIMEOFFSET(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	FinishedAt DATETIMEOFFSET(7) NULL,
	DurationMs INT NULL,

	-- Results
	Status NVARCHAR(50) NOT NULL,       -- 'Success', 'Failed', 'Skipped'
	ItemsRegistered INT NOT NULL DEFAULT 0,
	Error NVARCHAR(2000) NULL,

	-- Context
	HandlerName NVARCHAR(256) NULL,

	FOREIGN KEY (ScheduleId) REFERENCES dbo.WorkerSchedules(Id),
	INDEX IX_ScheduleExecutionLogs_Schedule (ScheduleId, StartedAt),
	INDEX IX_ScheduleExecutionLogs_Status (Status, StartedAt)
);

-- =====================================================
-- Stored Procedures
-- =====================================================

-- Claim the schedules that are due to run
EXEC(N'
CREATE OR ALTER PROCEDURE dbo.ClaimDueSchedules
	@LeaseSeconds INT = 3600,
	@Now DATETIMEOFFSET(7) = NULL  -- Defaults to the database clock; tests pass a fixed time
AS
BEGIN
	SET NOCOUNT ON;

	-- All times are UTC. StartTime is a UTC time of day.
	SET @Now = COALESCE(@Now, SYSUTCDATETIME());
	DECLARE @Today DATE = CAST(@Now AS DATE);
	DECLARE @TimeOfDay TIME = CAST(@Now AS TIME);
	-- 0 = Sunday ... 6 = Saturday regardless of @@DATEFIRST (1900-01-07 was a Sunday)
	DECLARE @DayOfWeek INT = DATEDIFF(DAY, ''19000107'', @Today) % 7;
	DECLARE @DayOfMonth INT = DAY(@Today);
	DECLARE @DayOfYear INT = DATEPART(DAYOFYEAR, @Today);

	-- One UPDATE both claims and returns the due rows. A concurrent caller blocks on the
	-- row lock and re-checks the WHERE clause after the first commits, so it sees the claim
	-- and skips the row: each due schedule is handed to exactly one worker instance.
	UPDATE s
	SET ClaimedAt = @Now,
		ClaimedUntil = DATEADD(SECOND, @LeaseSeconds, @Now)
	OUTPUT
		inserted.Id,
		inserted.WorkerType,
		inserted.Frequency,
		inserted.StartTime,
		inserted.DayOfWeek,
		inserted.DayOfMonth,
		inserted.DayOfYear,
		inserted.MaxConcurrentRegistrations,
		inserted.LastRunAt
	FROM dbo.WorkerSchedules s
	WHERE s.IsActive = 1
		-- Not claimed by another instance (or that claim has expired)
		AND (s.ClaimedUntil IS NULL OR s.ClaimedUntil <= @Now)
		AND (
			-- Hourly: at least an hour since the last run started
			(s.Frequency = ''Hourly''
				AND (s.LastRunAt IS NULL OR s.LastRunAt <= DATEADD(HOUR, -1, @Now)))

			-- Daily / Weekly / Monthly / Yearly: on a matching day, at or after StartTime,
			-- and not already run today
			OR (s.Frequency IN (''Daily'', ''Weekly'', ''Monthly'', ''Yearly'')
				AND @TimeOfDay >= s.StartTime
				AND (s.LastRunAt IS NULL OR CAST(s.LastRunAt AS DATE) < @Today)
				AND (s.Frequency = ''Daily''
					OR (s.Frequency = ''Weekly'' AND s.DayOfWeek = @DayOfWeek)
					OR (s.Frequency = ''Monthly'' AND s.DayOfMonth = @DayOfMonth)
					OR (s.Frequency = ''Yearly'' AND s.DayOfYear = @DayOfYear)))
		);
END;
');

-- Record schedule execution
EXEC(N'
CREATE OR ALTER PROCEDURE dbo.LogScheduleExecution
	@ScheduleId UNIQUEIDENTIFIER,
	@Status NVARCHAR(50),
	@ItemsRegistered INT = 0,
	@Error NVARCHAR(2000) = NULL,
	@HandlerName NVARCHAR(256) = NULL,
	@StartedAt DATETIMEOFFSET(7) = NULL
AS
BEGIN
	SET NOCOUNT ON;
	SET @StartedAt = COALESCE(@StartedAt, SYSUTCDATETIME());

	DECLARE @Now DATETIMEOFFSET(7) = SYSUTCDATETIME();
	DECLARE @DurationMs INT = DATEDIFF(MILLISECOND, @StartedAt, @Now);

	-- Log execution
	INSERT INTO dbo.ScheduleExecutionLogs (
		ScheduleId, StartedAt, FinishedAt, DurationMs, Status, ItemsRegistered, 
		Error, HandlerName
	)
	VALUES (
		@ScheduleId, 
		@StartedAt, 
		@Now,
		@DurationMs,
		@Status,
		@ItemsRegistered,
		@Error,
		@HandlerName
	);

	-- Record when the run started (the claim time, so a run that crosses midnight still
	-- counts for the day it started) and release the claim.
	UPDATE dbo.WorkerSchedules
	SET LastRunAt = COALESCE(ClaimedAt, @Now),
		ClaimedAt = NULL,
		ClaimedUntil = NULL,
		UpdatedAt = @Now
	WHERE Id = @ScheduleId;
END;
');

-- Get execution history
EXEC(N'
CREATE OR ALTER PROCEDURE dbo.GetScheduleHistory
	@ScheduleId UNIQUEIDENTIFIER,
	@Limit INT = 100
AS
BEGIN
	SET NOCOUNT ON;

	SELECT TOP (@Limit)
		Id,
		ScheduleId,
		StartedAt,
		FinishedAt,
		DurationMs,
		Status,
		ItemsRegistered,
		Error,
		HandlerName
	FROM dbo.ScheduleExecutionLogs
	WHERE ScheduleId = @ScheduleId
	ORDER BY StartedAt DESC;
END;
');

-- Success
PRINT 'Scheduler schema created successfully';

