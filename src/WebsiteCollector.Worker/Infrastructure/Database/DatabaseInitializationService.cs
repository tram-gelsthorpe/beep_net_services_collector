using Beep.ServiceBus.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WebsiteCollector.Worker.Infrastructure.Database;

/// <summary>
/// Initializes required SQL databases and schemas on application startup.
/// Ensures all database objects exist before application code accesses them.
/// </summary>
public sealed class DatabaseInitializationService
{
	private readonly string _connectionString;
	private readonly ILogger<DatabaseInitializationService> _logger;

	public DatabaseInitializationService(IConfiguration configuration, ILogger<DatabaseInitializationService> logger)
	{
		_connectionString = configuration.GetConnectionString("Default")
			?? throw new InvalidOperationException("Connection string 'Default' is required");
		_logger = logger;
	}

	public async Task InitializeAsync()
	{
		try
		{
			// Initialize Beep.ServiceBus schema
			var serviceBusDb = new SqlServiceBusDatabase(_connectionString);
			await serviceBusDb.Create();
			_logger.LogInformation("Beep.ServiceBus schema initialized successfully");

			// Initialize WebsiteCollector schema
			await InitializeWebsiteCollectorSchemaAsync();
			_logger.LogInformation("WebsiteCollector schema initialized successfully");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Database initialization failed");
			throw;
		}
	}

	private async Task InitializeWebsiteCollectorSchemaAsync()
	{
		// SQL schema creation for WebsiteCollector tables
		const string schema = @"
			IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Websites')
			BEGIN
				CREATE TABLE [dbo].[Websites] (
					[WebsiteId] UNIQUEIDENTIFIER PRIMARY KEY,
					[Name] NVARCHAR(255) NOT NULL,
					[BaseUrl] NVARCHAR(2048) NOT NULL,
					[CreatedAt] DATETIMEOFFSET NOT NULL,
					[ModifiedAt] DATETIMEOFFSET NOT NULL
				);
			END

			IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'WebsiteSelectors')
			BEGIN
				CREATE TABLE [dbo].[WebsiteSelectors] (
					[WebsiteSelectorsId] UNIQUEIDENTIFIER PRIMARY KEY,
					[WebsiteId] UNIQUEIDENTIFIER NOT NULL,
					[Selector] NVARCHAR(MAX) NOT NULL,
					[DisplayName] NVARCHAR(255),
					[Description] NVARCHAR(MAX),
					[IsActive] BIT NOT NULL DEFAULT 1,
					[CreatedAt] DATETIMEOFFSET NOT NULL,
					[ModifiedAt] DATETIMEOFFSET NOT NULL,
					FOREIGN KEY ([WebsiteId]) REFERENCES [dbo].[Websites]([WebsiteId])
				);
			END

			IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'WebsiteSelectorsMonitor')
			BEGIN
				CREATE TABLE [dbo].[WebsiteSelectorsMonitor] (
					[WebsiteSelectorsMonitorId] UNIQUEIDENTIFIER PRIMARY KEY,
					[WebsiteSelectorsId] UNIQUEIDENTIFIER NOT NULL,
					[MinimumChangePercentage] FLOAT NOT NULL DEFAULT 5.0,
					[MonitoringIntervalSeconds] INT NOT NULL DEFAULT 3600,
					[IsMonitoring] BIT NOT NULL DEFAULT 0,
					[LastExecutionTime] DATETIMEOFFSET,
					[CreatedAt] DATETIMEOFFSET NOT NULL,
					[ModifiedAt] DATETIMEOFFSET NOT NULL,
					FOREIGN KEY ([WebsiteSelectorsId]) REFERENCES [dbo].[WebsiteSelectors]([WebsiteSelectorsId])
				);
			END

			IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'WebsiteSelectorsExecutions')
			BEGIN
				CREATE TABLE [dbo].[WebsiteSelectorsExecutions] (
					[WebsiteSelectorsExecutionId] UNIQUEIDENTIFIER PRIMARY KEY,
					[WebsiteSelectorsId] UNIQUEIDENTIFIER NOT NULL,
					[ExecutedAt] DATETIMEOFFSET NOT NULL,
					[ErrorMessage] NVARCHAR(MAX),
					[HttpStatusCode] INT,
					FOREIGN KEY ([WebsiteSelectorsId]) REFERENCES [dbo].[WebsiteSelectors]([WebsiteSelectorsId])
				);
			END

			IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'WebsiteSelectorsCollectedData')
			BEGIN
				CREATE TABLE [dbo].[WebsiteSelectorsCollectedData] (
					[WebsiteSelectorsCollectedDataId] UNIQUEIDENTIFIER PRIMARY KEY,
					[WebsiteSelectorsExecutionId] UNIQUEIDENTIFIER NOT NULL,
					[ContentHash] NVARCHAR(64) NOT NULL,
					[RawContent] NVARCHAR(MAX),
					[ChangePercentage] FLOAT,
					[CollectedAt] DATETIMEOFFSET NOT NULL,
					PRIMARY KEY ([WebsiteSelectorsCollectedDataId]),
					FOREIGN KEY ([WebsiteSelectorsExecutionId]) REFERENCES [dbo].[WebsiteSelectorsExecutions]([WebsiteSelectorsExecutionId])
				);
			END
		";

		using var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
		await connection.OpenAsync();

		using var command = connection.CreateCommand();
		command.CommandText = schema;
		await command.ExecuteNonQueryAsync();
	}
}
