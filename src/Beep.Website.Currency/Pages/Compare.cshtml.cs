using System.Globalization;
using Beep.Website.Currency.Localization;
using Beep.Website.Currency.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beep.Website.Currency.Pages;

/// <summary>
/// Compare banks: one currency across the banks of one country, on or before a date. Shows the cheapest bank
/// to buy from (lowest sell rate), the best bank to sell to (highest transfer buy rate), the difference
/// between the best and worst bank for the amount, and every bank's rates. Everything is in the query string:
/// <c>/Compare?country=VN&amp;currency=EUR&amp;amount=1000&amp;date=2026-11-26&amp;side=buy&amp;sort=sell</c>.
/// </summary>
public sealed class CompareModel(BeepApiClient api, UiText text, ILogger<CompareModel> logger) : PageModel
{
	public const decimal DefaultAmount = 1000m;
	private static readonly string[] PreferredCurrencies = ["USD", "EUR"];

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

	/// <summary>sell, buy or spread.</summary>
	[BindProperty(SupportsGet = true)]
	public string? Sort { get; set; }

	public IReadOnlyList<CountryItem> Countries { get; private set; } = [];

	/// <summary>Currencies quoted by the country's banks: code and name.</summary>
	public IReadOnlyList<(string Code, string Name)> Currencies { get; private set; } = [];

	public decimal Amount { get; private set; } = DefaultAmount;
	public bool Buying => !string.Equals(Side, "sell", StringComparison.OrdinalIgnoreCase);
	public string SortBy { get; private set; } = "sell";

	/// <summary>Each bank and its rate for <see cref="Currency"/>, in <see cref="SortBy"/> order.</summary>
	public IReadOnlyList<(BankRates Bank, LatestRateItem Rate)> Rows { get; private set; } = [];

	public string? CountryName { get; private set; }
	public string? QuoteCurrency { get; private set; }
	public string? Error { get; private set; }

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

	public async Task OnGetAsync(CancellationToken cancellationToken)
	{
		Amount = ParseAmount(AmountText) ?? DefaultAmount;
		try
		{
			Countries = await api.GetCountriesAsync(cancellationToken);

			// Banks are compared within one country, so the rates share one currency (VND).
			var country = Countries.FirstOrDefault(c => string.Equals(c.Code, Country, StringComparison.OrdinalIgnoreCase))
				?? Countries.FirstOrDefault();
			if (country is null)
				return;
			Country = country.Code;
			CountryName = country.Name;

			var banks = BankRates.FromRates(await api.GetLatestRatesAsync(Date, country.Code, null, cancellationToken));
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

			SortBy = Sort?.ToLowerInvariant() is "sell" or "buy" or "spread" ? Sort.ToLowerInvariant() : Buying ? "sell" : "buy";
			var rows = banks
				.Select(b => (Bank: b, Rate: b.Rate(Currency)))
				.Where(r => r.Rate is not null)
				.Select(r => (r.Bank, Rate: r.Rate!))
				.ToList();
			QuoteCurrency = banks.Select(b => b.QuoteCurrency).FirstOrDefault(q => q is not null);

			// Missing rates go last whatever the order.
			Rows = SortBy switch
			{
				"buy" => rows.OrderBy(r => r.Rate.BuyingRateTransfer is null).ThenByDescending(r => r.Rate.BuyingRateTransfer).ToList(),
				"spread" => rows.OrderBy(r => RateFormat.Spread(r.Rate) is null).ThenBy(r => RateFormat.Spread(r.Rate)).ToList(),
				_ => rows.OrderBy(r => r.Rate.SellingRate is null).ThenBy(r => r.Rate.SellingRate).ToList(),
			};
		}
		catch (HttpRequestException ex)
		{
			logger.LogError(ex, "Beep.Api could not be reached or refused the request");
			Error = IndexModel.ApiError(text, ex);
		}
	}

	/// <summary>The same page in another order.</summary>
	public string SortUrl(string sort) =>
		Url.Page("/Compare", new
		{
			country = Country,
			currency = Currency,
			amount = Amount.ToString("0.##", CultureInfo.InvariantCulture),
			date = Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
			side = Buying ? "buy" : "sell",
			sort,
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
