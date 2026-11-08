using Dapper;
using Microsoft.Data.SqlClient;
using WebsiteCollector.Worker.Features.WebsiteCollector.Models;

namespace WebsiteCollector.Api.Repositories;

/// <summary>
/// SQL-based implementation of collected data repository.
/// </summary>
public sealed class CollectedDataRepository : ICollectedDataRepository
{
	private readonly IConfiguration _configuration;

	public CollectedDataRepository(IConfiguration configuration)
	{
		_configuration = configuration;
	}

	private SqlConnection Open() => 
		new(_configuration.GetConnectionString("Default") ?? throw new InvalidOperationException("Missing Default connection string"));

	public async Task<WebsiteSelectorsCollectedData?> GetCollectedDataByExecutionIdAsync(Guid executionId, CancellationToken cancellationToken)
	{
		await using var connection = Open();
		const string sql = @"
			SELECT TOP 1 Id, WebsiteSelectorsExecutionsId, ContentHash, ResponseContent, ExtractedJson, CollectedAt
			FROM dbo.WebsiteSelectorsCollectedData 
			WHERE WebsiteSelectorsExecutionsId = @ExecutionId
			ORDER BY CollectedAt DESC";

		return await connection.QueryFirstOrDefaultAsync<WebsiteSelectorsCollectedData>(sql, new { ExecutionId = executionId });
	}

	public async Task<WebsiteSelectorsCollectedData?> GetLatestCollectedDataBySelectorIdAsync(Guid selectorId, CancellationToken cancellationToken)
	{
		await using var connection = Open();
		const string sql = @"
			SELECT TOP 1 d.Id, d.WebsiteSelectorsExecutionsId, d.ContentHash, d.ResponseContent, d.ExtractedJson, d.CollectedAt
			FROM dbo.WebsiteSelectorsCollectedData d
			INNER JOIN dbo.WebsiteExecutions e ON d.WebsiteSelectorsExecutionsId = e.Id
			WHERE e.WebsiteSelectorsId = @SelectorId AND e.Status = 'Succeeded'
			ORDER BY d.CollectedAt DESC";

		return await connection.QueryFirstOrDefaultAsync<WebsiteSelectorsCollectedData>(sql, new { SelectorId = selectorId });
	}

	public async Task<(IReadOnlyList<WebsiteSelectorsCollectedData> Items, int Total)> GetCollectionHistoryAsync(Guid selectorId, int pageNumber, int pageSize, CancellationToken cancellationToken)
	{
		await using var connection = Open();

		// Get total count
		const string countSql = @"
			SELECT COUNT(*)
			FROM dbo.WebsiteSelectorsCollectedData d
			INNER JOIN dbo.WebsiteExecutions e ON d.WebsiteSelectorsExecutionsId = e.Id
			WHERE e.WebsiteSelectorsId = @SelectorId";

		var total = await connection.QueryFirstOrDefaultAsync<int>(countSql, new { SelectorId = selectorId });

		// Get paginated results
		const string sql = @"
			SELECT d.Id, d.WebsiteSelectorsExecutionsId, d.ContentHash, d.ResponseContent, d.ExtractedJson, d.CollectedAt
			FROM dbo.WebsiteSelectorsCollectedData d
			INNER JOIN dbo.WebsiteExecutions e ON d.WebsiteSelectorsExecutionsId = e.Id
			WHERE e.WebsiteSelectorsId = @SelectorId
			ORDER BY d.CollectedAt DESC
			OFFSET (@PageNumber - 1) * @PageSize ROWS
			FETCH NEXT @PageSize ROWS ONLY";

		var items = await connection.QueryAsync<WebsiteSelectorsCollectedData>(sql, new 
		{ 
			SelectorId = selectorId, 
			PageNumber = pageNumber, 
			PageSize = pageSize 
		});

		return (items.ToList(), total);
	}

	public async Task<WebsiteSelectorsExecutions?> GetExecutionByIdAsync(Guid executionId, CancellationToken cancellationToken)
	{
		await using var connection = Open();
		const string sql = @"
			SELECT Id, WebsiteSelectorsId, LeaseToken, WorkerId, StartedAt, FinishedAt, Status, AttemptNumber, HttpStatus, ErrorMessage
			FROM dbo.WebsiteExecutions 
			WHERE Id = @ExecutionId";

		return await connection.QueryFirstOrDefaultAsync<WebsiteSelectorsExecutions>(sql, new { ExecutionId = executionId });
	}

	public async Task<IReadOnlyList<WebsiteSelectorsExecutions>> GetRecentExecutionsBySelectorIdAsync(Guid selectorId, int limit, CancellationToken cancellationToken)
	{
		await using var connection = Open();
		const string sql = @"
			SELECT TOP (@Limit) Id, WebsiteSelectorsId, LeaseToken, WorkerId, StartedAt, FinishedAt, Status, AttemptNumber, HttpStatus, ErrorMessage
			FROM dbo.WebsiteExecutions 
			WHERE WebsiteSelectorsId = @SelectorId
			ORDER BY StartedAt DESC";

		var executions = await connection.QueryAsync<WebsiteSelectorsExecutions>(sql, new { SelectorId = selectorId, Limit = limit });
		return executions.ToList();
	}

	public async Task<IReadOnlyList<WebsiteSelectorsCollectedData>> SearchCollectedDataAsync(Guid? selectorId, string? searchTerm, DateTime? fromDate, DateTime? toDate, CancellationToken cancellationToken)
	{
		await using var connection = Open();

		var sql = @"
			SELECT d.Id, d.WebsiteSelectorsExecutionsId, d.ContentHash, d.ResponseContent, d.ExtractedJson, d.CollectedAt
			FROM dbo.WebsiteSelectorsCollectedData d
			INNER JOIN dbo.WebsiteExecutions e ON d.WebsiteSelectorsExecutionsId = e.Id
			WHERE 1=1";

		var parameters = new Dictionary<string, object>();

		if (selectorId.HasValue)
		{
			sql += " AND e.WebsiteSelectorsId = @SelectorId";
			parameters["SelectorId"] = selectorId.Value;
		}

		if (!string.IsNullOrWhiteSpace(searchTerm))
		{
			sql += " AND (d.ResponseContent LIKE @SearchTerm OR d.ExtractedJson LIKE @SearchTerm)";
			parameters["SearchTerm"] = $"%{searchTerm}%";
		}

		if (fromDate.HasValue)
		{
			sql += " AND d.CollectedAt >= @FromDate";
			parameters["FromDate"] = fromDate.Value;
		}

		if (toDate.HasValue)
		{
			sql += " AND d.CollectedAt <= @ToDate";
			parameters["ToDate"] = toDate.Value;
		}

		sql += " ORDER BY d.CollectedAt DESC";

		var results = await connection.QueryAsync<WebsiteSelectorsCollectedData>(sql, parameters);
		return results.ToList();
	}

	public async Task<object?> GetSelectorStatisticsAsync(Guid selectorId, CancellationToken cancellationToken)
	{
		await using var connection = Open();
		const string sql = @"
			SELECT 
				TotalExecutions = COUNT(e.Id),
				SuccessfulExecutions = SUM(CASE WHEN e.Status = 'Succeeded' THEN 1 ELSE 0 END),
				FailedExecutions = SUM(CASE WHEN e.Status = 'Failed' THEN 1 ELSE 0 END),
				AbandonedExecutions = SUM(CASE WHEN e.Status = 'Abandoned' THEN 1 ELSE 0 END),
				TotalDataPoints = COUNT(d.Id),
				AverageAttempts = CAST(AVG(CAST(e.AttemptNumber AS FLOAT)) AS DECIMAL(5,2)),
				LatestExecution = MAX(e.StartedAt),
				OldestExecution = MIN(e.StartedAt)
			FROM dbo.WebsiteExecutions e
			LEFT JOIN dbo.WebsiteSelectorsCollectedData d ON e.Id = d.WebsiteSelectorsExecutionsId
			WHERE e.WebsiteSelectorsId = @SelectorId";

		return await connection.QueryFirstOrDefaultAsync(sql, new { SelectorId = selectorId });
	}
}
