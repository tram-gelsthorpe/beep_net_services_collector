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
			SELECT Id, WebsitesId, CollectionUrl, CollectionType, ContentSelector, TimeoutSeconds, MaxAttempts, IsEnabled 
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

	/// <summary>
	/// Update WebsiteCollector-specific tables with collection results.
	/// Inserts data into WebsiteSelectorsCollectedData, WebsiteSelectorsExecutions, and updates WebsiteSelectorsMonitor.
	/// </summary>
	public async Task<bool> SaveCollectionResultAsync(
		Guid workItemId,
		Guid leaseToken,
		Guid domainId,
		int? httpStatus,
		byte[]? hash,
		string? raw,
		string? extracted,
		string? error,
		CancellationToken ct)
	{
		try
		{
			await using var conn = OpenAsync();
			await conn.OpenAsync(ct);

			const string sql = """
				-- Update the execution record using the leaseToken
				UPDATE dbo.WebsiteSelectorsExecutions
				SET Status = CASE WHEN @ErrorMessage IS NULL THEN 'Succeeded' ELSE 'Failed' END,
					FinishedAt = GETUTCDATE(),
					HttpStatus = @HttpStatus,
					ErrorMessage = @ErrorMessage
				WHERE LeaseToken = @LeaseToken;

				-- Insert collected data if we have a response
				IF @ErrorMessage IS NULL AND @ResponseContent IS NOT NULL
				BEGIN
					INSERT INTO dbo.WebsiteSelectorsCollectedData (Id, WebsiteSelectorsExecutionsId, ContentHash, ResponseContent, ExtractedJson, CollectedAt)
					SELECT NEWID(), Id, @ContentHash, @ResponseContent, @ExtractedJson, GETUTCDATE()
					FROM dbo.WebsiteSelectorsExecutions
					WHERE LeaseToken = @LeaseToken;
				END;

				-- Update the monitor record
				UPDATE dbo.WebsiteSelectorsMonitor
				SET LastSucceededAt = CASE WHEN @ErrorMessage IS NULL THEN GETUTCDATE() ELSE LastSucceededAt END,
					LastError = @ErrorMessage,
					LastStartedAt = GETUTCDATE(),
					LastSucceededAt = GETUTCDATE(),
				WHERE Id = @WebsiteSelectorsId;
				""";

			await conn.ExecuteAsync(new CommandDefinition(
				sql,
				new
				{
					LeaseToken = leaseToken,
					WebsiteSelectorsId = domainId,
					HttpStatus = httpStatus,
					ContentHash = hash,
					ResponseContent = raw,
					ExtractedJson = extracted,
					ErrorMessage = error
				},
				cancellationToken: ct));

			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "SelectorManagementRepository: Error saving collection result for LeaseToken={LeaseToken}", leaseToken);
			return false;
		}
	}

	/// <summary>
	/// Create an execution record when starting to process a selector.
	/// </summary>
	public async Task<bool> StartExecutionAsync(
		Guid workerId,
		Guid leaseToken,
		Guid websiteSelectorsId,
		int attemptNumber,
		int timeoutSeconds,
		int maxAttempts,
		CancellationToken ct)
	{
		try
		{
			await using var conn = OpenAsync();
			await conn.OpenAsync(ct);

			const string sql = """
				-- Insert a new execution record
				INSERT INTO dbo.WebsiteSelectorsExecutions (Id, WebsiteSelectorsId, LeaseToken, WorkerId, StartedAt, Status, AttemptNumber)
				VALUES (NEWID(), @WebsiteSelectorsId, @LeaseToken, @WorkerId, GETUTCDATE(), 'Running', @AttemptNumber);

				-- Ensure monitor record exists or update it
				IF NOT EXISTS (SELECT 1 FROM dbo.WebsiteSelectorsMonitor WHERE WebsiteSelectorsId = @WebsiteSelectorsId)
				BEGIN
					-- Insert new monitor record with defaults from selector
					INSERT INTO dbo.WebsiteSelectorsMonitor (Id, WebsiteSelectorsId, NextRunAt, TimeoutSeconds, MaxAttempts, AttemptCount, WorkerId, LeaseToken, LeaseUntil, CreatedAt, UpdatedAt)
					VALUES (NEWID(), @WebsiteSelectorsId, GETUTCDATE(), @TimeoutSeconds, @MaxAttempts, 0, @WorkerId, @LeaseToken, NULL, GETUTCDATE(), GETUTCDATE());
				END
				ELSE
				BEGIN
					-- Update existing monitor to mark lease as active
					UPDATE dbo.WebsiteSelectorsMonitor
					SET LeaseToken = @LeaseToken,
						LeaseUntil = NULL,
						UpdatedAt = GETUTCDATE()
					WHERE WebsiteSelectorsId = @WebsiteSelectorsId;
				END;
				""";

			await conn.ExecuteAsync(new CommandDefinition(
				sql,
				new
				{
					LeaseToken = leaseToken,
					WorkerId = workerId,
					WebsiteSelectorsId = websiteSelectorsId,
					AttemptNumber = attemptNumber,
					TimeoutSeconds = timeoutSeconds,
					MaxAttempts = maxAttempts
				},
				cancellationToken: ct));

			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "SelectorManagementRepository: Error starting execution for LeaseToken={LeaseToken}", leaseToken);
			return false;
		}
	}

	/// <summary>
	/// Renew the lease for an execution in progress.
	/// </summary>
	public async Task<bool> RenewLeaseAsync(
		Guid leaseToken,
		int leaseSeconds,
		CancellationToken ct)
	{
		try
		{
			await using var conn = OpenAsync();
			await conn.OpenAsync(ct);

			const string sql = """
				-- Update monitor with new lease expiration time
				UPDATE dbo.WebsiteSelectorsMonitor
				SET LeaseUntil = DATEADD(second, @LeaseSeconds, GETUTCDATE())
				WHERE LeaseToken = @LeaseToken;
				""";

			var result = await conn.ExecuteAsync(new CommandDefinition(
				sql,
				new
				{
					LeaseToken = leaseToken,
					LeaseSeconds = leaseSeconds
				},
				cancellationToken: ct));

			return result > 0;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "SelectorManagementRepository: Error renewing lease for LeaseToken={LeaseToken}", leaseToken);
			return false;
		}
	}
}
