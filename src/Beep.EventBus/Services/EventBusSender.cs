using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Beep.EventBus;

/// <summary>Sends one delivery attempt over HTTP.</summary>
public interface IEventBusSender
{
	/// <summary>
	/// POST the delivery's JSON payload to its endpoint. Never throws for HTTP errors or
	/// timeouts; they are returned as an unsuccessful <see cref="DeliveryOutcome"/>.
	/// </summary>
	Task<DeliveryOutcome> SendAsync(EventBusDelivery delivery, CancellationToken ct);
}

public sealed class EventBusSender(
	IHttpClientFactory httpClientFactory,
	ILogger<EventBusSender> logger) : IEventBusSender
{
	/// <summary>Name of the HttpClient used for deliveries (configure it with AddHttpClient).</summary>
	public const string HttpClientName = "EventBus";

	public async Task<DeliveryOutcome> SendAsync(EventBusDelivery delivery, CancellationToken ct)
	{
		ArgumentNullException.ThrowIfNull(delivery);

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
		timeout.CancelAfter(TimeSpan.FromSeconds(delivery.TimeoutSeconds));

		try
		{
			using var content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json");
			using var response = await httpClientFactory.CreateClient(HttpClientName)
				.PostAsync(delivery.DeliveryUrl, content, timeout.Token);

			var status = (int)response.StatusCode;
			if (response.IsSuccessStatusCode)
			{
				logger.LogInformation(
					"EventBus delivery {DeliveryId} delivered to {DeliveryUrl} ({StatusCode})",
					delivery.DeliveryId, delivery.DeliveryUrl, status);
				return new DeliveryOutcome(true, status, null);
			}

			logger.LogWarning(
				"EventBus delivery {DeliveryId} to {DeliveryUrl} failed with HTTP {StatusCode}",
				delivery.DeliveryId, delivery.DeliveryUrl, status);
			return new DeliveryOutcome(false, status, $"HTTP {status}");
		}
		catch (OperationCanceledException) when (!ct.IsCancellationRequested)
		{
			logger.LogWarning(
				"EventBus delivery {DeliveryId} to {DeliveryUrl} timed out after {Timeout}s",
				delivery.DeliveryId, delivery.DeliveryUrl, delivery.TimeoutSeconds);
			return new DeliveryOutcome(false, null, $"Timeout after {delivery.TimeoutSeconds}s");
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(
				"EventBus delivery {DeliveryId} to {DeliveryUrl} failed: {Error}",
				delivery.DeliveryId, delivery.DeliveryUrl, ex.Message);
			return new DeliveryOutcome(false, null, $"HTTP error: {ex.Message}");
		}
	}
}
