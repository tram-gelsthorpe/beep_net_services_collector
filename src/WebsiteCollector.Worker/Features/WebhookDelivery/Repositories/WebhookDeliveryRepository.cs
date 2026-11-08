using Beep.WebhookBus.Domain;
using Beep.WebhookBus.Repository;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace WebsiteCollector.Worker.Features.WebhookDelivery.Repositories;

/// <summary>
/// Project-specific webhook delivery repository.
/// Implements both IWebhookDeliveryRepository (project-specific queuing) and IWebhookRepository (generic delivery).
/// Provides queuing and result tracking for WebsiteSelectors webhooks using generic dbo.WebhookDeliveries table.
/// </summary>
public sealed class WebhookDeliveryRepository(IConfiguration config) : IWebhookDeliveryRepository, IWebhookRepository
{
	private SqlConnection Open() => new(config.GetConnectionString("Default") ?? throw new InvalidOperationException("Missing Default connection string"));

	/// <summary>
	/// Queue a webhook delivery for processing.
	/// Inserts a delivery record in the generic dbo.WebhookDeliveries table.
	/// </summary>
	public async Task QueueDeliveryAsync(Guid webhookId, Guid collectedDataId, CancellationToken ct)
	{
		await using var connection = Open();
		// Inserts into generic dbo.WebhookDeliveries table (from Beep.WebhookBus)
		// WebhooksId = webhook endpoint ID (references dbo.Webhooks)
		// WebhooksOwnerCollectedDataId = collectedDataId (references WebsiteSelectorsCollectedData.Id in project context)
		const string sql = """
			INSERT INTO dbo.WebhookDeliveries 
				(WebhooksId, WebhooksOwnerCollectedDataId, Status, AttemptNumber, NextRetryAt, CreatedAt)
			VALUES 
				(@WebhooksId, @WebhooksOwnerCollectedDataId, 'PENDING', 1, SYSUTCDATETIME(), SYSUTCDATETIME())
			""";
		await connection.ExecuteAsync(
			new CommandDefinition(sql, new { WebhooksId = webhookId, WebhooksOwnerCollectedDataId = collectedDataId }, cancellationToken: ct));
	}

	/// <summary>
	/// Record the result of a webhook delivery attempt.
	/// Updates status and retry scheduling in the generic table.
	/// </summary>
	async Task IWebhookDeliveryRepository.RecordDeliveryResultAsync(Guid deliveryId, bool success, int? httpStatusCode, string? errorMessage, CancellationToken ct)
	{
		await RecordDeliveryResultAsync(deliveryId, success, httpStatusCode, errorMessage, null, ct);
	}

	/// <summary>
	/// Record the result of a webhook delivery attempt.
	/// Updates status and retry scheduling in the generic table.
	/// </summary>
	async Task IWebhookRepository.RecordDeliveryResultAsync(Guid deliveryId, bool isSuccessful, int? httpStatusCode, string? errorMessage, string? responseContent, CancellationToken ct)
	{
		await RecordDeliveryResultAsync(deliveryId, isSuccessful, httpStatusCode, errorMessage, responseContent, ct);
	}

	/// <summary>
	/// Internal implementation of record delivery result.
	/// </summary>
	private async Task RecordDeliveryResultAsync(Guid deliveryId, bool isSuccessful, int? httpStatusCode, string? errorMessage, string? responseContent, CancellationToken ct)
	{
		await using var connection = Open();

		if (isSuccessful)
		{
			const string successSql = """
				UPDATE dbo.WebhookDeliveries
				SET Status = 'SUCCESS', 
					HttpStatusCode = @HttpStatusCode, 
					DeliveredAt = SYSUTCDATETIME()
				WHERE Id = @DeliveryId
				""";
			await connection.ExecuteAsync(
				new CommandDefinition(successSql, new { DeliveryId = deliveryId, HttpStatusCode = httpStatusCode }, cancellationToken: ct));
		}
		else
		{
			// Mark as FAILED and schedule retry with exponential backoff
			const string failSql = """
				UPDATE dbo.WebhookDeliveries
				SET Status = 'FAILED', 
					HttpStatusCode = @HttpStatusCode, 
					ErrorMessage = @ErrorMessage,
					NextRetryAt = DATEADD(second, POWER(2, AttemptNumber), SYSUTCDATETIME())
				WHERE Id = @DeliveryId
				""";
			await connection.ExecuteAsync(
				new CommandDefinition(failSql, 
					new { DeliveryId = deliveryId, HttpStatusCode = httpStatusCode, ErrorMessage = errorMessage }, 
					cancellationToken: ct));
		}
	}

	/// <summary>
	/// Get pending deliveries ready for processing from the generic table.
	/// This implementation fetches pending deliveries with full webhook context.
	/// </summary>
	public async Task<IList<PendingWebhookItem>> GetPendingDeliveriesAsync(int batchSize, CancellationToken ct)
	{
		await using var connection = Open();
		const string sql = """
			SELECT TOP (@BatchSize)
				d.Id as DeliveryId,
				d.WebhooksId as WebhookId,
				d.WebhooksOwnerCollectedDataId as OwnerCollectedDataId,
				d.AttemptNumber,
				w.WebhookUrl as RawPayload,
				w.TimeoutSeconds,
				w.MaxRetryAttempts,
				d.CreatedAt,
				d.NextRetryAt,
				d.Status
			FROM dbo.WebhookDeliveries d
			INNER JOIN dbo.Webhooks w ON d.WebhooksId = w.Id
			WHERE d.Status = 'PENDING'
			  AND d.NextRetryAt <= SYSUTCDATETIME()
			  AND w.IsEnabled = 1
			ORDER BY d.NextRetryAt ASC, d.CreatedAt ASC
			""";

		var deliveries = await connection.QueryAsync<PendingWebhookItem>(
			new CommandDefinition(sql, new { BatchSize = batchSize }, cancellationToken: ct));

		return deliveries.ToList();
	}

	/// <summary>
	/// Get a specific delivery by ID from the generic table.
	/// </summary>
	async Task<PendingWebhookItem?> IWebhookRepository.GetDeliveryByIdAsync(Guid deliveryId, CancellationToken ct)
	{
		await using var connection = Open();
		const string sql = """
			SELECT 
				d.Id as DeliveryId,
				d.WebhooksId as WebhookId,
				d.WebhooksOwnerCollectedDataId as OwnerCollectedDataId,
				d.AttemptNumber,
				w.WebhookUrl as RawPayload,
				w.TimeoutSeconds,
				w.MaxRetryAttempts,
				d.CreatedAt,
				d.NextRetryAt,
				d.Status
			FROM dbo.WebhookDeliveries d
			INNER JOIN dbo.Webhooks w ON d.WebhooksId = w.Id
			WHERE d.Id = @DeliveryId
			""";

		return await connection.QueryFirstOrDefaultAsync<PendingWebhookItem>(
			new CommandDefinition(sql, new { DeliveryId = deliveryId }, cancellationToken: ct));
	}

	/// <summary>
	/// Abandon a delivery (mark as failed/no more retries).
	/// </summary>
	async Task IWebhookRepository.AbandonDeliveryAsync(Guid deliveryId, string reason, CancellationToken ct)
	{
		await using var connection = Open();
		const string sql = """
			UPDATE dbo.WebhookDeliveries
			SET Status = 'ABANDONED',
				ErrorMessage = @Reason
			WHERE Id = @DeliveryId
			""";
		await connection.ExecuteAsync(
			new CommandDefinition(sql, new { DeliveryId = deliveryId, Reason = reason }, cancellationToken: ct));
	}

	/// <summary>
	/// Get the current attempt count for a delivery.
	/// </summary>
	async Task<int> IWebhookRepository.GetDeliveryAttemptCountAsync(Guid deliveryId, CancellationToken ct)
	{
		await using var connection = Open();
		const string sql = """
			SELECT AttemptNumber FROM dbo.WebhookDeliveries WHERE Id = @DeliveryId
			""";
		var result = await connection.QueryFirstOrDefaultAsync<int?>(
			new CommandDefinition(sql, new { DeliveryId = deliveryId }, cancellationToken: ct));
		return result ?? 0;
	}
}


