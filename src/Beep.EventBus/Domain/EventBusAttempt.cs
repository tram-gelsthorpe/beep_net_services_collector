using System;

namespace Beep.EventBus;

/// <summary>
/// Represents delivery attempt metadata for a EventBus delivery.
/// Records the result of each attempt (success, failure, timeout, etc.)
/// </summary>
public record EventBusAttempt
{
	public required Guid EventBusDeliveryId { get; init; }
	public required int AttemptNumber { get; init; }
	public required DateTimeOffset AttemptedAt { get; init; }
	public required bool IsSuccessful { get; init; }
	public int? HttpStatusCode { get; init; }
	public string? ErrorMessage { get; init; }
	public string? ResponseContent { get; init; }
}
