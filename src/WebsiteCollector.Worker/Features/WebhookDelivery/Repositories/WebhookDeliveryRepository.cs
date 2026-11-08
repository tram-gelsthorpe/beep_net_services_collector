using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using WebsiteCollector.Worker.Features.WebhookDelivery.Models;

namespace WebsiteCollector.Worker.Features.WebhookDelivery.Repositories;

public sealed class WebhookDeliveryRepository(IConfiguration config) : IWebhookDeliveryRepository
{
	private SqlConnection Open() => new(config.GetConnectionString("Default") ?? throw new InvalidOperationException("Missing Default connection string"));

	public async Task QueueDeliveryAsync(Guid webhookId, Guid collectedDataId, CancellationToken ct)
	{
		await using var connection = Open();
		// Uses generic dbo.WebhookDeliveries table from Beep.WebhookBus
		// WebhooksOwnerId context is provided through the webhookId (references WebsiteSelectorsWebhooks.Id)
		// WebhooksOwnerCollectedDataId is the project-specific collected data reference (WebsiteSelectorsCollectedData.Id)
		const string sql = """
			INSERT INTO dbo.WebhookDeliveries 
				(WebhooksId, WebhooksOwnerCollectedDataId, Status, AttemptNumber, NextRetryAt, CreatedAt)
			VALUES 
				(@WebhooksId, @WebhooksOwnerCollectedDataId, 'PENDING', 1, SYSUTCDATETIME(), SYSUTCDATETIME())
			""";
		await connection.ExecuteAsync(
			new CommandDefinition(sql, new { WebhooksId = webhookId, WebhooksOwnerCollectedDataId = collectedDataId }, cancellationToken: ct));
	}

	public async Task<IReadOnlyList<PendingWebhookDelivery>> GetPendingDeliveriesAsync(Guid webhookId, int maxResults, CancellationToken ct)
	{
		await using var connection = Open();
		// Uses generic dbo.WebhookDeliveries and dbo.Webhooks from Beep.WebhookBus
		// Scoped to WebsiteSelectors context through webhookId parameter
		const string sql = """
			SELECT TOP (@MaxResults)
				d.Id as DeliveryId,
				d.WebhooksId as WebsiteSelectorsWebhooksId,
				d.WebhooksOwnerCollectedDataId as CollectedDataId,
				d.AttemptNumber,
				w.WebhookUrl,
				w.TimeoutSeconds,
				w.MaxRetryAttempts,
				cd.ResponseContent as RawBody
			FROM dbo.WebhookDeliveries d
			INNER JOIN dbo.Webhooks w ON d.WebhooksId = w.Id
			INNER JOIN dbo.WebsiteSelectorsCollectedData cd ON d.WebhooksOwnerCollectedDataId = cd.Id
			WHERE d.WebhooksId = @WebhookId
			  AND d.Status = 'PENDING'
			  AND d.NextRetryAt <= SYSUTCDATETIME()
			  AND w.IsEnabled = 1
			ORDER BY d.NextRetryAt ASC, d.CreatedAt ASC
			""";

		var deliveries = await connection.QueryAsync<PendingWebhookDelivery>(
			new CommandDefinition(sql, new { WebhookId = webhookId, MaxResults = maxResults }, cancellationToken: ct));

		return deliveries.ToList();
	}

	public async Task RecordDeliveryResultAsync(Guid deliveryId, bool success, int? httpStatusCode, string? errorMessage, CancellationToken ct)
	{
		await using var connection = Open();

		if (success)
		{
			// Uses generic dbo.WebhookDeliveries from Beep.WebhookBus
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
			// Uses generic dbo.WebhookDeliveries from Beep.WebhookBus
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
}
