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

	/// <summary>Groups rates by bank, ordered by short name.</summary>
	public static IReadOnlyList<BankRates> FromRates(IEnumerable<LatestRateItem> rates) =>
		rates
			.GroupBy(r => r.Bic)
			.Select(g => new BankRates(g.First().BankName, g.Key, g.First().CountryCode, g.First().CountryName, g.ToList()))
			.OrderBy(b => b.ShortName, StringComparer.CurrentCultureIgnoreCase)
			.ToList();

	/// <summary>"Vietcombank" from "Ngân hàng TMCP Ngoại thương Việt Nam – Vietcombank".</summary>
	public string ShortName => SplitName().Short;

	/// <summary>The short name of a bank name, as <see cref="ShortName"/>.</summary>
	public static string ShortNameOf(string name) => SplitName(name).Short;

	/// <summary>The legal name, when the bank's name has a short name after a dash; otherwise null.</summary>
	public string? LegalName => SplitName().Legal;

	/// <summary>Up to three letters for the round badge.</summary>
	public string Badge
	{
		get
		{
			if (KnownBadges.TryGetValue(Bic, out var known))
				return known;
			var words = ShortName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			return words.Length > 1
				? string.Concat(words.Take(3).Select(w => char.ToUpperInvariant(w[0])))
				: ShortName[..Math.Min(3, ShortName.Length)].ToUpperInvariant();
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

	private (string Short, string? Legal) SplitName() => SplitName(Name);

	private static (string Short, string? Legal) SplitName(string Name)
	{
		foreach (var separator in NameSeparators)
		{
			var at = Name.LastIndexOf(separator, StringComparison.Ordinal);
			if (at > 0 && at + separator.Length < Name.Length)
				return (Name[(at + separator.Length)..].Trim(), Name[..at].Trim());
		}
		return (Name, null);
	}
}
