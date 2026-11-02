namespace WebsiteCollector.Infrastructure.Events;

/// <summary>
/// Base class for domain events providing common event properties.
/// </summary>
public abstract class DomainEvent : IEvent
{
	public Guid EventId { get; } = Guid.NewGuid();
	public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
	public abstract string EventType { get; }
}
