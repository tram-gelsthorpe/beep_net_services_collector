namespace Beep.Website.Currency;

/// <summary>Beep.Api's answer to a subscription it refused: <c>{ "errors": { field: message } }</c>.</summary>
internal sealed record SubscribeErrorsResponse(Dictionary<string, string>? Errors);
