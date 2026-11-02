-- Add Webhooks table for storing registered subscriber endpoints
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.WebsiteSelectionWebhooks', 'U') IS NOT NULL 
	DROP TABLE dbo.WebsiteSelectionWebhooks;
GO

CREATE TABLE dbo.WebsiteSelectionWebhooks (
	Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
	WebsiteSelectorsId uniqueidentifier NOT NULL REFERENCES dbo.WebsiteSelectors(Id) ON DELETE CASCADE,
	WebhookUrl nvarchar(2048) NOT NULL,
	IsEnabled bit NOT NULL DEFAULT 1,
	RetryAttempts int NOT NULL DEFAULT 0,
	MaxRetryAttempts int NOT NULL DEFAULT 3,
	LastCalledAt datetimeoffset(7) NULL,
	LastStatusCode int NULL,
	LastErrorMessage nvarchar(2000) NULL,
	CreatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	UpdatedAt datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
	CONSTRAINT UQ_WebsiteSelectionWebhooks_Unique UNIQUE (WebsiteSelectorsId, WebhookUrl)
);
GO

CREATE INDEX IX_WebsiteSelectionWebhooks_Enabled ON dbo.WebsiteSelectionWebhooks(WebsiteSelectorsId) 
WHERE IsEnabled = 1;
GO
