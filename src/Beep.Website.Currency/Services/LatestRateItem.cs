namespace Beep.Website.Currency;

public sealed record LatestRateItem(
	string Bic,
	string BankName,
	string? CountryCode,
	string? CountryName,
	string CurrencyCode,
	string CurrencyName,
	DateOnly Date,
	DateTimeOffset? RateTime,
	decimal? BuyingRateCash,
	decimal? BuyingRateTransfer,
	decimal? SellingRate,
	string? CurrencySymbol,
	string? QuoteCurrencyCode);
