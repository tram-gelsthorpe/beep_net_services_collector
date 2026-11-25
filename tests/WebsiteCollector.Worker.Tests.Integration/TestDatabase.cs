using Beep.Database;
using Beep.ServiceBus;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace WebsiteCollector.Worker.Tests.Integration;

/// <summary>
/// Creates the ServiceBus and Collector test databases once per test run using the
/// production schema initializers, and clears their data between tests.
/// Connection strings come from appsettings.json and can be overridden with
/// ConnectionStrings__ServiceBus / ConnectionStrings__Collector environment variables.
/// </summary>
[TestClass]
public static class TestDatabase
{
	/// <summary>Same schema name the worker uses in production.</summary>
	public const string ServiceBusSchema = "ServiceBus";

	public static string ServiceBusConnectionString { get; private set; } = string.Empty;
	public static string CollectorConnectionString { get; private set; } = string.Empty;

	[AssemblyInitialize]
	public static async Task AssemblyInitializeAsync(TestContext _)
	{
		var configuration = new ConfigurationBuilder()
			.SetBasePath(AppContext.BaseDirectory)
			.AddJsonFile("appsettings.json", optional: false)
			.AddEnvironmentVariables()
			.Build();

		ServiceBusConnectionString = configuration.GetConnectionString("ServiceBus")
			?? throw new InvalidOperationException("Missing ServiceBus connection string");
		CollectorConnectionString = configuration.GetConnectionString("Collector")
			?? throw new InvalidOperationException("Missing Collector connection string");

		// Start from a clean slate so the "create" path of the initializers is exercised.
		await DropDatabaseAsync(ServiceBusConnectionString);
		await DropDatabaseAsync(CollectorConnectionString);

		await CreateAsync();
	}

	[AssemblyCleanup]
	public static async Task AssemblyCleanupAsync()
	{
		await DropDatabaseAsync(ServiceBusConnectionString);
		await DropDatabaseAsync(CollectorConnectionString);
	}

	/// <summary>Runs the production initializers (create on first call, upgrade check afterwards).</summary>
	public static async Task CreateAsync()
	{
		await new SqlDatabaseInitializer(ServiceBusConnectionString, typeof(WorkItemRepository).Assembly, NullLogger.Instance)
			.CreateOrUpdateAsync(ServiceBusSchema);
		await new SqlDatabaseInitializer(CollectorConnectionString, typeof(CreationDatabaseService).Assembly, NullLogger.Instance)
			.CreateOrUpdateAsync(CreationDatabaseService.SelectorSchema);
	}

	public static async Task ClearDataAsync()
	{
		await using (var serviceBus = new SqlConnection(ServiceBusConnectionString))
		{
			await serviceBus.ExecuteAsync($"""
				DELETE FROM {ServiceBusSchema}.WorkItemHeartbeats;
				DELETE FROM {ServiceBusSchema}.WorkItemAttempts;
				DELETE FROM {ServiceBusSchema}.WorkItems;
				""");
		}

		await using var collector = new SqlConnection(CollectorConnectionString);
		await collector.ExecuteAsync("DELETE FROM dbo.Websites;"); // cascades to selectors, monitor, executions, data
	}

	public static SqlConnection OpenServiceBus() => new(ServiceBusConnectionString);
	public static SqlConnection OpenCollector() => new(CollectorConnectionString);

	private static async Task DropDatabaseAsync(string connectionString)
	{
		if (string.IsNullOrEmpty(connectionString))
			return;

		var builder = new SqlConnectionStringBuilder(connectionString);
		var databaseName = builder.InitialCatalog;
		builder.InitialCatalog = "master";

		SqlConnection.ClearAllPools();

		await using var connection = new SqlConnection(builder.ConnectionString);
		await connection.ExecuteAsync("""
			IF DB_ID(@name) IS NOT NULL
			BEGIN
				DECLARE @sql nvarchar(max) =
					N'ALTER DATABASE ' + QUOTENAME(@name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; ' +
					N'DROP DATABASE ' + QUOTENAME(@name);
				EXEC sp_executesql @sql;
			END
			""", new { name = databaseName });
	}
}
