
-- EventBus Delivery Schema (Generic)
-- Defines generic EventBus delivery tracking tables
-- for reuse across different projects.
--
-- Work claiming, lease management, and completion
-- are handled by the generic WorkerFramework

SET XACT_ABORT ON;

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
-- EventBus
-- =====================================================

CREATE TABLE dbo.EventBus (
    Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
    EventBusOwnerId uniqueidentifier NOT NULL,
    EventBusOwnerUrl nvarchar(2048) NOT NULL,
    EventBusOwnerUrlHash AS
        CONVERT(binary(32),
            HASHBYTES('SHA2_256', EventBusOwnerUrl)
        ) PERSISTED,
    IsEnabled bit NOT NULL DEFAULT 1,
    TimeoutSeconds int NOT NULL DEFAULT 30
        CONSTRAINT CK_EventBus_Timeout
        CHECK (TimeoutSeconds BETWEEN 5 AND 300),
    MaxRetryAttempts int NOT NULL DEFAULT 3
        CONSTRAINT CK_EventBus_MaxRetries
        CHECK (MaxRetryAttempts BETWEEN 1 AND 10),
    CreatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_EventBus_Unique
        UNIQUE (EventBusOwnerId, EventBusOwnerUrlHash)
);

-- =====================================================
-- EventBusMonitor
-- =====================================================

CREATE TABLE dbo.EventBusMonitor (
    Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
    EventBusId uniqueidentifier NOT NULL UNIQUE
        REFERENCES dbo.EventBus(Id) ON DELETE CASCADE,
    LastDeliveredAt datetimeoffset(7) NULL,
    LastStatusCode int NULL,
    LastErrorMessage nvarchar(2000) NULL,
    ConsecutiveFailures int NOT NULL DEFAULT 0,
    IsCircuitBreakerOpen bit NOT NULL DEFAULT 0,
    CreatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

-- =====================================================
-- EventBusDeliveries
-- =====================================================

CREATE TABLE dbo.EventBusDeliveries (
    Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
    EventBusId uniqueidentifier NOT NULL
        REFERENCES dbo.EventBus(Id) ON DELETE CASCADE,
    EventBusOwnerCollectedDataId uniqueidentifier NOT NULL,
    Status varchar(16) NOT NULL DEFAULT 'PENDING',
    AttemptNumber int NOT NULL DEFAULT 1,
    NextRetryAt datetimeoffset(7) NULL,
    HttpStatusCode int NULL,
    ErrorMessage nvarchar(2000) NULL,
    CreatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    QueuedAt datetimeoffset(7) NULL,
    DeliveredAt datetimeoffset(7) NULL,
    CONSTRAINT UQ_Delivery_Unique
        UNIQUE (
            EventBusId,
            EventBusOwnerCollectedDataId,
            AttemptNumber
        ),
    CONSTRAINT CK_Delivery_Status
        CHECK (
            Status IN (
                'PENDING',
                'IN_PROGRESS',
                'SUCCESS',
                'FAILED',
                'ABANDONED'
            )
        )
);

-- =====================================================
-- Indexes for EventBusDeliveries
-- =====================================================

CREATE INDEX IX_EventBusDeliveries_Pending
ON dbo.EventBusDeliveries (
    EventBusId,
    NextRetryAt,
    Status
)
WHERE Status IN ('PENDING', 'IN_PROGRESS');

CREATE INDEX IX_EventBusDeliveries_EventBus
ON dbo.EventBusDeliveries (
    EventBusId,
    DeliveredAt DESC
);

CREATE INDEX IX_EventBusDeliveries_CollectedData
ON dbo.EventBusDeliveries (
    EventBusOwnerCollectedDataId,
    CreatedAt DESC
);
