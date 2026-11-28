namespace Beep.Website.Currency;

/// <summary>Beep.Platform.Api's answer to a subscription it accepted: <c>{ "status": ... }</c>.</summary>
internal sealed record SubscribeStatusResponse(string? Status);
