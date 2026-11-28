namespace Beep.Platform.Api.Websites.Currency.Repositories.Country;

/// <summary>A country of <c>dbo.Country</c> (A_1_Beep_Global_Country).</summary>
/// <param name="CurrencyId">The country's currency in A_1_Beep_Global_Currency's <c>dbo.Currency</c>, if known</param>
public sealed record CountryInfo(Guid Id, string Code, string Name, Guid? CurrencyId);
