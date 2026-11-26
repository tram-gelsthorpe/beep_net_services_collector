using System.Globalization;
using Beep.Website.Currency.Localization;
using Beep.Website.Currency.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beep.Website.Currency.Pages;

/// <summary>
/// Pick a country, a date and/or a bank (name or BIC) and see each bank's latest rate per currency on or
/// before that date. The filters are in the query string, so a search can be bookmarked or shared.
/// </summary>
public sealed class IndexModel(BeepApiClient api, UiText text, ILogger<IndexModel> logger) : PageModel
{
	/// <summary>Currencies shown as highlight cards above a single bank's table, in this order.</summary>
	public static readonly IReadOnlyList<string> FeaturedCurrencies = ["USD", "GBP", "EUR"];

	[BindProperty(SupportsGet = true)]
	public string? Country { get; set; }

	[BindProperty(SupportsGet = true)]
	public DateOnly? Date { get; set; }

	/// <summary>A BIC or part of a bank name.</summary>
	[BindProperty(SupportsGet = true)]
	public string? Bank { get; set; }

	public IReadOnlyList<CountryItem> Countries { get; private set; } = [];
	public IReadOnlyList<BankItem> Banks { get; private set; } = [];

	/// <summary>Latest rates grouped by bank; null before the first search.</summary>
	public IReadOnlyList<BankRates>? Results { get; private set; }

	public string? Error { get; private set; }

	public bool HasSearch =>
		!string.IsNullOrWhiteSpace(Country) || Date is not null || !string.IsNullOrWhiteSpace(Bank);

	/// <summary>Highlight cards: shown when the search found exactly one bank.</summary>
	public IReadOnlyList<LatestRateItem> Featured =>
		Results is [var only]
			? FeaturedCurrencies
				.Select(code => only.Rates.FirstOrDefault(r => string.Equals(r.CurrencyCode, code, StringComparison.OrdinalIgnoreCase)))
				.OfType<LatestRateItem>()
				.ToList()
			: [];

	public async Task OnGetAsync(CancellationToken cancellationToken)
	{
		try
		{
			Countries = await api.GetCountriesAsync(cancellationToken);
			Banks = await api.GetBanksAsync(Country, cancellationToken);

			if (!HasSearch)
				return;

			var rates = await api.GetLatestRatesAsync(Date, Country, Bank, cancellationToken);
			Results = rates
				.GroupBy(r => r.Bic)
				.Select(g => new BankRates(g.First().BankName, g.Key, g.First().CountryName, g.ToList()))
				.OrderBy(b => b.ShortName, StringComparer.CurrentCultureIgnoreCase)
				.ToList();
		}
		catch (HttpRequestException ex)
		{
			logger.LogError(ex, "Beep.Api could not be reached or refused the request");
			Error = ex.StatusCode is { } status
				? text.Format("The rates service answered {0}.", $"{(int)status} ({status})")
				: text["The rates service could not be reached."];
		}
	}

	/// <summary>dd/MM/yyyy whatever the language.</summary>
	public static string FormatDate(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

	/// <summary>dd/MM HH:mm in the bank's own time.</summary>
	public static string FormatPublished(DateTimeOffset at) => at.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);

	public static string Format(decimal? rate) =>
		rate is { } value ? value.ToString("#,##0.00", CultureInfo.InvariantCulture) : "—";

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

	public sealed record BankRates(string Name, string Bic, string? CountryName, IReadOnlyList<LatestRateItem> Rates)
	{
		// Short names of banks whose initials don't give their usual abbreviation.
		private static readonly Dictionary<string, string> KnownBadges = new(StringComparer.OrdinalIgnoreCase)
		{
			["BFTVVNVX"] = "VCB",
		};

		private static readonly string[] NameSeparators = [" – ", " — ", " - "];

		/// <summary>"Vietcombank" from "Ngân hàng TMCP Ngoại thương Việt Nam – Vietcombank".</summary>
		public string ShortName => SplitName().Short;

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
				var initials = words.Length > 1
					? string.Concat(words.Take(3).Select(w => char.ToUpperInvariant(w[0])))
					: ShortName[..Math.Min(3, ShortName.Length)].ToUpperInvariant();
				return initials;
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
		public decimal MaxSpread => Rates.Select(Spread).OfType<decimal>().DefaultIfEmpty(0m).Max();

		/// <summary>Width of a spread bar, in percent of the column.</summary>
		public int SpreadBarWidth(decimal? spread) =>
			spread is { } value && MaxSpread > 0 ? (int)Math.Round(Math.Clamp(value / MaxSpread, 0m, 1m) * 100m) : 0;

		private (string Short, string? Legal) SplitName()
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
}
