using System.Globalization;

namespace Beep.Website.Currency;

/// <summary>
/// Reads currency rates from Beep.Api's CurrencyRate endpoints. Calls are made from the server, so the
/// API key (<c>BeepApi:ApiKey</c>) never reaches the browser. Country and currency names are given in the current
/// UI culture's language (<see cref="CountryNames"/>, <see cref="CurrencyNames"/>).
/// </summary>
public sealed class BeepApiClient(HttpClient http)
{
	public const string ApiKeyHeader = "X-API-Key";

	/// <summary>The countries with rates, in the order of their names.</summary>
	public async Task<IReadOnlyList<CountryItem>> GetCountriesAsync(CancellationToken cancellationToken) =>
		[.. (await http.GetFromJsonAsync<List<CountryItem>>("api/CurrencyRate/countries", cancellationToken) ?? [])
			.Select(c => c with { Name = CountryNames.Name(c.Code, c.Name) ?? c.Name })
			.OrderBy(c => c.Name, CountryNames.Order)];

	/// <summary>The banks with rates, of a country and matching <paramref name="bank"/> (a BIC or part of a name) when given.</summary>
	public async Task<IReadOnlyList<BankItem>> GetBanksAsync(string? country, CancellationToken cancellationToken, string? bank = null) =>
		[.. (await http.GetFromJsonAsync<List<BankItem>>(
			Url("api/CurrencyRate/banks", ("country", country), ("bank", bank)), cancellationToken) ?? [])
			.Select(b => b with { CountryName = CountryNames.Name(b.CountryCode, b.CountryName) })];

	public async Task<IReadOnlyList<LatestRateItem>> GetLatestRatesAsync(
		DateOnly? date, string? country, string? bank, CancellationToken cancellationToken) =>
		[.. (await http.GetFromJsonAsync<List<LatestRateItem>>(
			Url("api/CurrencyRate/latest",
				("date", date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
				("country", country),
				("bank", bank)),
			cancellationToken) ?? [])
			.Select(r => r with
			{
				CountryName = CountryNames.Name(r.CountryCode, r.CountryName),
				CurrencyName = CurrencyNames.Name(r.CurrencyCode, r.CurrencyName),
			})];

	// ---------------------------------------------------------------- rate alerts (api/Notification)

	/// <summary>
	/// Subscribes <paramref name="email"/> to rate alerts of <paramref name="banks"/> for
	/// <paramref name="currencies"/>. Null errors: saved ("confirmationSent" or "updated").
	/// </summary>
	public async Task<(string? Status, IReadOnlyDictionary<string, string>? Errors)> SubscribeAsync(
		string? email, string? country, IReadOnlyList<string> banks, IReadOnlyList<string> currencies, string culture,
		CancellationToken cancellationToken)
	{
		using var response = await http.PostAsJsonAsync("api/Notification/subscribe", new
		{
			email,
			website = "Currency",
			notificationType = "BankRates",
			filter = new { country, banks, currencies },
			culture,
		}, cancellationToken);

		if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
		{
			var body = await response.Content.ReadFromJsonAsync<SubscribeErrorsResponse>(cancellationToken);
			return (null, body?.Errors ?? new Dictionary<string, string>());
		}
		response.EnsureSuccessStatusCode();
		var ok = await response.Content.ReadFromJsonAsync<SubscribeStatusResponse>(cancellationToken);
		return (ok?.Status, null);
	}

	/// <summary>Confirms the subscription of a confirmation link; false when the link is unknown.</summary>
	public Task<bool> ConfirmSubscriptionAsync(string token, CancellationToken cancellationToken) =>
		PostTokenAsync("api/Notification/confirm", token, cancellationToken);

	/// <summary>Unsubscribes the subscription of an unsubscribe link; false when the link is unknown.</summary>
	public Task<bool> UnsubscribeAsync(string token, CancellationToken cancellationToken) =>
		PostTokenAsync("api/Notification/unsubscribe", token, cancellationToken);

	private async Task<bool> PostTokenAsync(string path, string token, CancellationToken cancellationToken)
	{
		using var response = await http.PostAsJsonAsync(path, new { token }, cancellationToken);
		if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
			return false;
		response.EnsureSuccessStatusCode();
		return true;
	}

	private static string Url(string path, params (string Name, string? Value)[] query)
	{
		var parts = query
			.Where(q => !string.IsNullOrWhiteSpace(q.Value))
			.Select(q => $"{q.Name}={Uri.EscapeDataString(q.Value!.Trim())}")
			.ToList();
		return parts.Count == 0 ? path : path + "?" + string.Join("&", parts);
	}
}
