namespace Beep.EventBus;

/// <summary>Outcome of one HTTP delivery attempt.</summary>
public sealed record DeliveryOutcome(bool Delivered, int? HttpStatusCode, string? Error);
