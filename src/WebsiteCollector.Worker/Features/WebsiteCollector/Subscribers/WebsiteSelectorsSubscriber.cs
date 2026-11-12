using Beep.EventBus;
using Microsoft.Extensions.Logging;

namespace WebsiteCollector.Worker;

/// <summary>
/// Subscriber that queues webhook deliveries when collection notifications occur.
/// Uses the generic Beep.EventBus delivery system with async, queued processing.
/// Webhook actual HTTP delivery is handled by the WebhookDeliveryWorker via the generic framework.
/// </summary>
public sealed class WebsiteSelectorsSubscriber : ISubscriber
{
	private readonly IEventAggregator _eventAggregator;
	private readonly IEventBusRepository _deliveryRepository;
	private readonly IWebsiteSelectionWebhookRepository _webhookRepository;
	
	private readonly ILogger<WebsiteSelectorsSubscriber> _logger;

	public string Name => "WebsiteSelectorsSubscriber";

	public WebsiteSelectorsSubscriber(
		IEventAggregator eventAggregator,
		IEventBusRepository deliveryRepository,
		IWebsiteSelectionWebhookRepository webhookRepository,
		ILogger<WebsiteSelectorsSubscriber> logger)
	{
		_eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
		_webhookRepository = webhookRepository ?? throw new ArgumentNullException(nameof(webhookRepository));
		_deliveryRepository = deliveryRepository ?? throw new ArgumentNullException(nameof(deliveryRepository));
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
				"Collection completed for selector {SelectorId}, queueing webhook deliveries",
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

			// Queue a delivery for each webhook
			// The generic webhook delivery system (Beep.WebhookBus) will handle actual HTTP delivery
			foreach (var webhook in webhooks)
			{
				try
				{
					_logger.LogDebug(
						"Queueing webhook delivery for {WebhookUrl} (webhook {WebhookId})",
						webhook.WebhookUrl,
						webhook.Id);

					// Queue the delivery - actual HTTP POST will be handled by WebhookDeliveryWorker
					await _deliveryRepository.QueueDeliveryAsync(webhook.Id, @event.CollectedDataId, ct);

					_logger.LogInformation(
						"Queued webhook delivery for selector {SelectorId} to webhook {WebhookUrl}",
						@event.WebsiteSelectorsId,
						webhook.WebhookUrl);
				}
				catch (Exception ex)
				{
					_logger.LogError(
						ex,
						"Failed to queue webhook delivery for selector {SelectorId} and webhook {WebhookId}",
						@event.WebsiteSelectorsId,
						webhook.Id);
				}
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(
				ex,
				"Error processing collection completed event for selector {SelectorId}",
				@event.WebsiteSelectorsId);
		}
	}
}
