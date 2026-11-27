namespace Beep.Website.Currency;

/// <summary>One bank's latest rates, with what the pages show about the bank.</summary>
public sealed record BankRates(string Name, string Bic, string? CountryCode, string? CountryName, IReadOnlyList<LatestRateItem> Rates)
{
	// Short names of banks whose initials don't give their usual abbreviation.
	private static readonly Dictionary<string, string> KnownBadges = new(StringComparer.OrdinalIgnoreCase)
	{
		["BFTVVNVX"] = "VCB",
	};

	private static readonly string[] NameSeparators = [" – ", " — ", " - "];

	/// <summary>Groups rates by bank, ordered by name.</summary>
	public static IReadOnlyList<BankRates> FromRates(IEnumerable<LatestRateItem> rates) =>
		rates
			.GroupBy(r => r.Bic)
			.Select(g => new BankRates(g.First().BankName, g.Key, g.First().CountryCode, g.First().CountryName, g.ToList()))
			.OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase)
			.ToList();

	/// <summary>
	/// Up to three letters for the round badge: the initials of what follows the name's last dash, its usual
	/// name ("VietinBank" in "Ngân hàng TMCP Công Thương Việt Nam – VietinBank"), or of the whole name.
	/// The pages show the bank's full name (<see cref="Name"/>) as it is in dbo.Bank.
	/// </summary>
	public string Badge
	{
		get
		{
			if (KnownBadges.TryGetValue(Bic, out var known))
				return known;
			var usual = UsualName(Name);
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

	// What follows the name's last dash, else the name: only for the badge's letters.
	private static string UsualName(string name)
	{
		foreach (var separator in NameSeparators)
		{
			var at = name.LastIndexOf(separator, StringComparison.Ordinal);
			if (at > 0 && at + separator.Length < name.Length)
				return name[(at + separator.Length)..].Trim();
		}
		return name;
	}
}
