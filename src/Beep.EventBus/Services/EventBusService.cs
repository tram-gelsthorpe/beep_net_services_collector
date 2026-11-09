using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Beep.EventBus;

/// <summary>
/// Handles EventBus delivery with HTTP POST, retry logic, and exponential backoff.
/// Processes queued deliveries asynchronously with error handling and monitoring.
/// </summary>
public sealed class EventBusService(
	IEventBusRepository repository,
	IHttpClientFactory httpClientFactory,
	ILogger<EventBusService> logger) : IEventBusService
{
	public async Task DeliverAsync(PendingEventBus delivery, CancellationToken ct)
	{
		try
		{
			using var httpClient = httpClientFactory.CreateClient();
			httpClient.Timeout = TimeSpan.FromSeconds(delivery.TimeoutSeconds);

			// Send POST request with raw payload
			var content = new StringContent(delivery.RawPayload, Encoding.UTF8, "application/json");
			var response = await httpClient.PostAsync(delivery.WebhookUrl, content, ct);

			// Log result
			if (response.IsSuccessStatusCode)
			{
				logger.LogInformation(
					"Webhook delivered successfully. DeliveryId={DeliveryId}, Webhook={Webhook}, StatusCode={StatusCode}, Attempt={Attempt}",
					delivery.DeliveryId, delivery.WebhookUrl, (int)response.StatusCode, delivery.AttemptNumber);

				// Mark as success
				await repository.RecordDeliveryResultAsync(
					delivery.DeliveryId,
					isSuccessful: true,
					(int)response.StatusCode,
					errorMessage: null,
					responseContent: null,
					ct);
			}
			else
			{
				logger.LogWarning(
					"Webhook delivery failed (non-2xx). DeliveryId={DeliveryId}, Webhook={Webhook}, StatusCode={StatusCode}, Attempt={Attempt}",
					delivery.DeliveryId, delivery.WebhookUrl, (int)response.StatusCode, delivery.AttemptNumber);

				// Read response content for diagnostics
				var responseContent = await response.Content.ReadAsStringAsync(ct);

				// Mark as failed (will retry or abandon based on attempt count)
				await repository.RecordDeliveryResultAsync(
					delivery.DeliveryId,
					isSuccessful: false,
					(int)response.StatusCode,
					errorMessage: $"HTTP {(int)response.StatusCode}",
					responseContent: responseContent,
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
				isSuccessful: false,
				httpStatusCode: null,
				errorMessage: $"HTTP Error: {ex.Message}",
				responseContent: null,
				ct);
		}
		catch (OperationCanceledException)
		{
			logger.LogWarning(
				"Webhook delivery timeout. DeliveryId={DeliveryId}, Webhook={Webhook}, Timeout={Timeout}s, Attempt={Attempt}",
				delivery.DeliveryId, delivery.WebhookUrl, delivery.TimeoutSeconds, delivery.AttemptNumber);

			await repository.RecordDeliveryResultAsync(
				delivery.DeliveryId,
				isSuccessful: false,
				httpStatusCode: null,
				errorMessage: $"Timeout after {delivery.TimeoutSeconds}s",
				responseContent: null,
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
				isSuccessful: false,
				httpStatusCode: null,
				errorMessage: $"Unexpected error: {ex.Message}",
				responseContent: null,
				ct);
		}
	}
}
