using System;

namespace Beep.WebhookBus.Infrastructure.Events;

/// <summary>
/// Base interface for all domain events published by the webhook delivery system.
/// </summary>
public interface IWebhookEvent
{
	Guid EventId { get; }
	DateTimeOffset OccurredAt { get; }
	string EventType { get; }
}
