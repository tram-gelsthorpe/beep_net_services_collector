namespace Beep.Api.Repositories.Currency;

/// <param name="Inserted">Rates recorded</param>
/// <param name="Duplicates">Already recorded (same bank, currency, day and time; or unchanged, with skipUnchanged)</param>
/// <param name="UnknownCurrencies">Codes not in dbo.Currency; nothing recorded for them</param>
public sealed record CurrencyRateSaveResult(int Inserted, int Duplicates, IReadOnlyList<string> UnknownCurrencies);
