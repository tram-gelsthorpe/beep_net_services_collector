using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Beep.ServiceBus;
using Microsoft.Extensions.Logging;

namespace Beep.EventBus;

public sealed class EventBusPublisher : IEventBusPublisher
{
	/// <summary>ServiceBus DomainType of delivery work items (DomainId = EventBusDeliveries.Id).</summary>
	public const string DeliveryDomainType = "EventBusDelivery";

	private readonly IEventBusRepository _repository;
	private readonly IWorkItemRepository _workItems;
	private readonly EventBusOptions _options;
	private readonly ILogger<EventBusPublisher> _logger;

	public EventBusPublisher(
		IEventBusRepository repository,
		IWorkItemRepository workItems,
		EventBusOptions options,
		ILogger<EventBusPublisher> logger)
	{
		_repository = repository ?? throw new ArgumentNullException(nameof(repository));
		_workItems = workItems ?? throw new ArgumentNullException(nameof(workItems));
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public async Task<int> PublishAsync<TPayload>(Guid ownerId, Guid eventId, TPayload payload, CancellationToken ct = default)
	{
		if (eventId == Guid.Empty)
			throw new ArgumentException("An event id is required so the event is delivered once.", nameof(eventId));

		var json = JsonSerializer.Serialize(payload);
		var deliveries = await _repository.CreateDeliveriesAsync(ownerId, eventId, json, ct);
		if (deliveries.Count == 0)
		{
			_logger.LogDebug("No enabled EventBus endpoints for owner {OwnerId}; event {EventId} not queued", ownerId, eventId);
			return 0;
		}

		// Deliveries that already have an unfinished work item are skipped by the ServiceBus.
		var queued = await _workItems.EnqueueBatchAsync(_options.WorkerType, DeliveryDomainType, deliveries, ct);

		_logger.LogInformation(
			"Published event {EventId} for owner {OwnerId}: {Queued} of {Deliveries} deliveries queued",
			eventId, ownerId, queued, deliveries.Count);
		return queued;
	}
}
