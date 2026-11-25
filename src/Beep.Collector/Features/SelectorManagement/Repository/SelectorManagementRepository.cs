using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Beep.Collector;

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

	/// <summary>
	/// Get one page of enabled selectors, ordered by Id (keyset paging).
	/// Start with <see cref="Guid.Empty"/> and pass the last Id of each page to get the next one;
	/// an empty page means there are no more. Each page is a cheap index seek regardless of table size.
	/// </summary>
	public async Task<IReadOnlyList<ActiveWebsiteSelector>> GetActiveSelectorsPageAsync(
		Guid afterId,
		int pageSize,
		CancellationToken cancellationToken)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

		try
		{
			await using var connection = new SqlConnection(_connectionString);

			const string sql = """
				SELECT TOP (@PageSize) Id, MaxAttempts, TimeoutSeconds, DispatchUrl
				FROM dbo.WebsiteSelectors
				WHERE IsEnabled = 1 AND Id > @AfterId
				ORDER BY Id
				""";

			var items = await connection.QueryAsync<ActiveWebsiteSelector>(
				new CommandDefinition(sql, new { AfterId = afterId, PageSize = pageSize }, cancellationToken: cancellationToken));

			return items.AsList();
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogError(ex, "SelectorManagementRepository: Error fetching active selectors after {AfterId}", afterId);
			throw;
		}
	}

	/// <summary>
	/// Update WebsiteCollector-specific tables with collection results.
	/// Inserts data into WebsiteSelectorsCollectedData, WebsiteSelectorsExecutions, and updates WebsiteSelectorsMonitor.
	/// </summary>
	/// <returns>The Id of the inserted WebsiteSelectorsCollectedData row, or null when nothing was collected or saving failed.</returns>
	public async Task<Guid?> SaveCollectionResultAsync(
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
				DECLARE @collectedDataId uniqueidentifier = NULL;
				IF @ErrorMessage IS NULL AND @ResponseContent IS NOT NULL
				BEGIN
					DECLARE @newId uniqueidentifier = NEWID();
					INSERT INTO dbo.WebsiteSelectorsCollectedData (Id, WebsiteSelectorsExecutionsId, ContentHash, ResponseContent, ExtractedJson, CollectedAt)
					SELECT TOP (1) @newId, Id, @ContentHash, @ResponseContent, @ExtractedJson, GETUTCDATE()
					FROM dbo.WebsiteSelectorsExecutions
					WHERE LeaseToken = @LeaseToken;
					IF @@ROWCOUNT = 1 SET @collectedDataId = @newId;
				END;

				-- Update the monitor record
				UPDATE dbo.WebsiteSelectorsMonitor
				SET LastSucceededAt = CASE WHEN @ErrorMessage IS NULL THEN GETUTCDATE() ELSE LastSucceededAt END,
					LastError = @ErrorMessage,
					LastStartedAt = GETUTCDATE() 
				WHERE WebsiteSelectorsId = @WebsiteSelectorsId;

				SELECT @collectedDataId;
				""";

			return await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(
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
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "SelectorManagementRepository: Error saving collection result for LeaseToken={LeaseToken}", leaseToken);
			return null;
		}
	}

	/// <summary>
	/// Create an execution record when starting to process a selector.
	/// </summary>
	public async Task<bool> StartExecutionAsync(
		Guid workerId,
		Guid leaseToken,
		int leaseSeconds,
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
					VALUES (NEWID(), @WebsiteSelectorsId, GETUTCDATE(), @TimeoutSeconds, @MaxAttempts, 0, @WorkerId, @LeaseToken, DATEADD(second, @LeaseSeconds, GETUTCDATE()), GETUTCDATE(), GETUTCDATE());
				END
				ELSE
				BEGIN
					-- Update existing monitor to mark lease as active
					UPDATE dbo.WebsiteSelectorsMonitor
					SET LeaseToken = @LeaseToken,
						LeaseUntil = DATEADD(second, @LeaseSeconds, GETUTCDATE()),
						UpdatedAt = GETUTCDATE()
					WHERE WebsiteSelectorsId = @WebsiteSelectorsId;
				END;
				""";

			await conn.ExecuteAsync(new CommandDefinition(
				sql,
				new
				{
					LeaseToken = leaseToken,
					LeaseSeconds = leaseSeconds,
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
