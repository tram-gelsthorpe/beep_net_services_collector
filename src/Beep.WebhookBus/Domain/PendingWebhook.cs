using System;

namespace Beep.WebhookBus.Domain;

/// <summary>
/// Represents a pending webhook delivery to be processed.
/// Generic model suitable for any webhook event delivery workflow.
/// </summary>
public record PendingWebhook
{
	public required Guid DeliveryId { get; init; }
	public required Guid WebhookId { get; init; }
	public required Guid EventId { get; init; }
	public required int AttemptNumber { get; init; }
	public required string WebhookUrl { get; init; }
	public required int TimeoutSeconds { get; init; }
	public required int MaxRetryAttempts { get; init; }
	public required string RawPayload { get; init; }
}
