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
	LastRunAt DATETIMEOFFSET(7) NULL,

	INDEX IX_WorkerSchedules_WorkerType (WorkerType),
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

-- Get schedules that are due to run
EXEC(N'
CREATE OR ALTER PROCEDURE dbo.GetDueSchedules
	@Now DATETIMEOFFSET(7) = NULL
AS
BEGIN
	SET NOCOUNT ON;
	SET @Now = COALESCE(@Now, SYSUTCDATETIME());

	SELECT 
		s.Id,
		s.WorkerType,
		s.Frequency,
		s.StartTime,
		s.DayOfWeek,
		s.DayOfMonth,
		s.DayOfYear,
		s.MaxConcurrentRegistrations,
		s.LastRunAt,
		DATEDIFF(SECOND, s.LastRunAt, @Now) as SecondsSinceLastRun
	FROM dbo.WorkerSchedules s
	WHERE s.IsActive = 1
		AND (
			-- Daily: run if last run was yesterday or earlier
			(s.Frequency = ''Daily'' 
			 AND (s.LastRunAt IS NULL 
				  OR CAST(s.LastRunAt AS DATE) < CAST(@Now AS DATE)))

			-- Weekly: run if day matches and not run today
			OR (s.Frequency = ''Weekly''
				AND DATEPART(WEEKDAY, @Now) = s.DayOfWeek + 1
				AND (s.LastRunAt IS NULL 
					 OR CAST(s.LastRunAt AS DATE) < CAST(@Now AS DATE)))

			-- Monthly: run if day matches and not run today
			OR (s.Frequency = ''Monthly''
				AND DAY(@Now) = s.DayOfMonth
				AND (s.LastRunAt IS NULL 
					 OR CAST(s.LastRunAt AS DATE) < CAST(@Now AS DATE)))

			-- Yearly: run if month/day matches and not run today
			OR (s.Frequency = ''Yearly''
				AND DATEPART(DAYOFYEAR, @Now) = s.DayOfYear
				AND (s.LastRunAt IS NULL 
					 OR CAST(s.LastRunAt AS DATE) < CAST(@Now AS DATE)))

			-- Hourly: run if last run was > 1 hour ago
			OR (s.Frequency = ''Hourly''
				AND (s.LastRunAt IS NULL 
					 OR DATEDIFF(HOUR, s.LastRunAt, @Now) >= 1))
		)
	ORDER BY s.LastRunAt ASC, s.CreatedAt ASC;
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

	-- Update schedule last run time
	UPDATE dbo.WorkerSchedules
	SET LastRunAt = @Now, UpdatedAt = @Now
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

