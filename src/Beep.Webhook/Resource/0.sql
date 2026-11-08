-- Webhook Delivery Schema (Generic)
-- Defines generic webhook delivery tracking tables for reuse across different projects
-- Note: Work claiming, lease management, and completion are handled by the generic
-- WorkerFramework stored procedures (see 002_worker_framework_schema.sql)
-- 
-- USAGE INSTRUCTIONS:
-- This schema is designed to be generic and reusable. When deployed in different projects,
-- follow this pattern:
-- 1. Replace table names with project-specific prefixes (if desired)
-- 2. Update foreign key references to match your owner entity tables
-- 3. The WebhooksOwnerId column should reference your owner entity (e.g., WebsiteSelectors.Id)
-- 4. The WebhooksOwnerCollectedDataId should reference your collected data table
-- 5. Ensure index names remain globally unique by including project prefix

SET XACT_ABORT ON;
GO

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

-- Create Webhooks table
-- WebhooksOwnerId: Foreign key to the owner entity (e.g., WebsiteSelectors.Id)
-- This field may be null if webhooks are not tied to a specific owner
CREATE TABLE dbo.Webhooks (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	WebhooksOwnerId uniqueidentifier,
	WebhookUrl nvarchar(2048) NOT NULL,
	IsEnabled bit NOT NULL DEFAULT 1,
	TimeoutSeconds int NOT NULL DEFAULT 30 CONSTRAINT CK_Webhook_Timeout CHECK (TimeoutSeconds BETWEEN 5 AND 300),
	MaxRetryAttempts int NOT NULL DEFAULT 3 CONSTRAINT CK_Webhook_MaxRetries CHECK (MaxRetryAttempts BETWEEN 1 AND 10),
	CreatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	UpdatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	CONSTRAINT UQ_Webhooks_Unique UNIQUE (WebhooksOwnerId, WebhookUrl)
);
GO

-- Create WebhookMonitor table
CREATE TABLE dbo.WebhookMonitor (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	WebhooksId uniqueidentifier NOT NULL UNIQUE REFERENCES dbo.Webhooks(Id) ON DELETE CASCADE,
	LastDeliveredAt datetimeoffset(7) NULL,
	LastStatusCode int NULL,
	LastErrorMessage nvarchar(2000) NULL,
	ConsecutiveFailures int NOT NULL DEFAULT 0,
	IsCircuitBreakerOpen bit NOT NULL DEFAULT 0,
	UpdatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

-- Create WebhookDeliveries table
-- WebhooksOwnerCollectedDataId: Foreign key to the owner's collected data entity
-- (e.g., WebsiteSelectorsCollectedData.Id for WebsiteCollector.Worker)
CREATE TABLE dbo.WebhookDeliveries (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	WebhooksId uniqueidentifier NOT NULL REFERENCES dbo.Webhooks(Id) ON DELETE CASCADE,
	WebhooksOwnerCollectedDataId uniqueidentifier NOT NULL,
	Status varchar(16) NOT NULL DEFAULT 'PENDING',
	AttemptNumber int NOT NULL DEFAULT 1,
	NextRetryAt datetimeoffset(7) NULL,
	HttpStatusCode int NULL,
	ErrorMessage nvarchar(2000) NULL,
	CreatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	QueuedAt datetimeoffset(7) NULL,
	DeliveredAt datetimeoffset(7) NULL,
	CONSTRAINT UQ_Delivery_Unique UNIQUE (WebhooksId, WebhooksOwnerCollectedDataId, AttemptNumber),
	CONSTRAINT CK_Delivery_Status CHECK (Status IN ('PENDING', 'IN_PROGRESS', 'SUCCESS', 'FAILED', 'ABANDONED'))
);
GO

-- Indexes for WebhookDeliveries
-- Note: Index names should be globally unique. When deploying in different projects,
-- consider adding a project prefix to ensure uniqueness across all databases.
-- Example: IX_ProjectName_WebhookDeliveries_Pending
CREATE INDEX IX_WebhookDeliveries_Pending ON dbo.WebhookDeliveries(WebhooksId, NextRetryAt, Status) 
	WHERE Status IN ('PENDING', 'IN_PROGRESS');

CREATE INDEX IX_WebhookDeliveries_Webhook ON dbo.WebhookDeliveries(WebhooksId, DeliveredAt DESC);

CREATE INDEX IX_WebhookDeliveries_CollectedData ON dbo.WebhookDeliveries(WebhooksOwnerCollectedDataId, CreatedAt DESC);
GO
