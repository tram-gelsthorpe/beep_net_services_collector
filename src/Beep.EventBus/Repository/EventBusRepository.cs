using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Beep.EventBus
{
	public sealed class EventBusRepository(IConfiguration config) : IEventBusRepository
	{
		private SqlConnection Open() => new(config.GetConnectionString("EventBus") ?? throw new InvalidOperationException("Missing EventBus connection string"));

		public async Task<IReadOnlyList<EventBusDomain>> GetByOwnerIdAsync(Guid eventBusOwnerId, CancellationToken ct)
		{
			await using var conn = Open();
			const string sql = """
				SELECT Id, EventBusOwnerId, EventBusOwnerDeliveryUrl, IsEnabled, TimeoutSeconds, MaxRetryAttempts
				FROM dbo.EventBus
				WHERE EventBusOwnerId = @EventBusOwnerId AND IsEnabled = 1
			""";

			var rows = await conn.QueryAsync<EventBusDomain>(
				new CommandDefinition(sql, new { EventBusOwnerId = eventBusOwnerId }, cancellationToken: ct));

			return rows.AsList();
		}

		public async Task AbandonDeliveryAsync(Guid eventBusDeliveryId, String reason, CancellationToken ct)
		{
			await using var connection = Open();

			const string sql = """
				UPDATE dbo.EventBusDeliveries
				SET Status = 'ABANDONED',
					ErrorMessage = @Reason
				WHERE Id = @EventBusDeliveryId
			""";

			await connection.ExecuteAsync(
				new CommandDefinition(sql, new { EventBusDeliveryId = eventBusDeliveryId, Reason = reason }, cancellationToken: ct));
		}

		public async Task<Int32> GetDeliveryAttemptCountAsync(Guid eventBusDeliveryId, CancellationToken ct)
		{
			await using var connection = Open();

			const string sql = """
				SELECT AttemptNumber
				FROM dbo.EventBusDeliveries
				WHERE Id = @EventBusDeliveryId
			""";

			return await connection.QueryFirstOrDefaultAsync<Int32>(
				new CommandDefinition(sql, new { EventBusDeliveryId = eventBusDeliveryId }, cancellationToken: ct));
		}

		public async Task<PendingEventBus?> GetDeliveryByIdAsync(Guid eventBusDeliveryId, CancellationToken ct)
		{
			await using var connection = Open();

			const string sql = """
				SELECT 
					d.Id as EventBusDeliveryId,
					d.EventBusId as EventBusId,
					d.EventBusOwnerCollectedDataId as EventBusOwnerCollectedDataId,
					d.AttemptNumber,
					e.EventBusOwnerUrl as RawPayload,
					e.TimeoutSeconds,
					e.MaxRetryAttempts,
					d.CreatedAt,
					d.NextRetryAt,
					d.Status
				FROM dbo.EventBusDeliveries d
				INNER JOIN dbo.EventBus e ON d.EventBusId = e.Id
				WHERE d.Id = @EventBusDeliveryId
			""";

			return await connection.QueryFirstOrDefaultAsync<PendingEventBus>(
				new CommandDefinition(sql, new { EventBusDeliveryId = eventBusDeliveryId }, cancellationToken: ct));
		}

		public async Task<IList<PendingEventBus>> GetPendingDeliveriesAsync(Int32 batchSize, CancellationToken ct)
		{
			await using var connection = Open();

			const string sql = """
				SELECT TOP (@BatchSize)
					d.Id as EventBusDeliveryId,
					d.EventBusId as EventBusId,
					d.EventBusOwnerCollectedDataId,
					d.AttemptNumber,
					d.CreatedAt,
					d.NextRetryAt,
					d.Status,
					e.EventBusOwnerId,
					e.EventBusOwnerDeliveryUrl,
					e.TimeoutSeconds,
					e.MaxRetryAttempts,
				FROM dbo.EventBusDeliveries d
				INNER JOIN dbo.EventBus e ON d.EventBusId = e.Id
				WHERE d.Status = 'PENDING'
				  AND d.NextRetryAt <= SYSUTCDATETIME()
				  AND e.IsEnabled = 1
				ORDER BY d.NextRetryAt ASC, d.CreatedAt ASC
				""";

		var deliveries = await connection.QueryAsync<PendingEventBus>(
				new CommandDefinition(sql, new { BatchSize = batchSize }, cancellationToken: ct));

			return deliveries.ToList();
		}

		public async Task RecordDeliveryResultAsync(Guid eventBusDeliveryId, Boolean isSuccessful, Int32? httpStatusCode, String? errorMessage, String? responseContent, CancellationToken ct)
		{
			await using var connection = Open();

			if (isSuccessful)
			{
				const string successSql = """
					UPDATE dbo.EventBusDeliveries
					SET Status = 'SUCCESS', 
						HttpStatusCode = @HttpStatusCode, 
						DeliveredAt = SYSUTCDATETIME()
					WHERE Id = @EventBusDeliveryId
					""";

				await connection.ExecuteAsync(
					new CommandDefinition(successSql, new { EventBusDeliveryId = eventBusDeliveryId, HttpStatusCode = httpStatusCode }, cancellationToken: ct));
			}
			else
			{
				// Mark as FAILED and schedule retry with exponential backoff
				const string failSql = """
					UPDATE dbo.EventBusDeliveries
					SET Status = 'FAILED', 
						HttpStatusCode = @HttpStatusCode, 
						ErrorMessage = @ErrorMessage,
						NextRetryAt = DATEADD(second, POWER(2, AttemptNumber), SYSUTCDATETIME())
					WHERE Id = @EventBusDeliveryId
					""";

				await connection.ExecuteAsync(
					new CommandDefinition(failSql,
						new { EventBusDeliveryId = eventBusDeliveryId, HttpStatusCode = httpStatusCode, ErrorMessage = errorMessage },
						cancellationToken: ct));
			}
		}

		/// <summary>
		/// Queue a EventBus delivery for processing.
		/// Creates a delivery work item in the generic framework.
		/// </summary>
		public async Task QueueDeliveryAsync(Guid eventBusId, Guid eventBusOwnerCollectedDataId, CancellationToken ct)
		{
			await using var connection = Open();
			
			const string sql = """
				INSERT INTO dbo.EventBusDeliveries 
					(EventBusId, EventBusOwnerCollectedDataId, Status, AttemptNumber, NextRetryAt, CreatedAt)
				VALUES 
					(@EventBusId, @EventBusOwnerCollectedDataId, 'PENDING', 1, SYSUTCDATETIME(), SYSUTCDATETIME())
			""";

			await connection.ExecuteAsync(
				new CommandDefinition(sql, new { EventBusId = eventBusId, EventBusOwnerCollectedDataId = eventBusOwnerCollectedDataId }, cancellationToken: ct));
		}
	}
}
