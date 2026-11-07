-- WebsiteCollector.Worker - Webhook Integration Schema
-- Integrates the generic Beep.WebhookBus schema with WebsiteSelectors
-- This migration defines project-specific webhook tables that reference Beep.WebhookBus
-- and tie webhooks to WebsiteSelectors
--
-- This script should run AFTER:
-- 1. Beep.WebhookBus schema is deployed (provides base webhook tables)
-- 2. 0_website_selectors_schema.sql is executed (provides WebsiteSelectors tables)

SET XACT_ABORT ON;
GO

-- =====================================================
-- Drop existing objects if they exist (in dependency order)
-- =====================================================
IF OBJECT_ID('dbo.WebsiteSelectorsWebhookDeliveries', 'U') IS NOT NULL DROP TABLE dbo.WebsiteSelectorsWebhookDeliveries;
GO
IF OBJECT_ID('dbo.WebsiteSelectorsWebhookMonitor', 'U') IS NOT NULL DROP TABLE dbo.WebsiteSelectorsWebhookMonitor;
GO
IF OBJECT_ID('dbo.WebsiteSelectorsWebhooks', 'U') IS NOT NULL DROP TABLE dbo.WebsiteSelectorsWebhooks;
GO

-- =====================================================
-- Create WebsiteSelectorsWebhooks table
-- =====================================================
-- Project-specific wrapper around Beep.WebhookBus Webhooks
-- Links webhooks to WebsiteSelectors
CREATE TABLE dbo.WebsiteSelectorsWebhooks (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	WebsiteSelectorsId uniqueidentifier NOT NULL REFERENCES dbo.WebsiteSelectors(Id) ON DELETE CASCADE,
	WebhookUrl nvarchar(2048) NOT NULL,
	IsEnabled bit NOT NULL DEFAULT 1,
	TimeoutSeconds int NOT NULL DEFAULT 30 CONSTRAINT CK_Webhook_Timeout CHECK (TimeoutSeconds BETWEEN 5 AND 300),
	MaxRetryAttempts int NOT NULL DEFAULT 3 CONSTRAINT CK_Webhook_MaxRetries CHECK (MaxRetryAttempts BETWEEN 1 AND 10),
	CreatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	UpdatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	CONSTRAINT UQ_WebsiteSelectorsWebhooks_Unique UNIQUE (WebsiteSelectorsId, WebhookUrl)
);
GO

-- =====================================================
-- Create WebsiteSelectorsWebhookMonitor table
-- =====================================================
-- Tracks webhook delivery health and circuit breaker state
-- Follows Beep.WebhookBus pattern for monitoring
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

-- =====================================================
-- Create WebsiteSelectorsWebhookDeliveries table
-- =====================================================
-- Records webhook delivery attempts for collected data
-- References WebsiteSelectorsCollectedData for tracking what was delivered
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

-- =====================================================
-- Create Indexes for Performance
-- =====================================================
-- Optimizes queries for pending deliveries that need processing
CREATE INDEX IX_WebhookDeliveries_Pending 
	ON dbo.WebsiteSelectorsWebhookDeliveries(WebsiteSelectorsWebhooksId, NextRetryAt, Status) 
	WHERE Status IN ('PENDING', 'IN_PROGRESS');
GO

-- Optimizes queries for delivery history
CREATE INDEX IX_WebhookDeliveries_Webhook 
	ON dbo.WebsiteSelectorsWebhookDeliveries(WebsiteSelectorsWebhooksId, DeliveredAt DESC);
GO

-- Optimizes queries for collected data delivery tracking
CREATE INDEX IX_WebhookDeliveries_CollectedData 
	ON dbo.WebsiteSelectorsWebhookDeliveries(CollectedDataId, CreatedAt DESC);
GO
