-- =====================================================
-- Beep.EventBus Database Schema
-- =====================================================
-- Endpoints (where to deliver) and deliveries (what to deliver).
--
-- Status, claiming, leases, retries and back-off for deliveries are handled by
-- Beep.ServiceBus: each delivery has a work item (DomainType 'EventBusDelivery',
-- DomainId = EventBusDeliveries.Id). This schema only records the payload and the
-- result of the last attempt.

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

INSERT INTO dbo.DatabaseVersion (Version)
VALUES ('0');

-- =====================================================
-- EventBus: delivery endpoints
-- =====================================================
-- An owner (e.g. a website selector) can have several endpoints; each event
-- published for the owner is delivered to every enabled endpoint.
CREATE TABLE dbo.EventBus (
    Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
    EventBusOwnerId uniqueidentifier NOT NULL,
    EventBusOwnerDeliveryUrl nvarchar(2048) NOT NULL,
    EventBusOwnerUrlHash AS
        CONVERT(binary(32),
            HASHBYTES('SHA2_256', EventBusOwnerDeliveryUrl)
        ) PERSISTED,
    IsEnabled bit NOT NULL DEFAULT 1,
    TimeoutSeconds int NOT NULL DEFAULT 30
        CONSTRAINT CK_EventBus_TimeoutSeconds
        CHECK (TimeoutSeconds BETWEEN 5 AND 300),
    MaxAttempts int NOT NULL DEFAULT 3
        CONSTRAINT CK_EventBus_MaxAttempts
        CHECK (MaxAttempts BETWEEN 1 AND 10),
    CreatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_EventBus_Unique
        UNIQUE (EventBusOwnerId, EventBusOwnerUrlHash)
);

CREATE INDEX IX_EventBus_Owner ON dbo.EventBus (EventBusOwnerId) WHERE IsEnabled = 1;

-- =====================================================
-- EventBusDeliveries: one row per (endpoint, event)
-- =====================================================
CREATE TABLE dbo.EventBusDeliveries (
    Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
    EventBusId uniqueidentifier NOT NULL
        REFERENCES dbo.EventBus(Id) ON DELETE CASCADE,
    EventId uniqueidentifier NOT NULL,          -- Publisher's id for the event; publishing it again is a no-op
    Payload nvarchar(max) NOT NULL,             -- JSON body that is POSTed
    HttpStatusCode int NULL,                    -- Result of the last attempt
    ErrorMessage nvarchar(2000) NULL,
    CreatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    LastAttemptAt datetimeoffset(7) NULL,
    DeliveredAt datetimeoffset(7) NULL,         -- Set once a 2xx response is received
    CONSTRAINT UQ_EventBusDeliveries_Event UNIQUE (EventBusId, EventId)
);

CREATE INDEX IX_EventBusDeliveries_Event ON dbo.EventBusDeliveries (EventId);
