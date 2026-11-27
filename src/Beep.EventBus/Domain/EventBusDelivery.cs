using System;

namespace Beep.EventBus;

/// <summary>
/// One delivery of an event to one endpoint, as loaded by the delivery worker.
/// </summary>
public sealed record EventBusDelivery
{
	public required Guid DeliveryId { get; init; }
	public required Guid EventBusId { get; init; }
	public required Guid EventId { get; init; }
	public required Guid OwnerId { get; init; }
	public required string DeliveryUrl { get; init; }
	public required int TimeoutSeconds { get; init; }
	public required bool IsEnabled { get; init; }
	public required string Payload { get; init; }
	public DateTimeOffset? DeliveredAt { get; init; }
}
