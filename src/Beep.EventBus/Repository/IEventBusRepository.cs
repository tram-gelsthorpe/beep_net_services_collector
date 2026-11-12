using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Beep.EventBus;

/// <summary>
/// Generic repository abstraction for EventBus delivery operations.
/// Implementations handle storage and retrieval of EventBus deliveries with dynamic payload support.
/// </summary>
public interface IEventBusRepository
{
	/// <summary>
	/// Get all enabled EventBus entries for a specific EventBus owner.
	/// </summary>
	Task<IReadOnlyList<EventBusDomain>> GetByOwnerIdAsync(Guid eventBusOwnerId
		, CancellationToken ct);

	/// <summary>
	///	Queues a new EventBus delivery for processing, given the EventBus ID and the associated collected data ID. 
	/// </summary>
	/// <param name="eventBusId"></param>
	/// <param name="eventBusOwnerCollectedDataId"></param>
	/// <param name="ct"></param>
	/// <returns></returns>
	Task QueueDeliveryAsync(Guid eventBusId, Guid eventBusOwnerCollectedDataId
		, CancellationToken ct);

	/// <summary>
	/// Retrieves the next batch of pending EventBus deliveries for processing.
	/// </summary>
	Task<IList<PendingEventBus>> GetPendingDeliveriesAsync(int batchSize
		, CancellationToken ct);

	/// <summary>
	/// Records the result of a delivery attempt (success or failure).
	/// </summary>
	Task RecordDeliveryResultAsync(
		Guid eventBusDeliveryId,
		bool isSuccessful,
		int? httpStatusCode,
		string? errorMessage,
		string? responseContent,
		CancellationToken ct);

	/// <summary>
	/// Retrieves a specific delivery for inspection or retries.
	/// </summary>
	Task<PendingEventBus?> GetDeliveryByIdAsync(Guid eventBusDeliveryId, CancellationToken ct);

	/// <summary>
	/// Marks a delivery as failed/abandoned (no more retry attempts).
	/// </summary>
	Task AbandonDeliveryAsync(Guid eventBusDeliveryId, string reason, CancellationToken ct);

	/// <summary>
	/// Gets the current attempt count for a delivery.
	/// </summary>
	Task<int> GetDeliveryAttemptCountAsync(Guid eventBusDeliveryId, CancellationToken ct);
}
