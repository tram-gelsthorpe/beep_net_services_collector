using System.Reflection;
using Beep.Database;
using Beep.EventBus;
using Beep.Scheduler;
using Beep.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WebsiteCollector.Worker;

/// <summary>
/// Creates or upgrades every database the worker uses, on application startup,
/// from the SQL scripts embedded in each owning assembly.
/// </summary>
public sealed class CreationDatabaseService(
	IConfiguration configuration,
	ILogger<CreationDatabaseService> logger)
{
	public const string SchedulerDatabaseName = "Scheduler";
	public const string ServiceBusDatabaseName = "ServiceBus";
	public const string EventBusDatabaseName = "EventBus";
	public const string SelectorDatabaseName = "Collector";

	public const string SchedulerSchema = "Scheduler";
	public const string ServiceBusSchema = "ServiceBus";
	public const string EventBusSchema = "EventBus";
	public const string SelectorSchema = "dbo";

	/// <summary>
	/// (connection string name, assembly embedding the scripts, target schema).
	/// All scripts are written against dbo and rewritten to the target schema.
	/// </summary>
	private static readonly (string ConnectionName, Assembly Scripts, string Schema)[] Databases =
	[
		(SchedulerDatabaseName, typeof(SchedulerRepository).Assembly, SchedulerSchema),
		(ServiceBusDatabaseName, typeof(WorkItemRepository).Assembly, ServiceBusSchema),
		(EventBusDatabaseName, typeof(EventBusRepository).Assembly, EventBusSchema),
		(SelectorDatabaseName, typeof(CreationDatabaseService).Assembly, SelectorSchema),
	];

	/// <summary>
	/// Initialize all databases and schemas.
	/// Should be called during application startup before any services access the database.
	/// </summary>
	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		logger.LogInformation("Starting database initialization...");

		foreach (var (connectionName, scripts, schema) in Databases)
		{
			var connectionString = configuration.GetConnectionString(connectionName)
				?? throw new InvalidOperationException($"Missing '{connectionName}' connection string");

			try
			{
				logger.LogInformation("Initializing {Database} database...", connectionName);
				await new SqlDatabaseInitializer(connectionString, scripts, logger)
					.CreateOrUpdateAsync(schema, cancellationToken);
				logger.LogInformation("{Database} database initialized successfully", connectionName);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "{Database} database initialization failed", connectionName);
				throw;
			}
		}
	}
}
