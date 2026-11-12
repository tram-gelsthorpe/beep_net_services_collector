using System;

namespace Beep.EventBus;

/// <summary>
/// Represents a pending EventBus delivery to be processed.
/// Generic model suitable for any EventBus delivery workflow.
/// </summary>
public record PendingEventBus
{
	public required Guid EventBusId { get; init; }
	public required Guid EventBusDeliveryId { get; init; }
	public required Guid EventBusOwnerCollectedDataId { get; init; }
	public required Guid EventBusOwnerId { get; init; }
	public required string EventBusOwnerDeliveryUrl { get; init; }
	public required int TimeoutSeconds { get; init; }
	public required int AttemptNumber { get; init; }
	public required int MaxRetryAttempts { get; init; }
}
