using System.Threading;
using System.Threading.Tasks;
using Beep.Shared;
using Microsoft.Extensions.Logging;

namespace Beep.EventBus;

/// <summary>
/// Creates or upgrades the EventBus database (EventBus and EventBusDeliveries tables).
/// </summary>
public sealed class SqlEventBusDatabase : IEventBusDatabase
{
	private readonly SqlSchemaInitializer _initializer;

	public SqlEventBusDatabase(string connectionString, ILogger logger)
	{
		_initializer = new SqlSchemaInitializer(connectionString, scriptSchema: "dbo", logger);
	}

	public Task Create(string schema = "dbo", CancellationToken cancellationToken = default) =>
		_initializer.CreateOrUpdateAsync(schema, cancellationToken);
}
