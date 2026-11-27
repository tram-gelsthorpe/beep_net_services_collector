namespace Beep.Website.Currency;

/// <summary>One bank's latest rates, with what the pages show about the bank.</summary>
/// <param name="Name">The bank's full name (dbo.Bank.LongName).</param>
/// <param name="ShortName">Its usual name ("Vietcombank", dbo.Bank.ShortName), if it has one.</param>
public sealed record BankRates(string Name, string Bic, string? CountryCode, string? CountryName, IReadOnlyList<LatestRateItem> Rates, string? ShortName = null)
{
	// Short names of banks whose initials don't give their usual abbreviation.
	private static readonly Dictionary<string, string> KnownBadges = new(StringComparer.OrdinalIgnoreCase)
	{
		["BFTVVNVX"] = "VCB",
	};

	/// <summary>Groups rates by bank, ordered by the name shown.</summary>
	public static IReadOnlyList<BankRates> FromRates(IEnumerable<LatestRateItem> rates) =>
		rates
			.GroupBy(r => r.Bic)
			.Select(g => new BankRates(g.First().BankName, g.Key, g.First().CountryCode, g.First().CountryName, g.ToList(), g.First().BankShortName))
			.OrderBy(b => b.DisplayName, StringComparer.CurrentCultureIgnoreCase)
			.ToList();

	/// <summary>The name to show first: the short name ("Vietcombank"), else the full name.</summary>
	public string DisplayName => ShortName ?? Name;

	/// <summary>The full name to show under <see cref="DisplayName"/>: null when that is already the full name.</summary>
	public string? FullName => ShortName is null ? null : Name;

	/// <summary>Up to three letters for the round badge: the initials of <see cref="DisplayName"/>.</summary>
	public string Badge
	{
		get
		{
			if (KnownBadges.TryGetValue(Bic, out var known))
				return known;
			var usual = DisplayName;
			var words = usual.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			return words.Length > 1
				? string.Concat(words.Take(3).Select(w => char.ToUpperInvariant(w[0])))
				: usual[..Math.Min(3, usual.Length)].ToUpperInvariant();
		}
	}

	public DateOnly FirstDate => Rates.Min(r => r.Date);
	public DateOnly LastDate => Rates.Max(r => r.Date);
	public bool SameDate => FirstDate == LastDate;

	/// <summary>When the rates were published, if they all share one time.</summary>
	public DateTimeOffset? PublishedAt =>
		Rates.Select(r => r.RateTime).Distinct().Count() == 1 ? Rates[0].RateTime : null;

	/// <summary>Currency the rates are in (VND), when known.</summary>
	public string? QuoteCurrency => Rates.Select(r => r.QuoteCurrencyCode).FirstOrDefault(c => c is not null);

	/// <summary>Largest spread in the table: the full width of the spread bars.</summary>
	public decimal MaxSpread => Rates.Select(RateFormat.Spread).OfType<decimal>().DefaultIfEmpty(0m).Max();

	/// <summary>Width of a spread bar, in percent of the column.</summary>
	public int SpreadBarWidth(decimal? spread) =>
		spread is { } value && MaxSpread > 0 ? (int)Math.Round(Math.Clamp(value / MaxSpread, 0m, 1m) * 100m) : 0;

	public LatestRateItem? Rate(string currencyCode) =>
		Rates.FirstOrDefault(r => string.Equals(r.CurrencyCode, currencyCode, StringComparison.OrdinalIgnoreCase));
}
