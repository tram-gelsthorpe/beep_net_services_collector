using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beep.Website.Currency;

/// <summary>The confirmation email's link: <c>/alerts/confirm?token=…</c> confirms the subscription.</summary>
public sealed class ConfirmModel(BeepApiClient api, ILogger<ConfirmModel> logger) : PageModel
{
	[BindProperty(SupportsGet = true)]
	public string? Token { get; set; }

	/// <summary>Confirmed, unknown link, or the API could not be reached.</summary>
	public AlertOutcome Outcome { get; private set; }

	public async Task OnGetAsync(CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(Token))
		{
			Outcome = AlertOutcome.UnknownLink;
			return;
		}
		try
		{
			Outcome = await api.ConfirmSubscriptionAsync(Token, cancellationToken) ? AlertOutcome.Done : AlertOutcome.UnknownLink;
		}
		catch (HttpRequestException ex)
		{
			logger.LogError(ex, "Beep.Api could not confirm a subscription");
			Outcome = AlertOutcome.Error;
		}
	}
}
