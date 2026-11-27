namespace Beep.Website.Currency;

public sealed record BankItem(string Bic, string Name, string? CountryCode, string? CountryName, DateOnly LatestDate);
