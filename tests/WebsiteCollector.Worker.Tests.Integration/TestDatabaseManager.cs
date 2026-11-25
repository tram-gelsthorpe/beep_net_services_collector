using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace WebsiteCollector.Worker.Tests.Integration;

/// <summary>
/// Manages test database setup and teardown for integration tests.
/// Provides test data seeding and cleanup utilities.
/// </summary>
internal sealed class TestDatabaseManager
{
	private readonly IConfiguration _configuration;
	private readonly string _connectionString;
	private readonly string _databaseName = "WebsiteCollectorTests";

	public TestDatabaseManager(IConfiguration configuration)
	{
		_configuration = configuration;
		_connectionString = _configuration.GetConnectionString("Default") 
			?? throw new InvalidOperationException("Missing Default connection string in test configuration");
	}

	/// <summary>
	/// Initialize test database (create if not exists, then create schema).
	/// </summary>
	public async Task InitializeAsync(CancellationToken ct = default)
	{
		// First, create the database if it doesn't exist
		await CreateDatabaseIfNotExistsAsync(ct);

		// Then create the tables in the test database
		await using var connection = new SqlConnection(_connectionString);
		await connection.OpenAsync(ct);

		// Create infrastructure tables if they don't exist
		await CreateWorkItemsTableAsync(connection, ct);
		await CreateWebsiteSelectorsTableAsync(connection, ct);
		await CreateWebhookDeliveriesTableAsync(connection, ct);

		// Create stored procedures used by WorkItemRepository
		await CreateStoredProceduresAsync(connection, ct);
	}

	/// <summary>
	/// Create the test database if it doesn't exist.
	/// </summary>
	private async Task CreateDatabaseIfNotExistsAsync(CancellationToken ct = default)
	{
		try
		{
			// Extract master connection string
			var builder = new SqlConnectionStringBuilder(_connectionString);
			builder.InitialCatalog = "master";
			var masterConnString = builder.ConnectionString;

			await using var connection = new SqlConnection(masterConnString);
			await connection.OpenAsync(ct);

			var createDbSql = @$"
				IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = '{_databaseName}')
				BEGIN
					CREATE DATABASE [{_databaseName}]
				END
			";

			await connection.ExecuteAsync(
				new CommandDefinition(createDbSql, cancellationToken: ct));
		}
		catch (Exception ex)
		{
			// Log but don't fail - database might already exist or user may not have permissions
			System.Diagnostics.Debug.WriteLine($"Warning: Could not ensure database exists: {ex.Message}");
		}
	}

	/// <summary>
	/// Clean up all test data from the database.
	/// </summary>
	public async Task CleanupAsync(CancellationToken ct = default)
	{
		try
		{
			await using var connection = new SqlConnection(_connectionString);
			await connection.OpenAsync(ct);

			// Delete in correct order to respect foreign keys
			const string cleanupSql = @"
				IF OBJECT_ID('dbo.WebhookDeliveries', 'U') IS NOT NULL
					DELETE FROM dbo.WebhookDeliveries;
				IF OBJECT_ID('dbo.WebsiteSelectors', 'U') IS NOT NULL
					DELETE FROM dbo.WebsiteSelectors;
				IF OBJECT_ID('dbo.WorkItems', 'U') IS NOT NULL
					DELETE FROM dbo.WorkItems;
			";

			await connection.ExecuteAsync(new CommandDefinition(cleanupSql, cancellationToken: ct));
		}
		catch (Exception ex)
		{
			// Log but don't fail cleanup
			System.Diagnostics.Debug.WriteLine($"Warning: Could not clean up database: {ex.Message}");
		}
	}

	/// <summary>
	/// Create test WorkItems table (if not exists).
	/// Mirrors Beep.ServiceBus.WorkItems structure.
	/// </summary>
	private async Task CreateWorkItemsTableAsync(SqlConnection connection, CancellationToken ct)
	{
		const string sql = @"
			IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'WorkItems')
			BEGIN
				DROP TABLE dbo.WorkItems
			END

			CREATE TABLE dbo.WorkItems (
				Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
				WorkerType NVARCHAR(100) NOT NULL,
				DomainId UNIQUEIDENTIFIER NOT NULL,
				DomainType NVARCHAR(100) NOT NULL,
				Status NVARCHAR(50) NOT NULL DEFAULT 'PENDING',
				AttemptNumber INT NOT NULL DEFAULT 0,
				MaxAttempts INT NOT NULL DEFAULT 3,
				WorkerId UNIQUEIDENTIFIER,
				LeaseToken UNIQUEIDENTIFIER,
				LeaseUntil DATETIMEOFFSET,
				CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
				UpdatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
				CONSTRAINT idx_WorkerType_DomainId UNIQUE (WorkerType, DomainId)
			)
			CREATE INDEX idx_WorkItems_Status_LeaseUntil ON dbo.WorkItems(Status, LeaseUntil)
			CREATE INDEX idx_WorkItems_WorkerId ON dbo.WorkItems(WorkerId)
			";

		await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
	}

	/// <summary>
	/// Create test WebsiteSelectors table (if not exists).
	/// </summary>
	private async Task CreateWebsiteSelectorsTableAsync(SqlConnection connection, CancellationToken ct)
	{
		const string sql = @"
			IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'WebsiteSelectors')
			BEGIN
				CREATE TABLE dbo.WebsiteSelectors (
					Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
					WebsitesId UNIQUEIDENTIFIER NOT NULL,
					CollectionUrl NVARCHAR(2048) NOT NULL,
					CollectionType NVARCHAR(100) NOT NULL,
					ContentSelector NVARCHAR(500),
					TableSelector NVARCHAR(500),
					IntervalSeconds INT NOT NULL,
					IsEnabled BIT NOT NULL DEFAULT 1,
					CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME()
				)
				CREATE INDEX idx_WebsiteSelectors_IsEnabled ON dbo.WebsiteSelectors(IsEnabled)
			END
		";

		await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
	}

	/// <summary>
	/// Create test WebhookDeliveries table (if not exists).
	/// </summary>
	private async Task CreateWebhookDeliveriesTableAsync(SqlConnection connection, CancellationToken ct)
	{
		const string sql = @"
			IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'WebhookDeliveries')
			BEGIN
				CREATE TABLE dbo.WebhookDeliveries (
					Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
					WebhooksId UNIQUEIDENTIFIER NOT NULL,
					WebhooksOwnerCollectedDataId UNIQUEIDENTIFIER,
					Status NVARCHAR(50) NOT NULL DEFAULT 'PENDING',
					AttemptNumber INT NOT NULL DEFAULT 1,
					NextRetryAt DATETIMEOFFSET,
					CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
					UpdatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME()
				)
				CREATE INDEX idx_WebhookDeliveries_Status ON dbo.WebhookDeliveries(Status)
			END
		";

		await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
	}

	/// <summary>
	/// Get a database connection for test operations.
	/// </summary>
	public SqlConnection OpenConnection() => new(_connectionString);

	/// <summary>
	/// Create minimal stored procedures needed by WorkItemRepository for tests.
	/// </summary>
	private async Task CreateStoredProceduresAsync(SqlConnection connection, CancellationToken ct)
	{
		const string sql = @"
			-- ClaimWorkItems: Claim available work items
			IF OBJECT_ID('dbo.ClaimWorkItems', 'P') IS NOT NULL DROP PROCEDURE dbo.ClaimWorkItems;
			EXEC(N'CREATE PROCEDURE dbo.ClaimWorkItems
				@WorkerType nvarchar(100),
				@WorkerId uniqueidentifier,
				@BatchSize int,
				@LeaseSeconds int
			AS
			BEGIN
				SET NOCOUNT ON;
				DECLARE @now datetimeoffset(7) = SYSUTCDATETIME();
				DECLARE @leaseUntil datetimeoffset(7) = DATEADD(second, @LeaseSeconds, @now);

				UPDATE TOP(@BatchSize) dbo.WorkItems
				SET WorkerId = @WorkerId,
					LeaseToken = NEWID(),
					LeaseUntil = @leaseUntil,
					Status = ''RUNNING''
				OUTPUT inserted.Id as WorkItemId,
					   inserted.DomainId,
					   inserted.DomainType,
					   inserted.LeaseToken,
					   inserted.AttemptNumber,
					   inserted.MaxAttempts
				WHERE Status = ''PENDING''
					AND WorkerType = @WorkerType
					AND (WorkerId IS NULL OR LeaseUntil <= @now);
			END;');

			-- RenewWorkItemLease: Extend lease duration
			IF OBJECT_ID('dbo.RenewWorkItemLease', 'P') IS NOT NULL DROP PROCEDURE dbo.RenewWorkItemLease;
			EXEC(N'CREATE PROCEDURE dbo.RenewWorkItemLease
				@WorkItemId uniqueidentifier,
				@LeaseToken uniqueidentifier,
				@LeaseSeconds int
			AS
			BEGIN
				SET NOCOUNT ON;
				DECLARE @now datetimeoffset(7) = SYSUTCDATETIME();
				DECLARE @newLeaseUntil datetimeoffset(7) = DATEADD(second, @LeaseSeconds, @now);

				UPDATE dbo.WorkItems
				SET LeaseUntil = @newLeaseUntil
				WHERE Id = @WorkItemId AND LeaseToken = @LeaseToken;

				SELECT CAST(@@ROWCOUNT as bit) as Success;
			END;');

			-- CompleteWorkItem: Mark work item as completed
			IF OBJECT_ID('dbo.CompleteWorkItem', 'P') IS NOT NULL DROP PROCEDURE dbo.CompleteWorkItem;
			EXEC(N'CREATE PROCEDURE dbo.CompleteWorkItem
				@WorkItemId uniqueidentifier,
				@LeaseToken uniqueidentifier,
				@Succeeded bit,
				@Error nvarchar(2000) = NULL,
				@ResultData nvarchar(max) = NULL,
				@BackoffStrategyName nvarchar(50) = ''exponential''
			AS
			BEGIN
				SET NOCOUNT ON;
				DECLARE @now datetimeoffset(7) = SYSUTCDATETIME();

				UPDATE dbo.WorkItems
				SET Status = CASE WHEN @Succeeded = 1 THEN ''SUCCEEDED'' ELSE ''FAILED'' END,
					WorkerId = NULL,
					LeaseToken = NULL,
					LeaseUntil = NULL
				WHERE Id = @WorkItemId AND LeaseToken = @LeaseToken;

				SELECT CAST(@@ROWCOUNT as bit) as Success;
			END;');

			-- RecordWorkItemAttempt: Record attempt details
			IF OBJECT_ID('dbo.RecordWorkItemAttempt', 'P') IS NOT NULL DROP PROCEDURE dbo.RecordWorkItemAttempt;
			EXEC(N'CREATE PROCEDURE dbo.RecordWorkItemAttempt
				@WorkItemId uniqueidentifier,
				@AttemptNumber int,
				@StatusSnapshot nvarchar(50),
				@ErrorMessage nvarchar(2000) = NULL,
				@ResultData nvarchar(max) = NULL
			AS
			BEGIN
				SET NOCOUNT ON;
				-- Minimal implementation for testing
			END;');

			-- ReleaseWorkItemLease: Release lease without changing status
			IF OBJECT_ID('dbo.ReleaseWorkItemLease', 'P') IS NOT NULL DROP PROCEDURE dbo.ReleaseWorkItemLease;
			EXEC(N'CREATE PROCEDURE dbo.ReleaseWorkItemLease
				@WorkItemId uniqueidentifier,
				@LeaseToken uniqueidentifier
			AS
			BEGIN
				SET NOCOUNT ON;
				UPDATE dbo.WorkItems
				SET WorkerId = NULL,
					LeaseToken = NULL,
					LeaseUntil = NULL
				WHERE Id = @WorkItemId AND LeaseToken = @LeaseToken;

				SELECT CAST(@@ROWCOUNT as bit) as Success;
			END;');

			-- GetWorkItem: Get work item details
			IF OBJECT_ID('dbo.GetWorkItem', 'P') IS NOT NULL DROP PROCEDURE dbo.GetWorkItem;
			EXEC(N'CREATE PROCEDURE dbo.GetWorkItem
				@WorkItemId uniqueidentifier
			AS
			BEGIN
				SET NOCOUNT ON;
				SELECT 
					Id,
					WorkerType,
					DomainId,
					DomainType,
					Status,
					WorkerId,
					AttemptNumber,
					MaxAttempts,
					LeaseUntil
				FROM dbo.WorkItems
				WHERE Id = @WorkItemId;
			END;');
		";

		await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
	}
}
