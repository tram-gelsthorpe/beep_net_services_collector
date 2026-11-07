using System.Collections.Generic;

namespace Beep.WebhookDelivery.Infrastructure.Events;

/// <summary>
/// Aggregates and dispatches domain events published during webhook delivery processing.
/// </summary>
public interface IEventAggregator
{
	void RaiseEvent(IDomainEvent domainEvent);
	IReadOnlyList<IDomainEvent> GetEvents();
	void ClearEvents();
}
