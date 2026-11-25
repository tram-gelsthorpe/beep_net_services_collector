using Beep.Shared;
using Microsoft.Extensions.Logging;

namespace Beep.Scheduler;

/// <summary>
/// Creates or upgrades the Scheduler database (WorkerSchedules, execution logs and procedures).
/// </summary>
public sealed class SqlSchedulerDatabase : ISchedulerDatabase
{
	private readonly SqlSchemaInitializer _initializer;

	public SqlSchedulerDatabase(string connectionString, ILogger logger)
	{
		_initializer = new SqlSchemaInitializer(connectionString, scriptSchema: "Scheduler", logger);
	}

	public Task Create(string schema = "Scheduler", CancellationToken cancellationToken = default) =>
		_initializer.CreateOrUpdateAsync(schema, cancellationToken);
}
