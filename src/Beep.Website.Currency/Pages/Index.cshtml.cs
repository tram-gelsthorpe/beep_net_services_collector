using System.Globalization;
using Beep.Website.Currency.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beep.Website.Currency.Pages;

/// <summary>
/// Pick a country, a date and/or a bank (name or BIC) and see each bank's latest rate per currency on or
/// before that date. The filters are in the query string, so a search can be bookmarked or shared.
/// </summary>
public sealed class IndexModel(BeepApiClient api, ILogger<IndexModel> logger) : PageModel
{
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
				.OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase)
				.ToList();
		}
		catch (HttpRequestException ex)
		{
			logger.LogError(ex, "Beep.Api could not be reached or refused the request");
			Error = ex.StatusCode is { } status
				? $"The rates service answered {(int)status} ({status})."
				: "The rates service could not be reached.";
		}
	}

	public static string Format(decimal? rate) =>
		rate is { } value ? value.ToString("#,##0.00##", CultureInfo.InvariantCulture) : "–";

	public sealed record BankRates(string Name, string Bic, string? CountryName, IReadOnlyList<LatestRateItem> Rates)
	{
		public DateOnly FirstDate => Rates.Min(r => r.Date);
		public DateOnly LastDate => Rates.Max(r => r.Date);
		public bool SameDate => FirstDate == LastDate;

		/// <summary>When the rates were published, if they all share one time.</summary>
		public DateTimeOffset? PublishedAt =>
			Rates.Select(r => r.RateTime).Distinct().Count() == 1 ? Rates[0].RateTime : null;
	}
}
