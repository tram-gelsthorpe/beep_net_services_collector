namespace Beep.Api.Controllers.Currency;

/// <param name="Bic">Bank BIC</param>
/// <param name="BankName">Bank name</param>
/// <param name="CountryCode">Bank's country code</param>
/// <param name="CountryName">Bank's country name</param>
/// <param name="CurrencyCode">ISO currency code</param>
/// <param name="CurrencyName">Currency name</param>
/// <param name="Date">Day the rate applies to</param>
/// <param name="RateTime">When the bank published it, in the bank's local time</param>
/// <param name="BuyingRateCash">Bank buys cash (null: not offered)</param>
/// <param name="BuyingRateTransfer">Bank buys by transfer</param>
/// <param name="SellingRate">Bank sells</param>
/// <param name="CurrencySymbol">Symbol of the currency ($, €), if known</param>
/// <param name="QuoteCurrencyCode">Currency the rates are in: the bank country's currency, if known</param>
/// <param name="BankShortName">The bank's usual name ("Vietcombank"), if it has one</param>
/// <param name="IsReferenceRate">A central bank's reference rate (the ECB's): one middle rate, not a bank's prices</param>
public sealed record LatestRateResponse(
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
	string? QuoteCurrencyCode,
	string? BankShortName,
	bool IsReferenceRate);
