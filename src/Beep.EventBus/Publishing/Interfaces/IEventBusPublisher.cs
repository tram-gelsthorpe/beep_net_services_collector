using System;
using System.Threading;
using System.Threading.Tasks;

namespace Beep.EventBus;

/// <summary>
/// Publishes an event to every enabled endpoint registered for its owner. Delivery happens in
/// the background through the ServiceBus (EventBusWorker), with its claiming, retries and
/// back-off, so publishing returns as soon as the deliveries are queued.
/// </summary>
public interface IEventBusPublisher
{
	/// <summary>
	/// Queue <paramref name="payload"/> (serialized as JSON) for delivery to each enabled endpoint
	/// of <paramref name="ownerId"/>.
	/// </summary>
	/// <param name="ownerId">Entity the endpoints are registered for (e.g. a website selector Id).</param>
	/// <param name="eventId">
	/// Unique id of the event (e.g. the collected data Id). Publishing the same event again does
	/// not deliver it twice; it only re-queues deliveries that have not succeeded.
	/// </param>
	/// <param name="payload">Body to POST.</param>
	/// <param name="ct">Cancellation token</param>
	/// <returns>The number of deliveries queued.</returns>
	Task<int> PublishAsync<TPayload>(Guid ownerId, Guid eventId, TPayload payload, CancellationToken ct = default);
}
