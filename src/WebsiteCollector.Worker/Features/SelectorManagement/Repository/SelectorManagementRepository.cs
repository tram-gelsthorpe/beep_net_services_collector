using Beep.ServiceBus;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WebsiteCollector.Worker;

/// <summary>
/// Database access layer for website collection domain models.
/// Also provides access to WorkerFramework generic work item repository.
/// All work claiming, lease management, and completion is handled through the generic WorkItems interface.
/// </summary>
public sealed class SelectorManagementRepository(IConfiguration config, 
	ILogger<SelectorManagementRepository> logger, ILoggerFactory loggerFactory)
{
	private IWorkItemRepository WorkItemRepository { get; } = new WorkItemRepository(
		config.GetConnectionString("Default") ?? throw new InvalidOperationException("Missing Default connection string"),
		loggerFactory.CreateLogger<WorkItemRepository>());

	private SqlConnection Open() => new(config.GetConnectionString("Default") ?? throw new InvalidOperationException("Missing Default connection string"));

	/// <summary>
	/// Generic work item repository for all workers to use.
	/// Use this for claiming work, renewing leases, and completing work items.
	/// </summary>
	public IWorkItemRepository WorkItems => WorkItemRepository;

	/// <summary>
	/// Get details of a specific website selector for processing.
	/// </summary>
	public async Task<IReadOnlyList<WebsiteSelector>> GetWebsiteSelectorAsync(Guid websiteSelectorsId, CancellationToken ct)
	{
		await using var conn = Open();
		const string sql = """
			SELECT Id, WebsitesId, CollectionUrl, CollectionType, ContentSelector, TableSelector, IntervalSeconds, IsEnabled 
			FROM dbo.WebsiteSelectors 
			WHERE Id = @WebsiteSelectorsId AND IsEnabled = 1
			""";
		var rows = await conn.QueryAsync<WebsiteSelector>(new CommandDefinition(sql, new { WebsiteSelectorsId = websiteSelectorsId }, cancellationToken: ct));
		return rows.AsList();
	}
}
