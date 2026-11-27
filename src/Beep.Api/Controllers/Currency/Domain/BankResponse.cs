namespace Beep.Api.Controllers.Currency;

public sealed record BankResponse(string Bic, string Name, string? CountryCode, string? CountryName, DateOnly LatestDate);
