-- WebsiteCollector.Worker - WebsiteSelectors Base Schema
-- Core domain tables for website collection and data tracking
--
-- WEBHOOK SCHEMA NOTE:
-- Webhook tables (Webhooks, WebhookMonitor, WebhookDeliveries) are provided by the 
-- generic Beep.WebhookBus schema. This project uses webhooks through:
--   - dbo.Webhooks with WebhooksOwnerId = WebsiteSelectors.Id
--   - dbo.WebhookDeliveries with WebhooksOwnerCollectedDataId = WebsiteSelectorsCollectedData.Id
--
-- Deployment order:
-- 1. Beep.WebhookBus schema (creates generic webhook tables)
-- 2. This file (creates WebsiteSelectors domain tables)

SET XACT_ABORT ON;

-- =====================================================
-- Database Version Tracking
-- =====================================================
-- Tracks the schema version and deployment information
CREATE TABLE dbo.DatabaseVersion (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	Version nvarchar(20) NOT NULL UNIQUE,  -- e.g., '0', '1'
	CreatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	UpdatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

-- Insert initial database version
INSERT INTO dbo.DatabaseVersion (Version)
VALUES ('0');

-- =====================================================
-- Create Websites table - Main parent table
-- =====================================================
-- Stores website configurations that can have multiple selectors
CREATE TABLE dbo.Websites (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	Name nvarchar(200) NOT NULL,
	Url nvarchar(2048) NOT NULL,
	IsEnabled bit NOT NULL DEFAULT 1,
	CreatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	UpdatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

-- =====================================================
-- Create WebsiteSelectors table
-- =====================================================
-- Defines specific collection endpoints and selectors for each website
CREATE TABLE dbo.WebsiteSelectors (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	WebsitesId uniqueidentifier NOT NULL REFERENCES dbo.Websites(Id) ON DELETE CASCADE,
	CollectionUrl nvarchar(2048) NOT NULL,
	CollectionType varchar(10) NOT NULL CONSTRAINT CK_Selector_Type CHECK (CollectionType IN ('HTML', 'JSON', 'XML', 'RAW')),
	ContentSelector nvarchar(1000) NULL,
	TableSelector nvarchar(1000) NULL,
	IntervalSeconds int NOT NULL CONSTRAINT CK_Selector_Interval CHECK (IntervalSeconds BETWEEN 10 AND 2592000),
	IsEnabled bit NOT NULL DEFAULT 1,
	CreatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	UpdatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	CONSTRAINT UQ_WebsiteSelectors_Unique UNIQUE (WebsitesId, CollectionUrl, CollectionType, ContentSelector, TableSelector)
);

CREATE INDEX IX_WebsiteSelectors_Website ON dbo.WebsiteSelectors(WebsitesId) WHERE IsEnabled = 1;

-- =====================================================
-- Create WebsiteSelectorsMonitor table
-- =====================================================
-- Tracks execution state, leasing, and scheduling for each selector
CREATE TABLE dbo.WebsiteSelectorsMonitor (
	Id uniqueidentifier PRIMARY KEY REFERENCES dbo.WebsiteSelectors(Id) ON DELETE CASCADE,
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
	UpdatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	RowVer rowversion NOT NULL
);

CREATE INDEX IX_WebsiteSelectorsMonitor_Due ON dbo.WebsiteSelectorsMonitor(NextRunAt, Id) WHERE LeaseOwner IS NULL;

-- =====================================================
-- Create WebsiteSelectorsExecutions table
-- =====================================================
-- Records individual execution attempts for each collection run
CREATE TABLE dbo.WebsiteSelectorsExecutions (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	WebsiteSelectorsId uniqueidentifier NOT NULL REFERENCES dbo.WebsiteSelectors(Id) ON DELETE CASCADE,
	LeaseToken uniqueidentifier NOT NULL UNIQUE,
	WorkerId uniqueidentifier NOT NULL,
	StartedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	FinishedAt datetimeoffset(7) NULL,
	Status varchar(16) NOT NULL CONSTRAINT CK_Exec_Status CHECK (Status IN ('Running', 'Succeeded', 'Failed', 'Abandoned')),
	AttemptNumber int NOT NULL,
	HttpStatus int NULL,
	ErrorMessage nvarchar(2000) NULL
);

CREATE INDEX IX_Exec_WebsiteSelectorsTime ON dbo.WebsiteSelectorsExecutions(WebsiteSelectorsId, StartedAt DESC);

-- =====================================================
-- Create WebsiteSelectorsCollectedData table
-- =====================================================
-- Stores collected content from successful executions
CREATE TABLE dbo.WebsiteSelectorsCollectedData (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	WebsiteSelectorsExecutionsId uniqueidentifier NOT NULL REFERENCES dbo.WebsiteSelectorsExecutions(Id) ON DELETE CASCADE,
	ContentHash binary(32) NOT NULL,
	ResponseContent nvarchar(max) NOT NULL,
	ExtractedJson nvarchar(max) NULL,
	CollectedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	CONSTRAINT UQ_CollectedData_Hash UNIQUE(WebsiteSelectorsExecutionsId, ContentHash)
);

CREATE INDEX IX_CollectedData_Execution ON dbo.WebsiteSelectorsCollectedData(WebsiteSelectorsExecutionsId, CollectedAt DESC);