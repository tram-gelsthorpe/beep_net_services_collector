using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace WebsiteCollector.Worker;

/// <summary>
/// Database access layer for website collection domain models.
/// Also provides access to WorkerFramework generic work item repository.
/// All work claiming, lease management, and completion is handled through the generic WorkItems interface.
/// </summary>
public sealed class SelectorManagementRepository
{
	private readonly string _connectionString;
	private readonly ILogger<SelectorManagementRepository> _logger;

	private SqlConnection OpenAsync() => new(_connectionString);

	public SelectorManagementRepository(string connectionString, ILogger<SelectorManagementRepository> logger)
	{
		_connectionString = connectionString ?? throw new InvalidOperationException("Missing Selector connection string");
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	/// <summary>
	/// Get details of a specific website selector for processing.
	/// </summary>
	public async Task<IReadOnlyList<WebsiteSelector>> GetWebsiteSelectorAsync(Guid websiteSelectorsId, CancellationToken ct)
	{
		await using var conn = OpenAsync();
		
		const string sql = """
			SELECT Id, WebsitesId, CollectionUrl, CollectionType, ContentSelector, TableSelector, TimeoutSeconds, MaxAttempts, IsEnabled 
			FROM dbo.WebsiteSelectors 
			WHERE Id = @WebsiteSelectorsId AND IsEnabled = 1
			""";

		var rows = await conn.QueryAsync<WebsiteSelector>(new CommandDefinition(sql, new { WebsiteSelectorsId = websiteSelectorsId }, cancellationToken: ct));
		
		return rows.AsList();
	}

	public async Task<IReadOnlyList<DispatchWebsiteSelector>> GetDispatchWebsiteSelectorAsync(CancellationToken cancellationToken)
	{
		var selectors = new List<DispatchWebsiteSelector>();

		try
		{
			await using var connection = new SqlConnection(_connectionString);
			await connection.OpenAsync(cancellationToken);

			const string sql = """
				SELECT Id, DispatchUrl, MaxAttempts, TimeoutSeconds
				FROM dbo.WebsiteSelectors 
				WHERE IsEnabled = 1
				ORDER BY Id
				""";

			var items = await connection.QueryAsync<DispatchWebsiteSelector>(
				new CommandDefinition(sql, cancellationToken: cancellationToken));

			selectors.AddRange(items);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "WebsiteCollectorScheduleHandler: Error fetching active selector IDs from database");
			throw;
		}

		return selectors;
	}

	public async Task<IReadOnlyList<ActiveWebsiteSelector>> GetActiveSelectorsAsync(CancellationToken cancellationToken)
	{
		var selectors = new List<ActiveWebsiteSelector>();

		try
		{
			await using var connection = new SqlConnection(_connectionString);
			await connection.OpenAsync(cancellationToken);

			const string sql = """
				SELECT Id, MaxAttempts 
				FROM dbo.WebsiteSelectors 
				WHERE IsEnabled = 1
				ORDER BY Id
				""";

			var items = await connection.QueryAsync<ActiveWebsiteSelector>(
				new CommandDefinition(sql, cancellationToken: cancellationToken));

			selectors.AddRange(items);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "SelectorManagementRepository: Error fetching active selector from database");
			throw;
		}

		return selectors;
	}
}
