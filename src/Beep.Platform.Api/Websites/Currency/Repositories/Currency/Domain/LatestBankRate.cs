namespace Beep.Platform.Api.Websites.Currency.Repositories.Currency;

/// <summary>The latest rate of one bank for one currency.</summary>
public sealed record LatestBankRate(
	Guid CountryId,
	string Bic,
	string BankName,
	string? BankShortName,
	string CurrencyCode,
	string CurrencyName,
	string? CurrencySymbol,
	DateTime Date,
	DateTimeOffset? RateTime,
	decimal? BuyingRateCash,
	decimal? BuyingRateTransfer,
	decimal? SellingRate);
