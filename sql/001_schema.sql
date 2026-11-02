SET XACT_ABORT ON;
GO
CREATE TABLE dbo.Websites (
 Id bigint IDENTITY PRIMARY KEY,
 Name nvarchar(200) NOT NULL,
 Url nvarchar(2048) NOT NULL,
 CollectionType varchar(10) NOT NULL CONSTRAINT CK_Website_Type CHECK (CollectionType IN ('HTML','JSON','XML','RAW')),
 IntervalSeconds int NOT NULL CONSTRAINT CK_Website_Interval CHECK (IntervalSeconds BETWEEN 10 AND 2592000),
 IsEnabled bit NOT NULL CONSTRAINT DF_Website_Enabled DEFAULT 1,
 NextRunAt datetime2(3) NOT NULL CONSTRAINT DF_Website_Next DEFAULT SYSUTCDATETIME(),
 TimeoutSeconds int NOT NULL CONSTRAINT DF_Website_Timeout DEFAULT 30 CONSTRAINT CK_Website_Timeout CHECK (TimeoutSeconds BETWEEN 1 AND 300),
 MaxAttempts int NOT NULL CONSTRAINT DF_Website_Attempts DEFAULT 5 CONSTRAINT CK_Website_Attempts CHECK (MaxAttempts BETWEEN 1 AND 20),
 AttemptCount int NOT NULL CONSTRAINT DF_Website_AttemptCount DEFAULT 0,
 LeaseOwner uniqueidentifier NULL,
 LeaseToken uniqueidentifier NULL,
 LeaseUntil datetime2(3) NULL,
 LastStartedAt datetime2(3) NULL,
 LastSucceededAt datetime2(3) NULL,
 LastError nvarchar(2000) NULL,
 CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Website_Created DEFAULT SYSUTCDATETIME(),
 RowVer rowversion NOT NULL
);
GO
CREATE INDEX IX_Websites_Due ON dbo.Websites(NextRunAt,Id) INCLUDE (IntervalSeconds,TimeoutSeconds,MaxAttempts,AttemptCount) WHERE IsEnabled = 1;
GO
CREATE TABLE dbo.WebsiteSelectors (
 Id bigint IDENTITY PRIMARY KEY,
 WebsiteId bigint NOT NULL REFERENCES dbo.Websites(Id),
 FieldName nvarchar(128) NOT NULL,
 CssSelector nvarchar(1000) NOT NULL,
 AttributeName nvarchar(128) NULL,
 IsEnabled bit NOT NULL DEFAULT 1,
 CONSTRAINT UQ_WebsiteSelectors_Field UNIQUE(WebsiteId,FieldName)
);
GO
CREATE TABLE dbo.WebsiteExecutions (
 Id bigint IDENTITY PRIMARY KEY,
 WebsiteId bigint NOT NULL REFERENCES dbo.Websites(Id),
 LeaseToken uniqueidentifier NOT NULL UNIQUE,
 WorkerId uniqueidentifier NOT NULL,
 StartedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
 FinishedAt datetime2(3) NULL,
 Status varchar(16) NOT NULL CONSTRAINT CK_Exec_Status CHECK (Status IN ('Running','Succeeded','Failed','Abandoned')),
 AttemptNumber int NOT NULL,
 HttpStatus int NULL,
 ErrorMessage nvarchar(2000) NULL,
 CONSTRAINT UQ_Exec_IdWebsite UNIQUE(Id,WebsiteId)
);
GO
CREATE INDEX IX_Exec_WebsiteTime ON dbo.WebsiteExecutions(WebsiteId,StartedAt DESC);
GO
CREATE TABLE dbo.CollectedData (
 Id bigint IDENTITY PRIMARY KEY,
 WebsiteId bigint NOT NULL REFERENCES dbo.Websites(Id),
 ExecutionId bigint NOT NULL,
 ContentHash binary(32) NOT NULL,
 ResponseContent nvarchar(max) NOT NULL,
 ExtractedJson nvarchar(max) NULL,
 CollectedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
 CONSTRAINT FK_Data_Exec FOREIGN KEY(ExecutionId,WebsiteId) REFERENCES dbo.WebsiteExecutions(Id,WebsiteId),
 CONSTRAINT UQ_Data_WebsiteHash UNIQUE(WebsiteId,ContentHash)
);
GO
CREATE OR ALTER PROCEDURE dbo.ClaimWebsites
 @WorkerId uniqueidentifier,
 @BatchSize int,
 @LeaseSeconds int
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;
 IF @BatchSize NOT BETWEEN 1 AND 500 OR @LeaseSeconds NOT BETWEEN 60 AND 3600 THROW 50001,'Invalid claim settings',1;
 DECLARE @now datetime2(3) = SYSUTCDATETIME();
 ;WITH due AS (
   SELECT TOP (@BatchSize) * FROM dbo.Websites WITH (UPDLOCK, READPAST, READCOMMITTEDLOCK, ROWLOCK)
   WHERE IsEnabled=1 AND NextRunAt<=@now AND (LeaseUntil IS NULL OR LeaseUntil<=@now)
   ORDER BY NextRunAt,Id
 )
 UPDATE due SET LeaseOwner=@WorkerId, LeaseToken=NEWID(), LeaseUntil=DATEADD(second,@LeaseSeconds,@now),
 LastStartedAt=@now, AttemptCount=AttemptCount+1
 OUTPUT inserted.Id,inserted.Name,inserted.Url,inserted.CollectionType,inserted.IntervalSeconds,
 inserted.TimeoutSeconds,inserted.MaxAttempts,inserted.AttemptCount,inserted.LeaseToken;
END;
GO
CREATE OR ALTER PROCEDURE dbo.CompleteWebsite
 @WebsiteId bigint,@LeaseToken uniqueidentifier,@Succeeded bit,
 @HttpStatus int=NULL,@Error nvarchar(2000)=NULL,
 @ContentHash binary(32)=NULL,@ResponseContent nvarchar(max)=NULL,@ExtractedJson nvarchar(max)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRAN;
 DECLARE @now datetime2(3)=SYSUTCDATETIME(), @interval int, @attempt int, @max int;
 SELECT @interval=IntervalSeconds,@attempt=AttemptCount,@max=MaxAttempts
 FROM dbo.Websites WITH(UPDLOCK,HOLDLOCK)
 WHERE Id=@WebsiteId AND LeaseToken=@LeaseToken AND LeaseUntil>@now;
 IF @interval IS NULL BEGIN ROLLBACK; RETURN -1; END;
 IF @Succeeded=1
 BEGIN
  IF @ContentHash IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.CollectedData WITH(UPDLOCK,HOLDLOCK) WHERE WebsiteId=@WebsiteId AND ContentHash=@ContentHash)
   INSERT dbo.CollectedData(WebsiteId,ExecutionId,ContentHash,ResponseContent,ExtractedJson)
   SELECT @WebsiteId,Id,@ContentHash,@ResponseContent,@ExtractedJson FROM dbo.WebsiteExecutions WHERE LeaseToken=@LeaseToken;
  UPDATE dbo.Websites SET AttemptCount=0, LastSucceededAt=@now,LastError=NULL,
   NextRunAt=DATEADD(second,@interval,@now),LeaseOwner=NULL,LeaseToken=NULL,LeaseUntil=NULL WHERE Id=@WebsiteId;
 END
 ELSE
 BEGIN
  DECLARE @delay int;
  IF @attempt>=@max SET @delay=@interval;
  ELSE SET @delay=CONVERT(int,POWER(CONVERT(float,2),IIF(@attempt>10,10,@attempt-1))*15)+ABS(CHECKSUM(NEWID())%15);
  UPDATE dbo.Websites SET AttemptCount=IIF(@attempt>=@max,0,@attempt),LastError=@Error,
   NextRunAt=DATEADD(second,@delay,@now),LeaseOwner=NULL,LeaseToken=NULL,LeaseUntil=NULL WHERE Id=@WebsiteId;
 END;
 UPDATE dbo.WebsiteExecutions SET FinishedAt=@now,Status=IIF(@Succeeded=1,'Succeeded','Failed'),HttpStatus=@HttpStatus,ErrorMessage=@Error WHERE LeaseToken=@LeaseToken AND Status='Running';
 COMMIT;
 RETURN 0;
END;
GO
