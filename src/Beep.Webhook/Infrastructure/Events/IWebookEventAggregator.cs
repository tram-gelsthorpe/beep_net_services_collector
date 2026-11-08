using System.Collections.Generic;

namespace Beep.Webhook.Infrastructure.Events;

/// <summary>
/// Aggregates and dispatches domain events published during webhook delivery processing.
/// </summary>
public interface IWebookEventAggregator
{
	void RaiseEvent(IWebhookEvent domainEvent);
	IReadOnlyList<IWebhookEvent> GetEvents();
	void ClearEvents();
}
