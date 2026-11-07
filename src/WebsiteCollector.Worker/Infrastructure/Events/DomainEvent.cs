namespace WebsiteCollector.Worker.Infrastructure.Events;

/// <summary>
/// Base class for domain events providing common event properties.
/// </summary>
public abstract class DomainEvent : IDomainEvent
{
	public Guid EventId { get; } = Guid.NewGuid();
	public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
	public abstract string EventType { get; }
}
