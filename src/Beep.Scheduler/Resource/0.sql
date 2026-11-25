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
-- Schedules Table
-- =====================================================
-- Defines schedules for each worker type

CREATE TABLE dbo.Schedules (
	Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),

	-- Which IScheduleHandler (its HandlerType) this schedule runs; one schedule per handler
	HandlerType NVARCHAR(100) NOT NULL, -- e.g. 'WebsiteCollector'
	Description NVARCHAR(500) NULL,

	-- Frequency configuration
	Frequency NVARCHAR(50) NOT NULL,    -- 'Daily', 'Weekly', 'Monthly', 'Yearly', 'Hourly'
	StartTime TIME NOT NULL,            -- Local time of day to start, in TimeZone (daily/weekly/monthly/yearly)
	-- Windows time zone id (sys.time_zone_info, e.g. 'SE Asia Standard Time'). StartTime and the
	-- day of week/month/year are read on this zone's calendar, daylight saving included.
	TimeZone NVARCHAR(100) NOT NULL DEFAULT 'UTC',
	DayOfWeek INT NULL,                 -- 0-6 for weekly (0=Sunday)
	DayOfMonth INT NULL,                -- 1-31 for monthly
	DayOfYear INT NULL,                 -- 1-366 for yearly (1=Jan 1, 32=Feb 1, etc.)

	-- Control
	IsActive BIT NOT NULL DEFAULT 1,
	MaxConcurrentRegistrations INT NOT NULL DEFAULT 10,  -- Limit to prevent overload

	-- Audit
	CreatedAt DATETIMEOFFSET(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	UpdatedAt DATETIMEOFFSET(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	-- When the last finished run started (UTC, database clock). Running a schedule is a ServiceBus
	-- work item (DomainType 'WorkerSchedule'); claiming, leases and retries live there.
	LastRunAt DATETIMEOFFSET(7) NULL,

	-- One schedule per handler (CreateOrUpdateSchedule upserts on it)
	CONSTRAINT UQ_Schedules_HandlerType UNIQUE (HandlerType),
	INDEX IX_Schedules_Active (IsActive, Frequency)
);

-- =====================================================
-- ScheduleExecutions Table
-- =====================================================
-- Audit trail of schedule executions

CREATE TABLE dbo.ScheduleExecutions (
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

	FOREIGN KEY (ScheduleId) REFERENCES dbo.Schedules(Id),
	INDEX IX_ScheduleExecutions_Schedule (ScheduleId, StartedAt),
	INDEX IX_ScheduleExecutions_Status (Status, StartedAt)
);

-- =====================================================
-- Stored Procedures
-- =====================================================

-- Get the schedules that are due to run (read-only).
-- SchedulerService enqueues a ServiceBus work item for each; the worker that runs it calls this
-- again with @ScheduleId to check the schedule is still due (a late duplicate item does nothing).
-- CheckedAt is the database time of the check: the run start recorded as LastRunAt.
EXEC(N'
CREATE OR ALTER PROCEDURE dbo.GetDueSchedules
	@ScheduleId UNIQUEIDENTIFIER = NULL,  -- Only this schedule
	@Now DATETIMEOFFSET(7) = NULL         -- Defaults to the database clock; tests pass a fixed time
AS
BEGIN
	SET NOCOUNT ON;

	-- @Now and LastRunAt are instants; each schedule reads them on its own TimeZone''s calendar:
	-- StartTime is a local time of day, and "today" and the day of week/month/year are local.
	SET @Now = COALESCE(@Now, SYSUTCDATETIME());

	SELECT
		s.Id,
		s.HandlerType,
		s.Frequency,
		s.StartTime,
		s.DayOfWeek,
		s.DayOfMonth,
		s.DayOfYear,
		s.MaxConcurrentRegistrations,
		s.LastRunAt,
		@Now AS CheckedAt,
		s.TimeZone
	FROM dbo.Schedules s
	CROSS APPLY (SELECT LocalNow = @Now AT TIME ZONE s.TimeZone) n
	CROSS APPLY (SELECT
		Today = CAST(n.LocalNow AS DATE),
		TimeOfDay = CAST(n.LocalNow AS TIME),
		LastRunDate = CAST(s.LastRunAt AT TIME ZONE s.TimeZone AS DATE)) l
	-- 0 = Sunday ... 6 = Saturday regardless of @@DATEFIRST (1900-01-07 was a Sunday)
	CROSS APPLY (SELECT
		DayOfWeek = DATEDIFF(DAY, ''19000107'', l.Today) % 7,
		DayOfMonth = DAY(l.Today),
		DayOfYear = DATEPART(DAYOFYEAR, l.Today)) d
	WHERE s.IsActive = 1
		AND (@ScheduleId IS NULL OR s.Id = @ScheduleId)
		AND (
			-- Hourly: at least an hour since the last run started
			(s.Frequency = ''Hourly''
				AND (s.LastRunAt IS NULL OR s.LastRunAt <= DATEADD(HOUR, -1, @Now)))

			-- Daily / Weekly / Monthly / Yearly: on a matching day, at or after StartTime,
			-- and not already run today
			OR (s.Frequency IN (''Daily'', ''Weekly'', ''Monthly'', ''Yearly'')
				AND l.TimeOfDay >= s.StartTime
				AND (s.LastRunAt IS NULL OR l.LastRunDate < l.Today)
				AND (s.Frequency = ''Daily''
					OR (s.Frequency = ''Weekly'' AND s.DayOfWeek = d.DayOfWeek)
					OR (s.Frequency = ''Monthly'' AND s.DayOfMonth = d.DayOfMonth)
					OR (s.Frequency = ''Yearly'' AND s.DayOfYear = d.DayOfYear)))
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
	@StartedAt DATETIMEOFFSET(7) = NULL,
	-- 1: the run is finished (succeeded, skipped, or failed on its last attempt), so LastRunAt
	--    is set and the schedule is not due again until its next period.
	-- 0: a failed attempt that the ServiceBus will retry; only the log row is written.
	@CompletesRun BIT = 1
AS
BEGIN
	SET NOCOUNT ON;
	SET @StartedAt = COALESCE(@StartedAt, SYSUTCDATETIME());

	DECLARE @Now DATETIMEOFFSET(7) = SYSUTCDATETIME();
	DECLARE @DurationMs INT = DATEDIFF(MILLISECOND, @StartedAt, @Now);

	-- Log execution
	INSERT INTO dbo.ScheduleExecutions (
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

	-- Record when the run started, so a run that crosses midnight still counts for the day it started.
	IF @CompletesRun = 1
		UPDATE dbo.Schedules
		SET LastRunAt = @StartedAt,
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
	FROM dbo.ScheduleExecutions
	WHERE ScheduleId = @ScheduleId
	ORDER BY StartedAt DESC;
END;
');

-- Success
PRINT 'Scheduler schema created successfully';

