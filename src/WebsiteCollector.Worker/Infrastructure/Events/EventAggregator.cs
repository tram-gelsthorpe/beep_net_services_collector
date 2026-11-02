namespace WebsiteCollector.Infrastructure.Events;

/// <summary>
/// In-memory event aggregator that dispatches events to subscribed handlers.
/// Thread-safe implementation suitable for worker scenarios.
/// </summary>
public sealed class EventAggregator : IEventAggregator
{
	private readonly Dictionary<Type, List<Delegate>> _subscribers = new();
	private readonly object _lock = new();

	public void Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : class, IEvent
	{
		if (handler == null)
			throw new ArgumentNullException(nameof(handler));

		lock (_lock)
		{
			var eventType = typeof(TEvent);
			if (!_subscribers.ContainsKey(eventType))
				_subscribers[eventType] = new List<Delegate>();

			_subscribers[eventType].Add(handler);
		}
	}

	public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : class, IEvent
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

	private static async Task InvokeHandlerSafely<TEvent>(
		Func<TEvent, CancellationToken, Task> handler,
		TEvent @event,
		CancellationToken ct) where TEvent : class, IEvent
	{
		try
		{
			await handler(@event, ct);
		}
		catch (Exception ex)
		{
			// Log but don't throw - other subscribers should still execute
			System.Diagnostics.Debug.WriteLine($"Event handler error: {ex.Message}");
		}
	}
}
