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

	public static string Format(decimal? rate) =>
		rate is { } value ? value.ToString("#,##0.00", CultureInfo.InvariantCulture) : "—";

	/// <summary>A whole amount: 28,989,550.</summary>
	public static string FormatAmount(decimal? amount) =>
		amount is { } value ? Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("#,##0", CultureInfo.InvariantCulture) : "—";

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
