using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beep.Website.Currency;

/// <summary>
/// Bank rates: one bank's latest rate per currency on or before a date. The country narrows the banks to choose
/// from; the bank (a BIC or part of its name) picks the one shown. Text matching several banks lists them to
/// choose from. The filters are in the query string, so a search can be bookmarked or shared.
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

	/// <summary>The chosen bank's latest rates (one bank, or none found); null until a bank is chosen.</summary>
	public IReadOnlyList<BankRates>? Results { get; private set; }

	/// <summary>The banks <see cref="Bank"/> matches, when it matches several: the page lists them to choose one.</summary>
	public IReadOnlyList<BankItem> Matches { get; private set; } = [];

	public string? Error { get; private set; }

	/// <summary>Highlight cards: shown above the chosen bank's table.</summary>
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

			// No bank yet: the page asks for one (a country alone does not list all its banks' tables).
			var bank = Bank?.Trim();
			if (string.IsNullOrEmpty(bank))
				return;

			// A BIC is that bank; text matching several bank names lists them to choose one.
			var matches = await api.GetBanksAsync(Country, cancellationToken, bank);
			var chosen = matches.FirstOrDefault(b => string.Equals(b.Bic, bank, StringComparison.OrdinalIgnoreCase))
				?? (matches.Count == 1 ? matches[0] : null);
			if (chosen is null && matches.Count > 1)
			{
				Matches = matches;
				return;
			}

			Results = chosen is null
				? []
				: BankRates.FromRates(await api.GetLatestRatesAsync(Date, Country, chosen.Bic, cancellationToken));
		}
		catch (HttpRequestException ex)
		{
			logger.LogError(ex, "Beep.Platform.Api could not be reached or refused the request");
			Error = ApiError(text, ex);
		}
	}

	/// <summary>The message shown when Beep.Platform.Api fails.</summary>
	public static string ApiError(UiText text, HttpRequestException ex) =>
		ex.StatusCode is { } status
			? text.Format("The rates service answered {0}.", $"{(int)status} ({status})")
			: text["The rates service could not be reached."];
}
