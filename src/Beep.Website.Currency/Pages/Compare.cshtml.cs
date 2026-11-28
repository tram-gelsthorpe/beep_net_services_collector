using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beep.Website.Currency;

/// <summary>
/// Compare banks: one currency across the banks of the chosen countries, on or before a date.
/// <list type="bullet">
/// <item><b>One country</b> (default: the first): its banks quote in one currency (VND), so they are compared
/// on price: cheapest bank to buy from (lowest sell rate), best bank to sell to (highest transfer buy rate),
/// best vs. worst for the amount, and "you pay" = amount × sell rate.</item>
/// <item><b>Two or more countries</b>: each bank quotes in its own currency, so banks are compared by spread:
/// lowest spread anywhere, best country by average spread, and coverage. Each bank's "you pay" for the amount is
/// in its own currency.</item>
/// </list>
/// At most <see cref="DefaultBanks"/> banks are compared at first: the first ones in the table's order. The visitor
/// removes a bank (×) or adds another ("+ Add a bank"); the compared banks are then in <c>banks</c>.
/// Everything is in the query string: <c>/Compare?countries=VN&amp;currency=EUR&amp;amount=1000
/// &amp;date=2026-11-26&amp;side=buy&amp;sort=sell&amp;banks=BFTVVNVX&amp;banks=VBAAVNVX</c>; with several
/// <c>countries</c>, <c>only</c> filters the table by country and <c>sort</c> is spread or country.
/// <c>scope=all</c> (older links) chooses every country.
/// </summary>
public sealed class CompareModel(BeepApiClient api, UiText text, ILogger<CompareModel> logger) : PageModel
{
	public const decimal DefaultAmount = 1000m;

	/// <summary>How many banks are compared until the visitor chooses them.</summary>
	public const int DefaultBanks = 6;
	private static readonly string[] PreferredCurrencies = ["USD", "EUR"];

	/// <summary>Older links: <c>all</c> chooses every country.</summary>
	[BindProperty(SupportsGet = true)]
	public string? Scope { get; set; }

	/// <summary>The chosen country codes (<c>countries=VN&amp;countries=TH</c>); default: the first country.</summary>
	[BindProperty(SupportsGet = true, Name = "countries")]
	public List<string> ChosenCountries { get; set; } = [];

	/// <summary>Older links and the one-country mode: a single country code.</summary>
	[BindProperty(SupportsGet = true)]
	public string? Country { get; set; }

	[BindProperty(SupportsGet = true)]
	public string? Currency { get; set; }

	/// <summary>As typed: "1,000" or "1000.50".</summary>
	[BindProperty(SupportsGet = true, Name = "amount")]
	public string? AmountText { get; set; }

	[BindProperty(SupportsGet = true)]
	public DateOnly? Date { get; set; }

	/// <summary>buy: I want to buy the currency (banks sell it); sell: I want to sell it (banks buy it).</summary>
	[BindProperty(SupportsGet = true)]
	public string? Side { get; set; }

	/// <summary>One country: sell, buy or spread. All countries: spread or country.</summary>
	[BindProperty(SupportsGet = true)]
	public string? Sort { get; set; }

	/// <summary>All countries: show only this country's banks in the table (a country code).</summary>
	[BindProperty(SupportsGet = true)]
	public string? Only { get; set; }

	/// <summary>
	/// The compared banks' BICs (<c>banks=BFTVVNVX&amp;banks=VBAAVNVX</c>), once the visitor has added or removed one.
	/// Empty: the first <see cref="DefaultBanks"/> in the table's order. After loading: the BICs compared.
	/// </summary>
	[BindProperty(SupportsGet = true, Name = "banks")]
	public List<string> ChosenBanks { get; set; } = [];

	/// <summary>The visitor chose the compared banks (<c>banks</c> named at least one that quotes the currency).</summary>
	public bool BanksChosen { get; private set; }

	/// <summary>The banks that quote the currency but are not compared, in the table's order: "+ Add a bank" offers them.</summary>
	public IReadOnlyList<(BankRates Bank, LatestRateItem Rate)> OtherBanks { get; private set; } = [];

	/// <summary>Every bank that quotes the currency in the chosen countries: compared or not.</summary>
	public int BanksQuoting { get; private set; }

	public IReadOnlyList<CountryItem> Countries { get; private set; } = [];

	/// <summary>Currencies quoted by the compared banks: code and name.</summary>
	public IReadOnlyList<(string Code, string Name)> Currencies { get; private set; } = [];

	/// <summary>Two or more countries are chosen: banks are compared by spread.</summary>
	public bool AllCountries => ChosenCountries.Count > 1;

	/// <summary>Every country is chosen.</summary>
	public bool EveryCountry => Countries.Count > 0 && ChosenCountries.Count == Countries.Count;
	public decimal Amount { get; private set; } = DefaultAmount;
	public bool Buying => !string.Equals(Side, "sell", StringComparison.OrdinalIgnoreCase);
	public string SortBy { get; private set; } = "sell";

	/// <summary>Each bank and its rate for <see cref="Currency"/>, in <see cref="SortBy"/> order.</summary>
	public IReadOnlyList<(BankRates Bank, LatestRateItem Rate)> Rows { get; private set; } = [];

	/// <summary>All countries: the compared banks, before the <see cref="Only"/> filter.</summary>
	public IReadOnlyList<(BankRates Bank, LatestRateItem Rate)> AllRows { get; private set; } = [];

	/// <summary>Countries of <see cref="AllRows"/>, for the table's country filter: code and name.</summary>
	public IReadOnlyList<(string Code, string Name)> RowCountries { get; private set; } = [];

	public string? CountryName { get; private set; }
	public string? QuoteCurrency { get; private set; }
	public string? Error { get; private set; }

	// ---------------------------------------------------------------- one country

	public (BankRates Bank, LatestRateItem Rate)? CheapestToBuy =>
		Rows.Where(r => r.Rate.SellingRate is not null).MinBy(r => r.Rate.SellingRate);

	public (BankRates Bank, LatestRateItem Rate)? BestToSell =>
		Rows.Where(r => r.Rate.BuyingRateTransfer is not null).MaxBy(r => r.Rate.BuyingRateTransfer);

	/// <summary>
	/// For the amount: what the best bank saves over the worst one, at the rate that matters for the
	/// chosen side (sell rate when buying, transfer buy rate when selling), and how many banks were compared.
	/// </summary>
	public (decimal Difference, int Banks) BestVsWorst
	{
		get
		{
			var rates = Rows.Select(r => Buying ? r.Rate.SellingRate : r.Rate.BuyingRateTransfer).OfType<decimal>().ToList();
			return rates.Count == 0 ? (0m, 0) : ((rates.Max() - rates.Min()) * Amount, rates.Count);
		}
	}

	// ---------------------------------------------------------------- all countries

	/// <summary>The bank with the lowest spread, of all countries. Reference rates don't count (see <see cref="RankedSpread"/>).</summary>
	public (BankRates Bank, LatestRateItem Rate, decimal Spread)? LowestSpread =>
		AllRows.Select(r => (r.Bank, r.Rate, Spread: RankedSpread(r.Rate)))
			.Where(r => r.Spread is not null)
			.Select(r => (r.Bank, r.Rate, Spread: r.Spread!.Value))
			.MinBy(r => r.Spread) is { Bank: not null } best ? best : null;

	/// <summary>The country whose banks have the lowest average spread. Reference rates don't count: a country with only those has none.</summary>
	public (string Country, decimal AverageSpread, int Banks)? BestCountry =>
		AllRows.Select(r => (Country: r.Rate.CountryName ?? r.Rate.CountryCode ?? "—", Spread: RankedSpread(r.Rate)))
			.Where(r => r.Spread is not null)
			.GroupBy(r => r.Country)
			.Select(g => (Country: g.Key, AverageSpread: g.Average(r => r.Spread!.Value), Banks: g.Count()))
			.OrderBy(c => c.AverageSpread)
			.Select(c => ((string, decimal, int)?)c)
			.FirstOrDefault();

	public int CoverageCountries => AllRows.Select(r => r.Rate.CountryCode).Distinct().Count();

	/// <summary>
	/// The spread a bank is ranked by: null for a central bank's reference rate (the ECB's, for Belgium). That is one
	/// middle rate recorded as both buying and selling rate, so its spread is always 0: no bank's real price could beat
	/// it, but no one can exchange at it.
	/// </summary>
	private static decimal? RankedSpread(LatestRateItem rate) => rate.IsReferenceRate ? null : RateFormat.Spread(rate);

	/// <summary>Where a row goes when sorted by spread: banks' real spreads first, then reference rates, then no spread.</summary>
	private static int SpreadGroup(LatestRateItem rate) =>
		RateFormat.Spread(rate) is null ? 2 : rate.IsReferenceRate ? 1 : 0;

	public async Task OnGetAsync(CancellationToken cancellationToken)
	{
		Amount = ParseAmount(AmountText) ?? DefaultAmount;
		try
		{
			Countries = await api.GetCountriesAsync(cancellationToken);
			var asked = string.Equals(Scope, "all", StringComparison.OrdinalIgnoreCase)
				? Countries.Select(c => c.Code).ToList()
				: ChosenCountries.Append(Country ?? string.Empty).ToList();
			ChosenCountries = Countries.Where(c => asked.Contains(c.Code, StringComparer.OrdinalIgnoreCase)).Select(c => c.Code).ToList();
			if (ChosenCountries.Count == 0 && Countries.Count > 0)
				ChosenCountries = [Countries[0].Code];
			if (ChosenCountries.Count == 0)
				return;
			var country = Countries.First(c => c.Code == ChosenCountries[0]);
			Country = AllCountries ? null : country.Code;
			CountryName = country.Name;

			// One country: its banks share one currency (VND). Several: their banks, compared by spread.
			var rates = await api.GetLatestRatesAsync(Date, AllCountries ? null : country.Code, null, cancellationToken);
			var banks = BankRates.FromRates(AllCountries
				? rates.Where(r => ChosenCountries.Contains(r.CountryCode ?? string.Empty, StringComparer.OrdinalIgnoreCase))
				: rates);

			Currencies = banks.SelectMany(b => b.Rates)
				.GroupBy(r => r.CurrencyCode, StringComparer.OrdinalIgnoreCase)
				.Select(g => (g.Key, g.First().CurrencyName))
				.OrderBy(c => c.Key, StringComparer.Ordinal)
				.ToList();

			Currency = Currencies.Select(c => c.Code).FirstOrDefault(c => string.Equals(c, Currency, StringComparison.OrdinalIgnoreCase))
				?? PreferredCurrencies.FirstOrDefault(p => Currencies.Any(c => c.Code == p))
				?? Currencies.Select(c => c.Code).FirstOrDefault();
			if (Currency is null)
				return;

			var rows = banks
				.Select(b => (Bank: b, Rate: b.Rate(Currency)))
				.Where(r => r.Rate is not null)
				.Select(r => (r.Bank, Rate: r.Rate!))
				.ToList();

			if (AllCountries)
				SortAllCountries(rows);
			else
				SortOneCountry(rows, banks);
		}
		catch (HttpRequestException ex)
		{
			logger.LogError(ex, "Beep.Platform.Api could not be reached or refused the request");
			Error = IndexModel.ApiError(text, ex);
		}
	}

	private void SortOneCountry(List<(BankRates Bank, LatestRateItem Rate)> rows, IReadOnlyList<BankRates> banks)
	{
		QuoteCurrency = banks.Select(b => b.QuoteCurrency).FirstOrDefault(q => q is not null);
		SortBy = Sort?.ToLowerInvariant() is "sell" or "buy" or "spread" ? Sort.ToLowerInvariant() : Buying ? "sell" : "buy";

		// Missing rates go last whatever the order.
		Rows = Compared(SortBy switch
		{
			"buy" => rows.OrderBy(r => r.Rate.BuyingRateTransfer is null).ThenByDescending(r => r.Rate.BuyingRateTransfer).ToList(),
			"spread" => rows.OrderBy(r => SpreadGroup(r.Rate)).ThenBy(r => RateFormat.Spread(r.Rate)).ToList(),
			_ => rows.OrderBy(r => r.Rate.SellingRate is null).ThenBy(r => r.Rate.SellingRate).ToList(),
		});
	}

	private void SortAllCountries(List<(BankRates Bank, LatestRateItem Rate)> rows)
	{
		SortBy = string.Equals(Sort, "country", StringComparison.OrdinalIgnoreCase) ? "country" : "spread";

		var bySpread = rows.OrderBy(r => SpreadGroup(r.Rate)).ThenBy(r => RateFormat.Spread(r.Rate));
		AllRows = Compared((SortBy == "country"
				? rows.OrderBy(r => r.Rate.CountryName, CountryNames.Order)
					.ThenBy(r => SpreadGroup(r.Rate)).ThenBy(r => RateFormat.Spread(r.Rate))
				: bySpread)
			.ToList());

		RowCountries = AllRows
			.Where(r => r.Rate.CountryCode is not null)
			.GroupBy(r => r.Rate.CountryCode!)
			.Select(g => (g.Key, g.First().Rate.CountryName ?? g.Key))
			.OrderBy(c => c.Item2, StringComparer.CurrentCultureIgnoreCase)
			.ToList();

		if (!RowCountries.Any(c => string.Equals(c.Code, Only, StringComparison.OrdinalIgnoreCase)))
			Only = null;
		Rows = Only is null
			? AllRows
			: AllRows.Where(r => string.Equals(r.Rate.CountryCode, Only, StringComparison.OrdinalIgnoreCase)).ToList();
	}

	/// <summary>
	/// The banks of <paramref name="sorted"/> to compare, in its order: those of <see cref="ChosenBanks"/>, or, when it
	/// names none of them, <see cref="DefaultBanks"/> banks: the first of each country (so no chosen country is left out,
	/// as Belgium would be with its reference rate sorted last), then the next ones in order. Sets
	/// <see cref="ChosenBanks"/> to the compared BICs and <see cref="OtherBanks"/> to the rest.
	/// </summary>
	private List<(BankRates Bank, LatestRateItem Rate)> Compared(List<(BankRates Bank, LatestRateItem Rate)> sorted)
	{
		var chosen = sorted.Where(r => ChosenBanks.Contains(r.Bank.Bic, StringComparer.OrdinalIgnoreCase)).ToList();
		BanksChosen = chosen.Count > 0;
		if (!BanksChosen)
		{
			var picked = sorted.DistinctBy(r => r.Rate.CountryCode).Take(DefaultBanks).ToHashSet();
			picked.UnionWith(sorted.Where(r => !picked.Contains(r)).Take(DefaultBanks - picked.Count));
			chosen = sorted.Where(picked.Contains).ToList();
		}

		ChosenBanks = chosen.Select(r => r.Bank.Bic).ToList();
		OtherBanks = sorted.Where(r => !ChosenBanks.Contains(r.Bank.Bic, StringComparer.OrdinalIgnoreCase)).ToList();
		BanksQuoting = sorted.Count;
		return chosen;
	}

	/// <summary>This page with some of its query values changed. The compared banks stay, once the visitor chose them.</summary>
	public string PageUrl(string? sort = null, string? only = null, bool keepOnly = true) =>
		BanksUrl(BanksChosen ? ChosenBanks : null, sort, only, keepOnly);

	/// <summary>This page comparing one more bank.</summary>
	public string AddBankUrl(string bic) => BanksUrl([.. ChosenBanks, bic]);

	/// <summary>This page without the bank; null when it is the only one compared.</summary>
	public string? RemoveBankUrl(string bic) =>
		ChosenBanks.Count > 1
			? BanksUrl(ChosenBanks.Where(b => !string.Equals(b, bic, StringComparison.OrdinalIgnoreCase)).ToList())
			: null;

	private string BanksUrl(List<string>? banks, string? sort = null, string? only = null, bool keepOnly = true) =>
		Url.Page("/Compare", new
		{
			countries = ChosenCountries,
			currency = Currency,
			amount = Amount.ToString("0.##", CultureInfo.InvariantCulture),
			date = Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
			side = Buying ? "buy" : "sell",
			sort = sort ?? SortBy,
			only = only ?? (keepOnly ? Only : null),
			banks,
		})!;

	public string AmountDisplay => Amount.ToString("#,##0.##", CultureInfo.InvariantCulture);

	/// <summary>"1,000", "1000", "1 000" or "1000.5"; null when not a positive number.</summary>
	private static decimal? ParseAmount(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return null;
		var cleaned = value.Replace(",", "").Replace(" ", "").Replace(" ", "");
		return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) && amount > 0
			? Math.Min(amount, 1_000_000_000_000m)
			: null;
	}
}
