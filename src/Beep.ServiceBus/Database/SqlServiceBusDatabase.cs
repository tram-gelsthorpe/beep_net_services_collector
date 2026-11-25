using System.Threading;
using System.Threading.Tasks;
using Beep.Shared;
using Microsoft.Extensions.Logging;

namespace Beep.ServiceBus;

/// <summary>
/// Creates or upgrades the ServiceBus database (WorkItems tables and stored procedures).
/// </summary>
public sealed class SqlServiceBusDatabase : IServiceBusDatabase
{
	private readonly SqlSchemaInitializer _initializer;

	public SqlServiceBusDatabase(string connectionString, ILogger logger)
	{
		_initializer = new SqlSchemaInitializer(connectionString, scriptSchema: "dbo", logger);
	}

	public Task Create(string schema = "dbo", CancellationToken cancellationToken = default) =>
		_initializer.CreateOrUpdateAsync(schema, cancellationToken);
}
