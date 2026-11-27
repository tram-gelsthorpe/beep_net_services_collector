using System.Globalization;
using Beep.Api.Repositories.Country;
using Beep.Api.Repositories.Currency;
using Beep.Notification;

namespace Beep.Api.Notifications;

/// <summary>
/// After a bank's new rates are recorded: emails every confirmed subscriber who chose that bank, with the
/// latest rates of the currencies they chose.
/// </summary>
public sealed class BankRatesAlerts(
	ISubscriptionRepository subscriptions,
	ICurrencyRateQueries queries,
	ICountryRepository countries,
	INotificationEmailSender sender,
	NotificationOptions options,
	ILogger<BankRatesAlerts> logger)
{
	private static EmailText Text => BankRatesNotification.Text;

	public async Task SendAsync(string bankBic, CancellationToken cancellationToken)
	{
		var website = options.FindWebsite(BankRatesNotification.WebsiteName);
		if (website is null)
			return;

		var matching = (await subscriptions.GetActiveAsync(website.Name, BankRatesNotification.TypeName, cancellationToken))
			.Select(s => (Subscription: s, Filter: BankRatesFilter.Parse(s.Filter)))
			.Where(s => s.Filter is not null && s.Filter.Banks.Contains(bankBic, StringComparer.OrdinalIgnoreCase))
			.ToList();
		if (matching.Count == 0)
			return;

		var rates = await queries.GetLatestRatesAsync(null, null, bankBic, cancellationToken);
		if (rates.Count == 0)
			return;
		var all = await countries.GetCountriesAsync(cancellationToken);
		var country = all.GetValueOrDefault(rates[0].CountryId);
		var quote = country?.CurrencyId is { } currencyId
			? (await queries.GetCurrencyCodesAsync([currencyId], cancellationToken)).GetValueOrDefault(currencyId)
			: null;

		var sent = new List<Guid>();
		foreach (var (subscription, filter) in matching)
		{
			var chosen = rates.Where(r => filter!.Currencies.Contains(r.CurrencyCode, StringComparer.OrdinalIgnoreCase))
				.OrderBy(r => r.CurrencyCode, StringComparer.Ordinal).ToList();
			if (chosen.Count == 0)
				continue;
			try
			{
				await sender.SendAsync(AlertEmail(subscription, website, chosen, quote ?? string.Empty), cancellationToken);
				sent.Add(subscription.Id);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				logger.LogError(ex, "Rate alert of subscription {SubscriptionId} not sent", subscription.Id);
			}
		}

		await subscriptions.MarkSentAsync(sent, cancellationToken);
		logger.LogInformation("Sent {Sent} {BankBic} rate alert(s) of {Matching} subscription(s)", sent.Count, bankBic, matching.Count);
	}

	/// <summary>The alert: the bank's latest rates of the chosen currencies, and links to the site and to unsubscribe.</summary>
	public static NotificationEmail AlertEmail(Subscription subscription, NotificationWebsite website, IReadOnlyList<LatestBankRate> rates, string quote)
	{
		var language = EmailText.Language(subscription.Culture);
		var bank = rates[0].BankShortName ?? BankRatesNotification.ShortName(rates[0].BankName);
		var date = rates.Max(r => r.Date).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
		var published = rates.Select(r => r.RateTime).Max() is { } t ? t.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture) : "—";

		// The currency column on the reading side (left, or right in Arabic), the rates on the other.
		var (start, end) = EmailText.IsRightToLeft(language) ? ("right", "left") : ("left", "right");
		string Cell(string value, bool head = false, string? align = null, string colour = "#101828") =>
			$"<{(head ? "th" : "td")} style=\"padding:10px 8px;border-bottom:1px solid #EAECF0;text-align:{align ?? end};font-size:{(head ? 12 : 14)}px;color:{(head ? "#667085" : colour)};font-weight:{(head ? 600 : 500)};white-space:nowrap\">{EmailLayout.Encode(value)}</{(head ? "th" : "td")}>";
		// Two decimals, up to five significant digits on a small rate (0.91266 EUR per USD), as on the website.
		static string Rate(decimal? rate) => rate is { } value
			? value.ToString("#,##0.00" + new string('#', value == 0 ? 0 : Math.Clamp(2 - (int)Math.Floor(Math.Log10((double)Math.Abs(value))), 0, 8)), CultureInfo.InvariantCulture)
			: "—";

		var rows = string.Concat(rates.Select(r =>
			"<tr>" + Cell(r.CurrencyCode, align: start) + Cell(Rate(r.BuyingRateCash)) + Cell(Rate(r.BuyingRateTransfer)) + Cell(Rate(r.SellingRate), colour: "#1D4ED8") + "</tr>"));
		var content = $"""
			<h1 style="margin:0 0 6px;font-size:22px">{EmailLayout.Encode(Text.Format("{0} published new rates", language, bank))}</h1>
			<p style="margin:0 0 20px;font-size:13px;color:#667085">{EmailLayout.Encode(Text.Format("Rates of {0} · published {1} · all values in {2}", language, date, published, quote))}</p>
			<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;margin:0 0 24px">
			<tr>{Cell(Text.Get("Currency", language), true, start)}{Cell(Text.Get("Buy · cash", language), true)}{Cell(Text.Get("Buy · transfer", language), true)}{Cell(Text.Get("Sell", language), true)}</tr>
			{rows}
			</table>
			<p style="margin:0">{EmailLayout.Button($"{website.Url}/?bank={Uri.EscapeDataString(rates[0].Bic)}", Text.Get("See all rates", language))}</p>
			""";

		return new NotificationEmail(
			subscription.Email,
			Text.Format("{0}: new rates", language, bank),
			EmailLayout.Wrap(language, website.DisplayName, content, website.UnsubscribeUrl(subscription.Token)));
	}
}
