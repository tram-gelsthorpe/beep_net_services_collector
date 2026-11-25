using Beep.Shared;
using Microsoft.Extensions.Logging;

namespace WebsiteCollector.Worker;

/// <summary>
/// Creates or upgrades the WebsiteCollector database (Websites, WebsiteSelectors and related tables).
/// </summary>
public sealed class SqlWebsiteCollectorDatabase : IWebsiteCollectorDatabase
{
	private readonly SqlSchemaInitializer _initializer;

	public SqlWebsiteCollectorDatabase(string connectionString, ILogger logger)
	{
		_initializer = new SqlSchemaInitializer(connectionString, scriptSchema: "dbo", logger);
	}

	public Task Create(CancellationToken cancellationToken = default) =>
		_initializer.CreateOrUpdateAsync("dbo", cancellationToken);
}
