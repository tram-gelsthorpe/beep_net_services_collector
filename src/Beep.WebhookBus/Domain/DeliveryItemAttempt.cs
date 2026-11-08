using System;

namespace Beep.WebhookBus.Domain;

/// <summary>
/// Represents delivery attempt metadata for a webhook delivery.
/// Records the result of each attempt (success, failure, timeout, etc.)
/// </summary>
public record DeliveryItemAttempt
{
	public required Guid DeliveryId { get; init; }
	public required int AttemptNumber { get; init; }
	public required DateTimeOffset AttemptedAt { get; init; }
	public required bool IsSuccessful { get; init; }
	public int? HttpStatusCode { get; init; }
	public string? ErrorMessage { get; init; }
	public string? ResponseContent { get; init; }
}
