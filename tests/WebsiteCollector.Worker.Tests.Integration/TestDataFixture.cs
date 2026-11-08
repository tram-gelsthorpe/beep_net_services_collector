using Dapper;
using Microsoft.Data.SqlClient;

namespace WebsiteCollector.Worker.Tests.Integration;

/// <summary>
/// Helper for seeding test data into the database.
/// Provides builders for creating test entities with sensible defaults.
/// </summary>
internal sealed class TestDataFixture
{
	private readonly SqlConnection _connection;

	public TestDataFixture(SqlConnection connection)
	{
		_connection = connection;
	}

	/// <summary>
	/// Insert a WebsiteSelector for testing.
	/// </summary>
	public async Task<Guid> SeedWebsiteSelectorAsync(
		Guid? id = null,
		Guid? websitesId = null,
		string collectionUrl = "https://example.com",
		string collectionType = "HTML",
		string? contentSelector = ".content",
		string? tableSelector = null,
		int intervalSeconds = 3600,
		bool isEnabled = true,
		CancellationToken ct = default)
	{
		id ??= Guid.NewGuid();
		websitesId ??= Guid.NewGuid();

		const string sql = @"
			INSERT INTO dbo.WebsiteSelectors 
			(Id, WebsitesId, CollectionUrl, CollectionType, ContentSelector, TableSelector, IntervalSeconds, IsEnabled)
			VALUES 
			(@Id, @WebsitesId, @CollectionUrl, @CollectionType, @ContentSelector, @TableSelector, @IntervalSeconds, @IsEnabled)
		";

		await _connection.ExecuteAsync(
			new CommandDefinition(sql,
				new
				{
					Id = id,
					WebsitesId = websitesId,
					CollectionUrl = collectionUrl,
					CollectionType = collectionType,
					ContentSelector = contentSelector,
					TableSelector = tableSelector,
					IntervalSeconds = intervalSeconds,
					IsEnabled = isEnabled
				},
				cancellationToken: ct));

		return (Guid)id;
	}

	/// <summary>
	/// Insert a WorkItem for testing (represents a claimed selector).
	/// </summary>
	public async Task<Guid> SeedWorkItemAsync(
		Guid? id = null,
		string workerType = "SelectorCollector",
		Guid? domainId = null,
		string status = "PENDING",
		int attemptNumber = 0,
		int maxAttempts = 3,
		Guid? leaseOwner = null,
		Guid? leaseToken = null,
		DateTimeOffset? leaseUntil = null,
		CancellationToken ct = default)
	{
		id ??= Guid.NewGuid();
		domainId ??= Guid.NewGuid();

		const string sql = @"
			INSERT INTO dbo.WorkItems 
			(Id, WorkerType, DomainId, Status, AttemptNumber, MaxAttempts, LeaseOwner, LeaseToken, LeaseUntil)
			VALUES 
			(@Id, @WorkerType, @DomainId, @Status, @AttemptNumber, @MaxAttempts, @LeaseOwner, @LeaseToken, @LeaseUntil)
		";

		await _connection.ExecuteAsync(
			new CommandDefinition(sql,
				new
				{
					Id = id,
					WorkerType = workerType,
					DomainId = domainId,
					Status = status,
					AttemptNumber = attemptNumber,
					MaxAttempts = maxAttempts,
					LeaseOwner = leaseOwner,
					LeaseToken = leaseToken,
					LeaseUntil = leaseUntil
				},
				cancellationToken: ct));

		return (Guid)id;
	}

	/// <summary>
	/// Query a WorkItem from the database.
	/// </summary>
	public async Task<dynamic?> GetWorkItemAsync(Guid id, CancellationToken ct = default)
	{
		const string sql = "SELECT * FROM dbo.WorkItems WHERE Id = @Id";
		return await _connection.QueryFirstOrDefaultAsync(
			new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
	}

	/// <summary>
	/// Query all pending WorkItems of a specific type.
	/// </summary>
	public async Task<IEnumerable<dynamic>> GetPendingWorkItemsAsync(
		string workerType = "SelectorCollector",
		CancellationToken ct = default)
	{
		const string sql = @"
			SELECT * FROM dbo.WorkItems 
			WHERE WorkerType = @WorkerType AND Status = 'PENDING'
			ORDER BY CreatedAt
		";

		return await _connection.QueryAsync(
			new CommandDefinition(sql, new { WorkerType = workerType }, cancellationToken: ct));
	}

	/// <summary>
	/// Query a WebsiteSelector from the database.
	/// </summary>
	public async Task<dynamic?> GetWebsiteSelectorAsync(Guid id, CancellationToken ct = default)
	{
		const string sql = "SELECT * FROM dbo.WebsiteSelectors WHERE Id = @Id";
		return await _connection.QueryFirstOrDefaultAsync(
			new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
	}

	/// <summary>
	/// Query WebsiteSelector count.
	/// </summary>
	public async Task<int> GetWebsiteSelectorCountAsync(CancellationToken ct = default)
	{
		const string sql = "SELECT COUNT(*) FROM dbo.WebsiteSelectors";
		return await _connection.QueryFirstAsync<int>(
			new CommandDefinition(sql, cancellationToken: ct));
	}

	/// <summary>
	/// Delete a specific entity by ID from a table.
	/// </summary>
	public async Task DeleteAsync(string tableName, Guid id, CancellationToken ct = default)
	{
		var sql = $"DELETE FROM dbo.{tableName} WHERE Id = @Id";
		await _connection.ExecuteAsync(
			new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
	}
}
