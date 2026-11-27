namespace Beep.Api.Repositories.Currency;

/// <summary>A bank that has at least one rate in <c>dbo.Currency_Rate_Bank</c>.</summary>
public sealed record BankWithRates(Guid CountryId, string Bic, string Name, DateTime LatestDate);
