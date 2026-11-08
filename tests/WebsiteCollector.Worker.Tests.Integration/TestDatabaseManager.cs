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
			IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'WorkItems')
			BEGIN
				CREATE TABLE dbo.WorkItems (
					Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
					WorkerType NVARCHAR(100) NOT NULL,
					DomainId UNIQUEIDENTIFIER NOT NULL,
					Status NVARCHAR(50) NOT NULL DEFAULT 'PENDING',
					AttemptNumber INT NOT NULL DEFAULT 0,
					MaxAttempts INT NOT NULL DEFAULT 3,
					LeaseOwner UNIQUEIDENTIFIER,
					LeaseToken UNIQUEIDENTIFIER,
					LeaseUntil DATETIMEOFFSET,
					CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
					UpdatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
					CONSTRAINT idx_WorkerType_DomainId UNIQUE (WorkerType, DomainId)
				)
				CREATE INDEX idx_WorkItems_Status_LeaseUntil ON dbo.WorkItems(Status, LeaseUntil)
				CREATE INDEX idx_WorkItems_LeaseOwner ON dbo.WorkItems(LeaseOwner)
			END
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
}
