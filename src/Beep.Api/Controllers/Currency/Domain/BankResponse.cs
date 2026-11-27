namespace Beep.Api.Controllers.Currency;

/// <param name="Name">The bank's full name</param>
/// <param name="ShortName">Its usual name ("Vietcombank"), if it has one</param>
public sealed record BankResponse(string Bic, string Name, string? CountryCode, string? CountryName, DateOnly LatestDate, string? ShortName);
