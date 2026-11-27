using Dapper;
using Microsoft.Data.SqlClient;

namespace Beep.Api.Repositories.Collector;

public sealed class CollectedDataRepository : ICollectedDataRepository
{
	private readonly string _connectionString;

	public CollectedDataRepository(string connectionString)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
		_connectionString = connectionString;
	}

	public async Task<string?> GetExtractedJsonAsync(Guid collectedDataId, CancellationToken cancellationToken)
	{
		await using var connection = new SqlConnection(_connectionString);
		return await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
			"SELECT ExtractedJson FROM dbo.WebsiteSelectorsCollectedData WHERE Id = @collectedDataId",
			new { collectedDataId },
			cancellationToken: cancellationToken));
	}
}
