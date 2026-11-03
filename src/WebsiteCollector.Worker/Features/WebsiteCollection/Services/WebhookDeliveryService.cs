using WebsiteCollector.Features.WebsiteCollection.Models;
using WebsiteCollector.Features.WebsiteCollection.Repositories;
using Microsoft.Extensions.Logging;

namespace WebsiteCollector.Features.WebsiteCollection.Services;

public interface IWebhookDeliveryService
{
	Task DeliverAsync(PendingWebhookDelivery delivery, CancellationToken ct);
}

/// <summary>
/// Handles webhook delivery with HTTP POST, retry logic, and exponential backoff.
/// Option B: Separate delivery service that processes queued deliveries asynchronously.
/// </summary>
public sealed class WebhookDeliveryService(
	IWebhookDeliveryRepository repository,
	IHttpClientFactory httpClientFactory,
	ILogger<WebhookDeliveryService> logger) : IWebhookDeliveryService
{
	public async Task DeliverAsync(PendingWebhookDelivery delivery, CancellationToken ct)
	{
		try
		{
			using var httpClient = httpClientFactory.CreateClient();
			httpClient.Timeout = TimeSpan.FromSeconds(delivery.TimeoutSeconds);

			// Create webhook payload
			var payload = new
			{
				delivery.DeliveryId,
				delivery.WebsiteSelectorsWebhooksId,
				delivery.CollectedDataId,
				delivery.AttemptNumber,
				delivery.RawBody,
				DeliveredAt = DateTimeOffset.UtcNow
			};

			// Send POST request
			var jsonContent = System.Text.Json.JsonSerializer.Serialize(payload);
			var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");
			var response = await httpClient.PostAsync(delivery.WebhookUrl, content, ct);

			// Log result
			if (response.IsSuccessStatusCode)
			{
				logger.LogInformation(
					"Webhook delivered successfully. DeliveryId={DeliveryId}, Webhook={Webhook}, StatusCode={StatusCode}, Attempt={Attempt}",
					delivery.DeliveryId, delivery.WebhookUrl, (int)response.StatusCode, delivery.AttemptNumber);

				// Mark as success
				await repository.RecordDeliveryResultAsync(delivery.DeliveryId, success: true, (int)response.StatusCode, null, ct);
			}
			else
			{
				logger.LogWarning(
					"Webhook delivery failed (non-2xx). DeliveryId={DeliveryId}, Webhook={Webhook}, StatusCode={StatusCode}, Attempt={Attempt}",
					delivery.DeliveryId, delivery.WebhookUrl, (int)response.StatusCode, delivery.AttemptNumber);

				// Mark as failed (will retry or abandon based on attempt count)
				await repository.RecordDeliveryResultAsync(
					delivery.DeliveryId,
					success: false,
					(int)response.StatusCode,
					$"HTTP {(int)response.StatusCode}",
					ct);
			}
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(
				"Webhook delivery HTTP error. DeliveryId={DeliveryId}, Webhook={Webhook}, Error={Error}, Attempt={Attempt}",
					delivery.DeliveryId, delivery.WebhookUrl, ex.Message, delivery.AttemptNumber);

				await repository.RecordDeliveryResultAsync(
					delivery.DeliveryId,
					success: false,
					null,
				$"HTTP Error: {ex.Message}",
				ct);
		}
		catch (OperationCanceledException)
		{
			logger.LogWarning(
				"Webhook delivery timeout. DeliveryId={DeliveryId}, Webhook={Webhook}, Timeout={Timeout}s, Attempt={Attempt}",
				delivery.DeliveryId, delivery.WebhookUrl, delivery.TimeoutSeconds, delivery.AttemptNumber);

			await repository.RecordDeliveryResultAsync(
				delivery.DeliveryId,
				success: false,
				null,
				$"Timeout after {delivery.TimeoutSeconds}s",
				ct);
		}
		catch (Exception ex)
		{
			logger.LogError(
				ex,
				"Unexpected error during webhook delivery. DeliveryId={DeliveryId}, Webhook={Webhook}, Attempt={Attempt}",
				delivery.DeliveryId, delivery.WebhookUrl, delivery.AttemptNumber);

			await repository.RecordDeliveryResultAsync(
				delivery.DeliveryId,
				success: false,
				null,
				$"Unexpected error: {ex.Message}",
				ct);
		}
	}
}
