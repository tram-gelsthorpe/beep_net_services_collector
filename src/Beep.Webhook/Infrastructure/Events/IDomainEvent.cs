using System;

namespace Beep.EventBus.Infrastructure.Events;

/// <summary>
/// Base event interface for all events in the system.
/// </summary>
public interface IDomainEvent
{
	/// <summary>
	/// Unique identifier for this event instance.
	/// </summary>
	Guid EventId { get; }

	/// <summary>
	/// Timestamp when the event occurred.
	/// </summary>
	DateTimeOffset OccurredAt { get; }

	/// <summary>
	/// Event type name for logging and tracing.
	/// </summary>
	string EventType { get; }
}
