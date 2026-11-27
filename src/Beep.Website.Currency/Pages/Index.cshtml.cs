using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beep.Website.Currency;

/// <summary>
/// Bank rates: pick a country, a date and/or a bank (name or BIC) and see each bank's latest rate per currency
/// on or before that date. The filters are in the query string, so a search can be bookmarked or shared.
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
			? FeaturedCurrencies.Select(only.Rate).OfType<LatestRateItem>().ToList()
			: [];

	public async Task OnGetAsync(CancellationToken cancellationToken)
	{
		try
		{
			Countries = await api.GetCountriesAsync(cancellationToken);
			Banks = await api.GetBanksAsync(Country, cancellationToken);

			if (!HasSearch)
				return;

			Results = BankRates.FromRates(await api.GetLatestRatesAsync(Date, Country, Bank, cancellationToken));
		}
		catch (HttpRequestException ex)
		{
			logger.LogError(ex, "Beep.Api could not be reached or refused the request");
			Error = ApiError(text, ex);
		}
	}

	/// <summary>The message shown when Beep.Api fails.</summary>
	public static string ApiError(UiText text, HttpRequestException ex) =>
		ex.StatusCode is { } status
			? text.Format("The rates service answered {0}.", $"{(int)status} ({status})")
			: text["The rates service could not be reached."];
}
