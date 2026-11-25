using Beep.Database;
using Beep.EventBus;
using Beep.Scheduler;
using Beep.ServiceBus;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beep.Collector.Tests.Integration;

/// <summary>
/// Creates the ServiceBus, Collector, EventBus and Scheduler test databases once per test run using
/// the production schema initializers, and clears their data between tests.
/// Connection strings come from appsettings.json and can be overridden with
/// ConnectionStrings__{ServiceBus|Collector|EventBus|Scheduler} environment variables.
/// </summary>
/// <remarks>
/// Test classes run in parallel (MSTestSettings.cs). Classes that use the ServiceBus database share
/// it, so only one of them may run in the parallel phase (WebsiteCollectorIntegrationTests); the
/// others are marked [DoNotParallelize] and run afterwards, one at a time.
/// </remarks>
[TestClass]
public static class TestDatabase
{
	/// <summary>Same schema names the worker uses in production.</summary>
	public const string ServiceBusSchema = "ServiceBus";
	public const string EventBusSchema = "EventBus";
	public const string SchedulerSchema = "Scheduler";

	public static string ServiceBusConnectionString { get; private set; } = string.Empty;
	public static string CollectorConnectionString { get; private set; } = string.Empty;
	public static string EventBusConnectionString { get; private set; } = string.Empty;
	public static string SchedulerConnectionString { get; private set; } = string.Empty;

	private static IEnumerable<string> AllConnectionStrings =>
		[ServiceBusConnectionString, CollectorConnectionString, EventBusConnectionString, SchedulerConnectionString];

	[AssemblyInitialize]
	public static async Task AssemblyInitializeAsync(TestContext _)
	{
		var configuration = new ConfigurationBuilder()
			.SetBasePath(AppContext.BaseDirectory)
			.AddJsonFile("appsettings.json", optional: false)
			.AddEnvironmentVariables()
			.Build();

		string Get(string name) => configuration.GetConnectionString(name)
			?? throw new InvalidOperationException($"Missing {name} connection string");

		ServiceBusConnectionString = Get("ServiceBus");
		CollectorConnectionString = Get("Collector");
		EventBusConnectionString = Get("EventBus");
		SchedulerConnectionString = Get("Scheduler");

		// Start from a clean slate so the "create" path of the initializers is exercised.
		foreach (var connectionString in AllConnectionStrings)
			await DropDatabaseAsync(connectionString);

		await CreateAsync();
	}

	[AssemblyCleanup]
	public static async Task AssemblyCleanupAsync()
	{
		foreach (var connectionString in AllConnectionStrings)
			await DropDatabaseAsync(connectionString);
	}

	/// <summary>Runs the production initializers (create on first call, upgrade check afterwards).</summary>
	public static async Task CreateAsync()
	{
		(string ConnectionString, System.Reflection.Assembly Scripts, string Schema)[] databases =
		[
			(ServiceBusConnectionString, typeof(WorkItemRepository).Assembly, ServiceBusSchema),
			(CollectorConnectionString, typeof(CreationDatabaseService).Assembly, CreationDatabaseService.SelectorSchema),
			(EventBusConnectionString, typeof(EventBusRepository).Assembly, EventBusSchema),
			(SchedulerConnectionString, typeof(SchedulerRepository).Assembly, SchedulerSchema),
		];

		foreach (var (connectionString, scripts, schema) in databases)
			await new SqlDatabaseInitializer(connectionString, scripts, NullLogger.Instance).CreateOrUpdateAsync(schema);
	}

	/// <summary>Clears the ServiceBus, Collector and EventBus databases.</summary>
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

		await using (var collector = new SqlConnection(CollectorConnectionString))
			await collector.ExecuteAsync("DELETE FROM dbo.Websites;"); // cascades to selectors, monitor, executions, data

		await using var eventBus = new SqlConnection(EventBusConnectionString);
		await eventBus.ExecuteAsync($"DELETE FROM {EventBusSchema}.EventBus;"); // cascades to deliveries
	}

	/// <summary>Clears only the Scheduler database (the scheduler tests run in parallel with the others).</summary>
	public static async Task ClearSchedulerDataAsync()
	{
		await using var scheduler = new SqlConnection(SchedulerConnectionString);
		await scheduler.ExecuteAsync($"DELETE FROM {SchedulerSchema}.ScheduleExecutions; DELETE FROM {SchedulerSchema}.Schedules;");
	}

	public static SqlConnection OpenServiceBus() => new(ServiceBusConnectionString);
	public static SqlConnection OpenCollector() => new(CollectorConnectionString);
	public static SqlConnection OpenEventBus() => new(EventBusConnectionString);
	public static SqlConnection OpenScheduler() => new(SchedulerConnectionString);

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
