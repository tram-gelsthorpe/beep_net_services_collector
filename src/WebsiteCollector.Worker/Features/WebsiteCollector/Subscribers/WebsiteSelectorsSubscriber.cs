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
	private readonly ILogger<WebsiteSelectorsSubscriber> _logger;

	public string Name => "WebsiteSelectorsSubscriber";

	public WebsiteSelectorsSubscriber(
		IEventAggregator eventAggregator,
		IEventBusRepository deliveryRepository,
		ILogger<WebsiteSelectorsSubscriber> logger)
	{
		_eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
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

			// Get all registered EventBus for this selector
			var eventBusCollection = await _deliveryRepository.GetByOwnerIdAsync(@event.WebsiteSelectorsId, ct);

			if (!eventBusCollection.Any())
			{
				_logger.LogDebug(
					"No EventBus registered for selector {SelectorId}",
					@event.WebsiteSelectorsId);
				return;
			}

			// Queue a delivery for each EventBus registered for this selector
			// The generic EventBus delivery system (Beep.EventBus) will handle actual HTTP delivery
			foreach (var eventBus in eventBusCollection)
			{
				try
				{
					_logger.LogDebug(
						"Queueing EventBus delivery for {EventBusOwnerUrl} (EventBus {EventBusId})",
						eventBus.EventBusOwnerUrl,
						eventBus.Id);

					// Queue the delivery - actual HTTP POST will be handled by EventBusDeliveryWorker
					await _deliveryRepository.QueueDeliveryAsync(eventBus.Id, @event.CollectedDataId, ct);

					_logger.LogInformation(
						"Queued EventBus delivery for selector {SelectorId} to EventBus {EventBusOwnerUrl}",
						@event.WebsiteSelectorsId,
						eventBus.EventBusOwnerUrl);
				}
				catch (Exception ex)
				{
					_logger.LogError(
						ex,
						"Failed to queue EventBus delivery for selector {SelectorId} and EventBus {EventBusId}",
						@event.WebsiteSelectorsId,
						eventBus.Id);
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
