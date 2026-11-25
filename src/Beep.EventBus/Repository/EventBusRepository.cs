using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Beep.EventBus;

public sealed class EventBusRepository : IEventBusRepository
{
	private readonly string _connectionString;
	private readonly string _schema;

	public EventBusRepository(string connectionString, string schema)
	{
		_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
		_schema = schema ?? throw new ArgumentNullException(nameof(schema));
	}

	private SqlConnection Open() => new(_connectionString);

	/// <summary>Columns mapped onto <see cref="PendingEventBus"/>; expects aliases d (deliveries) and e (EventBus).</summary>
	private const string PendingEventBusColumns = """
		d.Id AS EventBusDeliveryId,
		d.EventBusId,
		d.EventBusOwnerCollectedDataId,
		e.EventBusOwnerId,
		e.EventBusOwnerDeliveryUrl,
		e.TimeoutSeconds,
		d.AttemptCount + 1 AS AttemptNumber,
		e.MaxAttempts AS MaxRetryAttempts
		""";

	public async Task<IReadOnlyList<EventBusDomain>> GetByOwnerIdAsync(Guid eventBusOwnerId, CancellationToken ct)
	{
		await using var connection = Open();

		var sql = $"""
			SELECT Id, EventBusOwnerId, EventBusOwnerDeliveryUrl AS EventBusOwnerUrl, IsEnabled, TimeoutSeconds, MaxAttempts
			FROM {_schema}.EventBus
			WHERE EventBusOwnerId = @EventBusOwnerId AND IsEnabled = 1
			""";

		var rows = await connection.QueryAsync<EventBusDomain>(
			new CommandDefinition(sql, new { EventBusOwnerId = eventBusOwnerId }, cancellationToken: ct));

		return rows.AsList();
	}

	public async Task AbandonDeliveryAsync(Guid eventBusDeliveryId, string reason, CancellationToken ct)
	{
		await using var connection = Open();

		var sql = $"UPDATE {_schema}.EventBusDeliveries SET Status = 'ABANDONED', ErrorMessage = @Reason WHERE Id = @EventBusDeliveryId";

		await connection.ExecuteAsync(
			new CommandDefinition(sql, new { EventBusDeliveryId = eventBusDeliveryId, Reason = reason }, cancellationToken: ct));
	}

	public async Task<int> GetDeliveryAttemptCountAsync(Guid eventBusDeliveryId, CancellationToken ct)
	{
		await using var connection = Open();

		var sql = $"SELECT AttemptCount FROM {_schema}.EventBusDeliveries WHERE Id = @EventBusDeliveryId";

		return await connection.QueryFirstOrDefaultAsync<int>(
			new CommandDefinition(sql, new { EventBusDeliveryId = eventBusDeliveryId }, cancellationToken: ct));
	}

	public async Task<PendingEventBus?> GetDeliveryByIdAsync(Guid eventBusDeliveryId, CancellationToken ct)
	{
		await using var connection = Open();

		var sql = $"""
			SELECT {PendingEventBusColumns}
			FROM {_schema}.EventBusDeliveries d
			INNER JOIN {_schema}.EventBus e ON d.EventBusId = e.Id
			WHERE d.Id = @EventBusDeliveryId
			""";

		return await connection.QueryFirstOrDefaultAsync<PendingEventBus>(
			new CommandDefinition(sql, new { EventBusDeliveryId = eventBusDeliveryId }, cancellationToken: ct));
	}

	public async Task<IList<PendingEventBus>> GetPendingDeliveriesAsync(int batchSize, CancellationToken ct)
	{
		await using var connection = Open();

		var sql = $"""
			SELECT TOP (@BatchSize) {PendingEventBusColumns}
			FROM {_schema}.EventBusDeliveries d
			INNER JOIN {_schema}.EventBus e ON d.EventBusId = e.Id
			WHERE d.Status = 'PENDING'
			  AND d.NextRetryAt <= SYSUTCDATETIME()
			  AND e.IsEnabled = 1
			ORDER BY d.NextRetryAt ASC, d.CreatedAt ASC
			""";

		var deliveries = await connection.QueryAsync<PendingEventBus>(
			new CommandDefinition(sql, new { BatchSize = batchSize }, cancellationToken: ct));

		return deliveries.ToList();
	}

	public async Task RecordDeliveryResultAsync(
		Guid eventBusDeliveryId,
		bool isSuccessful,
		int? httpStatusCode,
		string? errorMessage,
		string? responseContent,
		CancellationToken ct)
	{
		await using var connection = Open();

		var sql = isSuccessful
			? $"""
				UPDATE {_schema}.EventBusDeliveries
				SET Status = 'SUCCESS',
					AttemptCount = AttemptCount + 1,
					HttpStatusCode = @HttpStatusCode,
					ErrorMessage = NULL,
					DeliveredAt = SYSUTCDATETIME()
				WHERE Id = @EventBusDeliveryId
				"""
			// Retry with exponential backoff until MaxAttempts is reached, then abandon.
			: $"""
				UPDATE d
				SET Status = CASE WHEN d.AttemptCount + 1 >= e.MaxAttempts THEN 'ABANDONED' ELSE 'PENDING' END,
					AttemptCount = d.AttemptCount + 1,
					HttpStatusCode = @HttpStatusCode,
					ErrorMessage = @ErrorMessage,
					NextRetryAt = DATEADD(second, POWER(2, d.AttemptCount + 1), SYSUTCDATETIME())
				FROM {_schema}.EventBusDeliveries d
				INNER JOIN {_schema}.EventBus e ON d.EventBusId = e.Id
				WHERE d.Id = @EventBusDeliveryId
				""";

		await connection.ExecuteAsync(new CommandDefinition(
			sql,
			new { EventBusDeliveryId = eventBusDeliveryId, HttpStatusCode = httpStatusCode, ErrorMessage = errorMessage },
			cancellationToken: ct));
	}

	/// <summary>
	/// Queue an EventBus delivery for processing.
	/// </summary>
	public async Task QueueDeliveryAsync(Guid eventBusId, Guid eventBusOwnerCollectedDataId, CancellationToken ct)
	{
		await using var connection = Open();

		var sql = $"""
			INSERT INTO {_schema}.EventBusDeliveries (EventBusId, EventBusOwnerCollectedDataId, Status, NextRetryAt, QueuedAt)
			VALUES (@EventBusId, @EventBusOwnerCollectedDataId, 'PENDING', SYSUTCDATETIME(), SYSUTCDATETIME())
			""";

		await connection.ExecuteAsync(new CommandDefinition(
			sql,
			new { EventBusId = eventBusId, EventBusOwnerCollectedDataId = eventBusOwnerCollectedDataId },
			cancellationToken: ct));
	}

	public async Task RegisterEventBusAsync(
		Guid eventBusOwnerId,
		string eventBusOwnerDeliveryUrl,
		int timeoutSeconds,
		int maxAttempts,
		CancellationToken ct)
	{
		await using var connection = Open();

		var sql = $"""
			INSERT INTO {_schema}.EventBus (EventBusOwnerId, EventBusOwnerDeliveryUrl, IsEnabled, TimeoutSeconds, MaxAttempts)
			VALUES (@EventBusOwnerId, @EventBusOwnerDeliveryUrl, 1, @TimeoutSeconds, @MaxAttempts)
			""";

		await connection.ExecuteAsync(new CommandDefinition(
			sql,
			new
			{
				EventBusOwnerId = eventBusOwnerId,
				EventBusOwnerDeliveryUrl = eventBusOwnerDeliveryUrl,
				TimeoutSeconds = timeoutSeconds,
				MaxAttempts = maxAttempts
			},
			cancellationToken: ct));
	}
}
