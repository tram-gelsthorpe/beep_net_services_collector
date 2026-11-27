namespace Beep.Website.Currency;

public sealed record SubscribeBody(string? Email, string? Country, List<string>? Banks, List<string>? Currencies);
