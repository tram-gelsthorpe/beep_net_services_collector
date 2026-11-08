using System;

namespace Beep.EventBus;

/// <summary>
/// Base class for domain events providing common event properties.
/// </summary>
public abstract class DomainEvent : IDomainEvent
{
	public Guid EventId { get; } = Guid.NewGuid();
	public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
	public abstract string EventType { get; }
}
