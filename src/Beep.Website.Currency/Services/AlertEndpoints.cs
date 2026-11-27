using System.Globalization;
using Microsoft.AspNetCore.Antiforgery;

namespace Beep.Website.Currency;

/// <summary>
/// What the Subscribe dialog (wwwroot/js/subscribe.js) calls: the choices it offers, and subscribing. Both go
/// through Beep.Api from the server, so the API key stays here.
/// </summary>
public static class AlertEndpoints
{
	public const string AntiforgeryHeader = "RequestVerificationToken";

	public static IEndpointRouteBuilder MapAlerts(this IEndpointRouteBuilder app)
	{
		// Countries, and the banks and currencies of one country (default: the first).
		app.MapGet("/alerts/options", async (string? country, BeepApiClient api, CancellationToken cancellationToken) =>
		{
			var countries = await api.GetCountriesAsync(cancellationToken);
			var chosen = countries.FirstOrDefault(c => string.Equals(c.Code, country, StringComparison.OrdinalIgnoreCase))
				?? countries.FirstOrDefault();
			if (chosen is null)
				return Results.Json(new { countries, country = (string?)null, banks = Array.Empty<object>(), currencies = Array.Empty<object>() });

			var rates = await api.GetLatestRatesAsync(null, chosen.Code, null, cancellationToken);
			var banks = rates.GroupBy(r => r.Bic)
				.Select(g => new { bic = g.Key, name = g.First().BankShortName ?? g.First().BankName })
				.OrderBy(b => b.name, StringComparer.CurrentCultureIgnoreCase)
				.ToList();
			var currencies = rates.GroupBy(r => r.CurrencyCode)
				.Select(g => new { code = g.Key, name = g.First().CurrencyName })
				.OrderBy(c => c.code, StringComparer.Ordinal)
				.ToList();
			return Results.Json(new { countries, country = chosen.Code, banks, currencies });
		});

		// Subscribe: { email, country, banks[], currencies[] }. 200 { status } or 400 { errors: [field, …] }.
		app.MapPost("/alerts/subscribe", async (SubscribeBody body, HttpContext context, IAntiforgery antiforgery,
			BeepApiClient api, ILogger<SubscribeBody> logger, CancellationToken cancellationToken) =>
		{
			if (!await antiforgery.IsRequestValidAsync(context))
				return Results.BadRequest(new { errors = new[] { "form" } });

			try
			{
				var (status, errors) = await api.SubscribeAsync(
					body.Email, body.Country, body.Banks ?? [], body.Currencies ?? [],
					CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, cancellationToken);
				return errors is null
					? Results.Ok(new { status })
					: Results.BadRequest(new { errors = errors.Keys.Select(k => k.ToLowerInvariant()).ToList() });
			}
			catch (HttpRequestException ex)
			{
				logger.LogError(ex, "Beep.Api could not save a subscription");
				return Results.Json(new { errors = new[] { "server" } }, statusCode: StatusCodes.Status502BadGateway);
			}
		});

		return app;
	}
}
