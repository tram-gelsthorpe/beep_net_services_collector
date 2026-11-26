using Beep.Website.Currency.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beep.Website.Currency.Pages.Alerts;

/// <summary>
/// The unsubscribe link of every alert: <c>/alerts/unsubscribe?token=…</c> asks, and the button unsubscribes
/// (a POST, so link checkers in mail clients cannot unsubscribe by opening the link).
/// </summary>
public sealed class UnsubscribeModel(BeepApiClient api, ILogger<UnsubscribeModel> logger) : PageModel
{
	[BindProperty(SupportsGet = true)]
	public string? Token { get; set; }

	/// <summary>Null until the button is pressed.</summary>
	public AlertOutcome? Outcome { get; private set; }

	public void OnGet()
	{
		if (string.IsNullOrWhiteSpace(Token))
			Outcome = AlertOutcome.UnknownLink;
	}

	public async Task OnPostAsync(CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(Token))
		{
			Outcome = AlertOutcome.UnknownLink;
			return;
		}
		try
		{
			Outcome = await api.UnsubscribeAsync(Token, cancellationToken) ? AlertOutcome.Done : AlertOutcome.UnknownLink;
		}
		catch (HttpRequestException ex)
		{
			logger.LogError(ex, "Beep.Api could not unsubscribe");
			Outcome = AlertOutcome.Error;
		}
	}
}
