using System;
using System.Threading;
using System.Threading.Tasks;

namespace Beep.EventBus;

/// <summary>
/// Event aggregator for publishing domain events to registered subscribers.
/// This is an in-memory, synchronous event dispatcher.
/// </summary>
public interface IEventAggregator
{
	/// <summary>
	/// Subscribe a handler to a specific event type.
	/// </summary>
	void Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : class, IDomainEvent;

	/// <summary>
	/// Publish an event to all registered subscribers.
	/// </summary>
	Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : class, IDomainEvent;
}
