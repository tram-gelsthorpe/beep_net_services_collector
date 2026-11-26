using System.Globalization;
using System.Net.Http.Json;

namespace Beep.Website.Currency.Services;

public sealed record CountryItem(string Code, string Name);

public sealed record BankItem(string Bic, string Name, string? CountryCode, string? CountryName, DateOnly LatestDate);

public sealed record LatestRateItem(
	string Bic,
	string BankName,
	string? CountryCode,
	string? CountryName,
	string CurrencyCode,
	string CurrencyName,
	DateOnly Date,
	DateTimeOffset? RateTime,
	decimal? BuyingRateCash,
	decimal? BuyingRateTransfer,
	decimal? SellingRate,
	string? CurrencySymbol,
	string? QuoteCurrencyCode);

/// <summary>
/// Reads currency rates from Beep.Api's CurrencyRate endpoints. Calls are made from the server, so the
/// API key (<c>BeepApi:ApiKey</c>) never reaches the browser.
/// </summary>
public sealed class BeepApiClient(HttpClient http)
{
	public const string ApiKeyHeader = "X-API-Key";

	public async Task<IReadOnlyList<CountryItem>> GetCountriesAsync(CancellationToken cancellationToken) =>
		await http.GetFromJsonAsync<List<CountryItem>>("api/CurrencyRate/countries", cancellationToken) ?? [];

	public async Task<IReadOnlyList<BankItem>> GetBanksAsync(string? country, CancellationToken cancellationToken) =>
		await http.GetFromJsonAsync<List<BankItem>>(Url("api/CurrencyRate/banks", ("country", country)), cancellationToken) ?? [];

	public async Task<IReadOnlyList<LatestRateItem>> GetLatestRatesAsync(
		DateOnly? date, string? country, string? bank, CancellationToken cancellationToken) =>
		await http.GetFromJsonAsync<List<LatestRateItem>>(
			Url("api/CurrencyRate/latest",
				("date", date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
				("country", country),
				("bank", bank)),
			cancellationToken) ?? [];

	private static string Url(string path, params (string Name, string? Value)[] query)
	{
		var parts = query
			.Where(q => !string.IsNullOrWhiteSpace(q.Value))
			.Select(q => $"{q.Name}={Uri.EscapeDataString(q.Value!.Trim())}")
			.ToList();
		return parts.Count == 0 ? path : path + "?" + string.Join("&", parts);
	}
}
