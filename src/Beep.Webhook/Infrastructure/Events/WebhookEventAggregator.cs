using System;
using System.Collections.Generic;

namespace Beep.Webhook.Infrastructure.Events;

/// <summary>
/// In-process event aggregator for collecting domain events during webhook delivery operations.
/// Events are stored in memory for eventual publication to external handlers.
/// </summary>
public sealed class WebhookEventAggregator : IWebookEventAggregator
{
	private readonly List<IWebhookEvent> _events = new();

	public void RaiseEvent(IWebhookEvent domainEvent)
	{
		if (domainEvent == null)
		{
			throw new ArgumentNullException(nameof(domainEvent));
		}

		_events.Add(domainEvent);
	}

	public IReadOnlyList<IWebhookEvent> GetEvents()
	{
		return _events.AsReadOnly();
	}

	public void ClearEvents()
	{
		_events.Clear();
	}
}
