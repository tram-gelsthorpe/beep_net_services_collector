using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Beep.ServiceBus;

namespace Beep.EventBus;

/// <summary>
/// Storage for EventBus endpoints and deliveries. Delivery status, retries and claiming
/// live in the ServiceBus work item of each delivery, not here.
/// </summary>
public interface IEventBusRepository
{
	/// <summary>
	/// Make the given endpoints the enabled endpoints of their owners, in one round trip.
	/// New endpoints are added, existing ones updated or re-enabled, and enabled endpoints of
	/// the listed owners that are not in the batch are disabled. Owners not in the batch are
	/// untouched, so callers can register page by page. Safe to call repeatedly.
	/// </summary>
	Task<EndpointRegistrationResult> RegisterEndpointsAsync(
		IReadOnlyCollection<EventBusEndpoint> endpoints,
		CancellationToken ct);

	/// <summary>
	/// Create a delivery of <paramref name="eventId"/> to each enabled endpoint of
	/// <paramref name="ownerId"/> (skipping endpoints that already have one), and return every
	/// not-yet-delivered delivery of that event as work items to enqueue.
	/// </summary>
	Task<IReadOnlyList<WorkItemRequest>> CreateDeliveriesAsync(
		Guid ownerId,
		Guid eventId,
		string payload,
		CancellationToken ct);

	/// <summary>Load a delivery with its endpoint.</summary>
	Task<EventBusDelivery?> GetDeliveryAsync(Guid deliveryId, CancellationToken ct);

	/// <summary>Record the result of one delivery attempt.</summary>
	Task RecordAttemptAsync(Guid deliveryId, DeliveryOutcome outcome, CancellationToken ct);
}
