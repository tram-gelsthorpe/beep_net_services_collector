-- dbo.User_Notification (A_1_Beep_Global_Default): what a user subscribed to, for any website.
-- One row per user, website and notification type, e.g. ('Currency', 'BankRates'). The website's own
-- choices are JSON in Filter, e.g. {"country":"VN","banks":["BFTVVNVX"],"currencies":["USD","EUR"]}.
-- Token is secret and appears in the confirmation and unsubscribe links. Safe to run again.

IF OBJECT_ID(N'dbo.User_Notification', N'U') IS NULL
BEGIN
	CREATE TABLE dbo.User_Notification (
		Id uniqueidentifier NOT NULL CONSTRAINT DF_unotif_Id DEFAULT NEWSEQUENTIALID(),
		Version int NOT NULL CONSTRAINT DF_unotif_Version DEFAULT 1,
		Modified datetimeoffset NULL,
		ModifiedBy uniqueidentifier NULL,
		Created datetimeoffset NOT NULL,
		CreatedBy uniqueidentifier NOT NULL,
		User_Id uniqueidentifier NOT NULL,
		Licence_Id uniqueidentifier NOT NULL,
		Website varchar(100) NOT NULL,
		NotificationType varchar(100) NOT NULL,
		Channel tinyint NOT NULL CONSTRAINT DF_unotif_Channel DEFAULT 1, -- 1 = email
		Filter nvarchar(max) NULL,
		IsConfirmed bit NOT NULL CONSTRAINT DF_unotif_IsConfirmed DEFAULT 0,
		ConfirmedAt datetimeoffset NULL,
		Token varchar(64) NOT NULL,
		LastSentAt datetimeoffset NULL,
		IsDeleted bit NOT NULL CONSTRAINT DF_unotif_IsDeleted DEFAULT 0, -- unsubscribed
		IsSynchronized bit NOT NULL CONSTRAINT DF_unotif_IsSynchronized DEFAULT 0,
		CONSTRAINT PK_unotif_Id PRIMARY KEY (Id),
		CONSTRAINT FK_unotif_User FOREIGN KEY (User_Id) REFERENCES dbo.[User] (Id),
		CONSTRAINT FK_unotif_Licence FOREIGN KEY (Licence_Id) REFERENCES dbo.Licence (Id),
		CONSTRAINT CK_unotif_Filter CHECK (Filter IS NULL OR ISJSON(Filter) = 1)
	);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'AK_unotif_User_Website_Type' AND object_id = OBJECT_ID(N'dbo.User_Notification'))
	CREATE UNIQUE INDEX AK_unotif_User_Website_Type ON dbo.User_Notification (User_Id, Website, NotificationType);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'AK_unotif_Token' AND object_id = OBJECT_ID(N'dbo.User_Notification'))
	CREATE UNIQUE INDEX AK_unotif_Token ON dbo.User_Notification (Token);
GO

-- Sending: the active subscriptions of a website and type.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IDX_unotif_Website_Type' AND object_id = OBJECT_ID(N'dbo.User_Notification'))
	CREATE INDEX IDX_unotif_Website_Type ON dbo.User_Notification (Website, NotificationType)
		INCLUDE (User_Id, IsConfirmed, IsDeleted);
GO
