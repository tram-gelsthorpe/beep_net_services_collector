namespace Beep.Platform.Api.Controllers.Currency;

/// <param name="Name">The bank's full name</param>
/// <param name="ShortName">Its usual name ("Vietcombank"), if it has one</param>
/// <param name="IsReferenceRate">The rates are a central bank's reference rates (the ECB's), not a bank's prices</param>
public sealed record BankResponse(string Bic, string Name, string? CountryCode, string? CountryName, DateOnly LatestDate, string? ShortName, bool IsReferenceRate);
