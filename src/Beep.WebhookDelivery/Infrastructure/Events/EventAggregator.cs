using System;
using System.Collections.Generic;

namespace Beep.WebhookDelivery.Infrastructure.Events;

/// <summary>
/// In-process event aggregator for collecting domain events during webhook delivery operations.
/// Events are stored in memory for eventual publication to external handlers.
/// </summary>
public sealed class EventAggregator : IEventAggregator
{
	private readonly List<IDomainEvent> _events = new();

	public void RaiseEvent(IDomainEvent domainEvent)
	{
		if (domainEvent == null)
		{
			throw new ArgumentNullException(nameof(domainEvent));
		}

		_events.Add(domainEvent);
	}

	public IReadOnlyList<IDomainEvent> GetEvents()
	{
		return _events.AsReadOnly();
	}

	public void ClearEvents()
	{
		_events.Clear();
	}
}
