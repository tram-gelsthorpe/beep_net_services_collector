using System.Text.Json;
using Beep.Website.Currency.Localization;
using Beep.Website.Currency.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beep.Website.Currency.Pages;

/// <summary>
/// Converter: converts between a foreign currency and the bank's currency (VND) at one bank's latest rates.
/// Foreign → VND uses the bank's buy rate (transfer or cash); VND → foreign uses its sell rate. The page
/// sends the bank's rates once; the conversion itself runs in the browser (wwwroot/js/converter.js).
/// The Country and Bank pickers choose the bank: <c>/Converter?country=VN&amp;bank=BFTVVNVX&amp;currency=EUR</c>.
/// </summary>
public sealed class ConverterModel(BeepApiClient api, UiText text, ILogger<ConverterModel> logger) : PageModel
{
	private static readonly string[] PreferredCurrencies = ["EUR", "USD"];

	/// <summary>The bank's country code; its banks fill the Bank picker.</summary>
	[BindProperty(SupportsGet = true)]
	public string? Country { get; set; }

	/// <summary>The bank's BIC.</summary>
	[BindProperty(SupportsGet = true)]
	public string? Bank { get; set; }

	[BindProperty(SupportsGet = true)]
	public string? Currency { get; set; }

	public IReadOnlyList<CountryItem> Countries { get; private set; } = [];
	public IReadOnlyList<BankItem> Banks { get; private set; } = [];
	public BankRates? Rates { get; private set; }
	public string? Error { get; private set; }

	public async Task OnGetAsync(CancellationToken cancellationToken)
	{
		try
		{
			Countries = await api.GetCountriesAsync(cancellationToken);
			var country = Countries.FirstOrDefault(c => string.Equals(c.Code, Country, StringComparison.OrdinalIgnoreCase))
				?? Countries.FirstOrDefault();
			Country = country?.Code;

			// A bank of another country (after the country changed) falls back to the country's first bank.
			Banks = await api.GetBanksAsync(Country, cancellationToken);
			var bank = Banks.FirstOrDefault(b => string.Equals(b.Bic, Bank, StringComparison.OrdinalIgnoreCase))
				?? Banks.FirstOrDefault();
			if (bank is null)
				return;
			Bank = bank.Bic;

			Rates = BankRates.FromRates(await api.GetLatestRatesAsync(null, null, bank.Bic, cancellationToken))
				.FirstOrDefault(b => string.Equals(b.Bic, bank.Bic, StringComparison.OrdinalIgnoreCase));
			if (Rates is null)
				return;

			Currency = Rates.Rate(Currency ?? "")?.CurrencyCode
				?? PreferredCurrencies.Select(Rates.Rate).OfType<LatestRateItem>().FirstOrDefault()?.CurrencyCode
				?? Rates.Rates[0].CurrencyCode;
		}
		catch (HttpRequestException ex)
		{
			logger.LogError(ex, "Beep.Api could not be reached or refused the request");
			Error = IndexModel.ApiError(text, ex);
		}
	}

	/// <summary>What converter.js needs: the bank's rates and the texts it writes.</summary>
	public string ConverterJson() => JsonSerializer.Serialize(new
	{
		currency = Currency,
		quote = Rates?.QuoteCurrency ?? "",
		compareUrl = Url.Page("/Compare", new { country = Rates?.CountryCode, currency = "__CUR__" }),
		rates = Rates?.Rates.ToDictionary(
			r => r.CurrencyCode,
			r => new { name = r.CurrencyName, cash = r.BuyingRateCash, transfer = r.BuyingRateTransfer, sell = r.SellingRate }),
		text = new
		{
			rateUsed = text["Rate used: {0}"],
			buysTransfer = text["bank buys (transfer)"],
			buysCash = text["bank buys (cash)"],
			sells = text["bank sells"],
			compare = text["Compare {0} at other banks →"],
			sell = text["sell {0}"],
		},
	});
}
