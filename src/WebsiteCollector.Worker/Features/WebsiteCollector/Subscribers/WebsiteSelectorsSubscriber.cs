using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WebsiteCollector.Worker.Features.WebhookDelivery.Models;
using WebsiteCollector.Worker.Features.WebsiteCollector.Events;
using WebsiteCollector.Worker.Features.WebsiteCollector.Repositories;
using WebsiteCollector.Worker.Infrastructure.Events;

namespace WebsiteCollector.Worker.Features.WebsiteCollector.Subscribers;

/// <summary>
/// Subscriber that forwards collection notifications to registered webhooks.
/// Executes asynchronously and non-blocking.
/// </summary>
public sealed class WebsiteSelectorsSubscriber : ISubscriber
{
	private readonly IEventAggregator _eventAggregator;
	private readonly IWebsiteSelectionWebhookRepository _webhookRepository;
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly ILogger<WebsiteSelectorsSubscriber> _logger;

	public string Name => "WebsiteSelectorsSubscriber";

	public WebsiteSelectorsSubscriber(
		IEventAggregator eventAggregator,
		IWebsiteSelectionWebhookRepository webhookRepository,
		IHttpClientFactory httpClientFactory,
		ILogger<WebsiteSelectorsSubscriber> logger)
	{
		_eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
		_webhookRepository = webhookRepository ?? throw new ArgumentNullException(nameof(webhookRepository));
		_httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public async Task RegisterAsync(CancellationToken ct = default)
	{
		_eventAggregator.Subscribe<CollectionCompletedEvent>(OnCollectionCompletedAsync);
		_logger.LogInformation("Registered WebsiteSelectorsSubscriber to collection completed events");
		await Task.CompletedTask;
	}

	private async Task OnCollectionCompletedAsync(CollectionCompletedEvent @event, CancellationToken ct)
	{
		try
		{
			_logger.LogInformation(
				"Collection completed for selector {SelectorId}, forwarding to webhooks",
				@event.WebsiteSelectorsId);

			// Get all registered webhooks for this selector
			var webhooks = await _webhookRepository.GetByWebsiteSelectorsIdAsync(@event.WebsiteSelectorsId, ct);

			if (!webhooks.Any())
			{
				_logger.LogDebug(
					"No webhooks registered for selector {SelectorId}",
					@event.WebsiteSelectorsId);
				return;
			}

			// Build notification payload
			var payload = BuildPayload(@event);

			// Call all webhooks asynchronously (fire-and-forget)
			var tasks = webhooks.Select(webhook => CallWebhookAsync(webhook, payload, ct));
			await Task.WhenAll(tasks);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error processing collection completed event for selector {SelectorId}", @event.WebsiteSelectorsId);
		}
	}

	private static CollectionNotificationPayload BuildPayload(CollectionCompletedEvent @event)
	{
		return new CollectionNotificationPayload
		{
			EventId = @event.EventId,
			WebsiteSelectorsId = @event.WebsiteSelectorsId,
			IsSuccess = @event.IsSuccess,
			HttpStatusCode = @event.HttpStatusCode,
			Error = @event.Error,
			ResponseContent = @event.ResponseContent,
			ExtractedJson = @event.ExtractedJson,
			ContentHashBase64 = @event.ContentHash != null ? Convert.ToBase64String(@event.ContentHash) : null,
			CollectionStartedAt = @event.CollectionStartedAt,
			CollectionFinishedAt = @event.CollectionFinishedAt,
			AttemptNumber = @event.AttemptNumber,
			NotificationSentAt = DateTimeOffset.UtcNow
		};
	}

	private async Task CallWebhookAsync(
		WebsiteSelectionWebhook webhook,
		CollectionNotificationPayload payload,
		CancellationToken ct)
	{
		try
		{
			using var client = _httpClientFactory.CreateClient("collector");

			var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
			using var content = new StringContent(json, Encoding.UTF8, "application/json");

			_logger.LogDebug("Calling webhook {WebhookUrl} for selector {SelectorId}", webhook.WebhookUrl, webhook.WebsiteSelectorsId);

			using var response = await client.PostAsync(webhook.WebhookUrl, content, ct);

			int? statusCode = (int?)response.StatusCode;
			string? errorMessage = null;

			if (!response.IsSuccessStatusCode)
			{
				errorMessage = $"HTTP {statusCode}: {response.ReasonPhrase}";
				_logger.LogWarning(
					"Webhook call failed for {WebhookUrl}: {StatusCode} {Reason}",
					webhook.WebhookUrl,
					response.StatusCode,
					response.ReasonPhrase);
			}
			else
			{
				_logger.LogInformation(
					"Webhook call succeeded for {WebhookUrl} [{StatusCode}]",
					webhook.WebhookUrl,
					response.StatusCode);
			}

			// Update webhook status in database
			await _webhookRepository.UpdateWebhookStatusAsync(webhook.Id, statusCode, errorMessage, ct);
		}
		catch (OperationCanceledException)
		{
			_logger.LogWarning("Webhook call to {WebhookUrl} was cancelled", webhook.WebhookUrl);
			await _webhookRepository.UpdateWebhookStatusAsync(webhook.Id, null, "Cancelled", ct);
		}
		catch (HttpRequestException ex)
		{
			_logger.LogError(ex, "HTTP error calling webhook {WebhookUrl}", webhook.WebhookUrl);
			await _webhookRepository.UpdateWebhookStatusAsync(webhook.Id, null, ex.Message, ct);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error calling webhook {WebhookUrl}", webhook.WebhookUrl);
			await _webhookRepository.UpdateWebhookStatusAsync(webhook.Id, null, ex.Message, ct);
		}
	}
}
