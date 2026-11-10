-- EventBus Delivery Schema (Generic)
-- Defines generic EventBus delivery tracking tables for reuse across different projects
-- Note: Work claiming, lease management, and completion are handled by the generic
-- WorkerFramework stored procedures (see 002_worker_framework_schema.sql)
-- 
-- USAGE INSTRUCTIONS:
-- This schema is designed to be generic and reusable. When deployed in different projects,
-- follow this pattern:
-- 1. Replace table names with project-specific prefixes (if desired)
-- 2. Update foreign key references to match your owner entity tables
-- 3. The EventBusOwnerId column should reference your owner entity (e.g., WebsiteSelectors.Id)
-- 4. The EventBusOwnerCollectedDataId should reference your collected data table
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

-- Create EventBus table
-- EventBusOwnerId: Foreign key to the owner entity (e.g., WebsiteSelectors.Id)
-- This field may be null if EventBus are not tied to a specific owner
CREATE TABLE dbo.EventBus (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	EventBusOwnerId uniqueidentifier NOT NULL,
	EventBusUrl nvarchar(2048) NOT NULL,
	IsEnabled bit NOT NULL DEFAULT 1,
	TimeoutSeconds int NOT NULL DEFAULT 30 CONSTRAINT CK_EventBus_Timeout CHECK (TimeoutSeconds BETWEEN 5 AND 300),
	MaxRetryAttempts int NOT NULL DEFAULT 3 CONSTRAINT CK_EventBus_MaxRetries CHECK (MaxRetryAttempts BETWEEN 1 AND 10),
	CreatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	UpdatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	CONSTRAINT UQ_EventBus_Unique UNIQUE (EventBusOwnerId, EventBusUrl)
);

-- Create EventBusMonitor table
CREATE TABLE dbo.EventBusMonitor (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	EventBusId uniqueidentifier NOT NULL UNIQUE REFERENCES dbo.EventBus(Id) ON DELETE CASCADE,
	LastDeliveredAt datetimeoffset(7) NULL,
	LastStatusCode int NULL,
	LastErrorMessage nvarchar(2000) NULL,
	ConsecutiveFailures int NOT NULL DEFAULT 0,
	IsCircuitBreakerOpen bit NOT NULL DEFAULT 0,
	UpdatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

-- Create EventBusDeliveries table
-- EventBusOwnerCollectedDataId: Foreign key to the owner's collected data entity
-- (e.g., WebsiteSelectorsCollectedData.Id for WebsiteCollector.Worker)
CREATE TABLE dbo.EventBusDeliveries (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	EventBusId uniqueidentifier NOT NULL REFERENCES dbo.EventBus(Id) ON DELETE CASCADE,
	EventBusOwnerCollectedDataId uniqueidentifier NOT NULL,
	Status varchar(16) NOT NULL DEFAULT 'PENDING',
	AttemptNumber int NOT NULL DEFAULT 1,
	NextRetryAt datetimeoffset(7) NULL,
	HttpStatusCode int NULL,
	ErrorMessage nvarchar(2000) NULL,
	CreatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	QueuedAt datetimeoffset(7) NULL,
	DeliveredAt datetimeoffset(7) NULL,
	CONSTRAINT UQ_Delivery_Unique UNIQUE (EventBusId, EventBusOwnerCollectedDataId, AttemptNumber),
	CONSTRAINT CK_Delivery_Status CHECK (Status IN ('PENDING', 'IN_PROGRESS', 'SUCCESS', 'FAILED', 'ABANDONED'))
);

-- Indexes for EventBusDeliveries
-- Note: Index names should be globally unique. When deploying in different projects,
-- consider adding a project prefix to ensure uniqueness across all databases.
-- Example: IX_ProjectName_EventBusDeliveries_Pending
CREATE INDEX IX_EventBusDeliveries_Pending ON dbo.EventBusDeliveries(EventBusId, NextRetryAt, Status) 
	WHERE Status IN ('PENDING', 'IN_PROGRESS');

CREATE INDEX IX_EventBusDeliveries_EventBus ON dbo.EventBusDeliveries(EventBusId, DeliveredAt DESC);

CREATE INDEX IX_EventBusDeliveries_CollectedData ON dbo.EventBusDeliveries(EventBusOwnerCollectedDataId, CreatedAt DESC);
