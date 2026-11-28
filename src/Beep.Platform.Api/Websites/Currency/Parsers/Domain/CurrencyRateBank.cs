namespace Beep.Platform.Api.Websites.Currency.Parsers;

/// <summary>
/// One bank's rates for one currency at one time, as recorded in <c>dbo.Currency_Rate_Bank</c>.
/// </summary>
/// <param name="CurrencyCode">ISO code, matched to <c>dbo.Currency.CodeISO</c></param>
/// <param name="Date">Day the rates apply to (<c>Date</c>)</param>
/// <param name="RateTime">When the bank published these rates (<c>RateTime</c>)</param>
/// <param name="BuyingRateCash">Bank buys cash; null when the bank does not buy this currency in cash</param>
/// <param name="BuyingRateTransfer">Bank buys by transfer</param>
/// <param name="SellingRate">Bank sells</param>
public sealed record CurrencyRateBank(
	string CurrencyCode,
	DateOnly Date,
	DateTimeOffset RateTime,
	decimal? BuyingRateCash,
	decimal? BuyingRateTransfer,
	decimal? SellingRate);
