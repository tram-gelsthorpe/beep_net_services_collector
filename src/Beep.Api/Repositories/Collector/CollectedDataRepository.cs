using Dapper;
using Microsoft.Data.SqlClient;

namespace Beep.Api.Repositories.Collector;

/// <summary>Reads collection results from the Beep.Collector database (<c>ConnectionStrings:Collector</c>).</summary>
public interface ICollectedDataRepository
{
	/// <summary>
	/// The <c>ExtractedJson</c> of a collection result, or null when the result does not exist or has
	/// no extracted data.
	/// </summary>
	Task<string?> GetExtractedJsonAsync(Guid collectedDataId, CancellationToken cancellationToken);
}

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
