using Beep.EventBus;
using Microsoft.Extensions.Logging;

namespace Beep.Collector;

/// <summary>
/// Publishes each successful collection to the selector's EventBus endpoints.
/// Endpoints are registered by <see cref="WebsiteCollectorScheduleHandler"/>; delivery is done by
/// the EventBus worker through the ServiceBus.
/// </summary>
public sealed class WebsiteSelectorsSubscriber : ISubscriber
{
	private readonly IEventAggregator _eventAggregator;
	private readonly IEventBusPublisher _publisher;
	private readonly ILogger<WebsiteSelectorsSubscriber> _logger;

	public string Name => "WebsiteSelectorsSubscriber";

	public WebsiteSelectorsSubscriber(
		IEventAggregator eventAggregator,
		IEventBusPublisher publisher,
		ILogger<WebsiteSelectorsSubscriber> logger)
	{
		_eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
		_publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public Task RegisterAsync(CancellationToken ct = default)
	{
		_eventAggregator.Subscribe<CollectionCompletedEvent>(OnCollectionCompletedAsync);
		_logger.LogInformation("Registered WebsiteSelectorsSubscriber to collection completed events");
		return Task.CompletedTask;
	}

	private async Task OnCollectionCompletedAsync(CollectionCompletedEvent @event, CancellationToken ct)
	{
		// Only a saved result is delivered: failed attempts are retried by the ServiceBus, and the
		// collected data Id is the event id that keeps each result from being delivered twice.
		if (!@event.IsSuccess || @event.CollectedDataId == Guid.Empty)
			return;

		try
		{
			await _publisher.PublishAsync(
				ownerId: @event.WebsiteSelectorsId,
				eventId: @event.CollectedDataId,
				payload: new CollectionDeliveryPayload(
					@event.WebsiteSelectorsId,
					@event.CollectedDataId,
					@event.CollectionFinishedAt),
				ct);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// The collection itself is already saved; a failed publish must not fail it.
			_logger.LogError(ex,
				"Failed to publish collected data {CollectedDataId} of selector {SelectorId}",
				@event.CollectedDataId, @event.WebsiteSelectorsId);
		}
	}
}

/// <summary>JSON body POSTed to a selector's DispatchUrl when new data is collected.</summary>
public sealed record CollectionDeliveryPayload(Guid WebsiteSelectorsId, Guid CollectedDataId, DateTimeOffset CollectedAt);
