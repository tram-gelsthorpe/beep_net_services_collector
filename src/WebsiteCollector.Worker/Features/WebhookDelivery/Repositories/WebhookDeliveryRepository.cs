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
		const string sql = """
			INSERT INTO dbo.WebsiteSelectorsWebhookDeliveries 
				(WebsiteSelectorsWebhooksId, CollectedDataId, Status, AttemptNumber, NextRetryAt, CreatedAt)
			VALUES 
				(@WebsiteSelectorsWebhooksId, @CollectedDataId, 'PENDING', 1, SYSUTCDATETIME(), SYSUTCDATETIME())
			""";
		await connection.ExecuteAsync(
			new CommandDefinition(sql, new { WebsiteSelectorsWebhooksId = webhookId, CollectedDataId = collectedDataId }, cancellationToken: ct));
	}

	public async Task<IReadOnlyList<PendingWebhookDelivery>> GetPendingDeliveriesAsync(Guid webhookId, int maxResults, CancellationToken ct)
	{
		await using var connection = Open();
		const string sql = """
			SELECT TOP (@MaxResults)
				d.Id as DeliveryId,
				d.WebsiteSelectorsWebhooksId,
				d.CollectedDataId,
				d.AttemptNumber,
				w.WebhookUrl,
				w.TimeoutSeconds,
				w.MaxRetryAttempts,
				cd.ResponseContent as RawBody
			FROM dbo.WebsiteSelectorsWebhookDeliveries d
			INNER JOIN dbo.WebsiteSelectorsWebhooks w ON d.WebsiteSelectorsWebhooksId = w.Id
			INNER JOIN dbo.WebsiteSelectorsCollectedData cd ON d.CollectedDataId = cd.Id
			WHERE d.WebsiteSelectorsWebhooksId = @WebhookId
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
			const string successSql = """
				UPDATE dbo.WebsiteSelectorsWebhookDeliveries
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
				UPDATE dbo.WebsiteSelectorsWebhookDeliveries
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
