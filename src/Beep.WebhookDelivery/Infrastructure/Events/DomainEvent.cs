using System;

namespace Beep.WebhookDelivery.Infrastructure.Events;

/// <summary>
/// Base class for domain events raised by the webhook delivery system.
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
	public required Guid EventId { get; init; }
	public required DateTimeOffset OccurredAt { get; init; }
	public abstract string EventType { get; }
}
