using System.Globalization;

namespace Beep.Website.Currency;

/// <summary>How rates, dates and spreads are written on every page, whatever the language.</summary>
public static class RateFormat
{
	/// <summary>Badge colours of banks in lists (background, text), by position.</summary>
	public static readonly IReadOnlyList<(string Tint, string Colour)> BankColours =
	[
		("#E0E7FF", "#2547D0"),
		("#E0F2FE", "#075985"),
		("#EDE9FE", "#5B21B6"),
		("#FFEDD5", "#9A3412"),
		("#FCE7F3", "#9D174D"),
	];

	// Currencies whose amounts are written without decimals (ISO 4217 minor unit 0): 28,989,550 VND.
	private static readonly HashSet<string> WholeCurrencies = new(StringComparer.OrdinalIgnoreCase)
	{
		"BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF",
	};

	/// <summary>
	/// A rate: two decimals, and on a small rate as many as give it five significant digits (at most ten):
	/// 25,640.00 VND, 18.66 VND, 0.91266 EUR, 0.0057205 EUR. Trailing zeros after the second decimal are left out.
	/// </summary>
	public static string Format(decimal? rate) =>
		rate is { } value ? value.ToString("#,##0.00" + new string('#', RateDecimals(value) - 2), CultureInfo.InvariantCulture) : "—";

	/// <summary>Most decimals <see cref="Format"/> writes for the rate: 2 to 10.</summary>
	public static int RateDecimals(decimal rate)
	{
		if (rate == 0)
			return 2;
		var magnitude = (int)Math.Floor(Math.Log10((double)Math.Abs(rate)));
		return Math.Clamp(4 - magnitude, 2, 10);
	}

	/// <summary>Decimals of an amount in the currency: 0 for VND (28,989,550), else 2 (912.66 EUR).</summary>
	public static int AmountDecimals(string? currency) =>
		currency is null || WholeCurrencies.Contains(currency) ? 0 : 2;

	/// <summary>An amount in the currency: whole for VND (28,989,550), with cents for EUR (912.66); whole when unknown.</summary>
	public static string FormatAmount(decimal? amount, string? currency = null)
	{
		if (amount is not { } value)
			return "—";
		var decimals = AmountDecimals(currency);
		return Math.Round(value, decimals, MidpointRounding.AwayFromZero)
			.ToString(decimals == 0 ? "#,##0" : "#,##0.00", CultureInfo.InvariantCulture);
	}

	/// <summary>dd/MM/yyyy whatever the language.</summary>
	public static string FormatDate(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

	/// <summary>dd/MM HH:mm in the bank's own time.</summary>
	public static string FormatPublished(DateTimeOffset at) => at.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);

	/// <summary>(sell − buy transfer) ÷ buy transfer, in percent; null when either is missing.</summary>
	public static decimal? Spread(LatestRateItem rate) =>
		rate is { SellingRate: { } sell, BuyingRateTransfer: { } buy } && buy > 0 ? (sell - buy) / buy * 100m : null;

	public static string FormatSpread(decimal? spread) =>
		spread is { } value ? value.ToString("0.00", CultureInfo.InvariantCulture) + "%" : "—";

	/// <summary>"UTC+7", "UTC−3:30", "UTC".</summary>
	public static string FormatOffset(TimeSpan offset)
	{
		if (offset == TimeSpan.Zero)
			return "UTC";
		var sign = offset < TimeSpan.Zero ? "−" : "+";
		var abs = offset.Duration();
		return $"UTC{sign}{abs.Hours}{(abs.Minutes == 0 ? "" : $":{abs.Minutes:00}")}";
	}

	/// <summary>Colour theme of a highlight card: usd, gbp or eur.</summary>
	public static string FeaturedTheme(string currencyCode) => currencyCode.ToLowerInvariant();
}
