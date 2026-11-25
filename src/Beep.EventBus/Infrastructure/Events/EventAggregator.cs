using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beep.EventBus;

/// <summary>
/// In-memory event aggregator that dispatches events to subscribed handlers.
/// Thread-safe implementation suitable for worker scenarios.
/// </summary>
public sealed class EventAggregator : IEventAggregator
{
	private readonly Dictionary<Type, List<Delegate>> _subscribers = new();
	private readonly Lock _lock = new();
	private readonly ILogger _logger;

	public EventAggregator(ILogger<EventAggregator>? logger = null)
	{
		_logger = logger ?? (ILogger)NullLogger.Instance;
	}

	public void Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : class, IDomainEvent
	{
		if (handler == null)
			throw new ArgumentNullException(nameof(handler));

		lock (_lock)
		{
			var eventType = typeof(TEvent);
			if (!_subscribers.TryGetValue(eventType, out var handlers))
				_subscribers[eventType] = handlers = [];

			handlers.Add(handler);
		}
	}

	public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : class, IDomainEvent
	{
		if (@event == null)
			throw new ArgumentNullException(nameof(@event));

		List<Delegate>? handlers;

		lock (_lock)
		{
			var eventType = typeof(TEvent);
			handlers = _subscribers.TryGetValue(eventType, out var list) ? new List<Delegate>(list) : null;
		}

		if (handlers == null || handlers.Count == 0)
			return;

		var tasks = handlers.OfType<Func<TEvent, CancellationToken, Task>>()
			.Select(handler => InvokeHandlerSafely(handler, @event, ct))
			.ToList();

		await Task.WhenAll(tasks);
	}

	private async Task InvokeHandlerSafely<TEvent>(
		Func<TEvent, CancellationToken, Task> handler,
		TEvent @event,
		CancellationToken ct) where TEvent : class, IDomainEvent
	{
		try
		{
			await handler(@event, ct);
		}
		catch (Exception ex)
		{
			// Don't allow one handler to break the others
			_logger.LogError(ex, "Handler for event {EventType} ({EventId}) failed", @event.EventType, @event.EventId);
		}
	}
}
