namespace Beep.Api.Repositories.Currency;

/// <summary>A bank that has at least one rate in <c>dbo.Currency_Rate_Bank</c>.</summary>
/// <param name="Name">The bank's full name (<c>dbo.Bank.LongName</c>).</param>
/// <param name="ShortName">Its usual name ("Vietcombank", <c>dbo.Bank.ShortName</c>), if it has one.</param>
public sealed record BankWithRates(Guid CountryId, string Bic, string Name, string? ShortName, DateTime LatestDate);
