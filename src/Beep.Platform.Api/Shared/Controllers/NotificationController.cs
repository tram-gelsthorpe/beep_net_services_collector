using Beep.Notification;
using Microsoft.AspNetCore.Mvc;

namespace Beep.Platform.Api.Shared.Controllers;

/// <summary>
/// Alert subscriptions of every website (Beep.Notification): subscribe, then confirm and unsubscribe with the
/// token of the links in the emails.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class NotificationController(SubscriptionService subscriptions) : ControllerBase
{
	/// <summary>
	/// Subscribe an email address to a website's alerts. A new address gets a <c>dbo.User</c> (9-digit login)
	/// and a confirmation email; an address already subscribed gets its choices replaced.
	/// </summary>
	/// <response code="200"><c>{ "status": "confirmationSent" }</c>, or <c>"updated"</c> for a confirmed address</response>
	/// <response code="400"><c>{ "errors": { field: message } }</c></response>
	[HttpPost("subscribe")]
	public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest request, CancellationToken cancellationToken)
	{
		var result = await subscriptions.SubscribeAsync(request, cancellationToken);
		return result.Status switch
		{
			SubscribeStatus.Invalid => BadRequest(new { errors = result.Errors }),
			SubscribeStatus.Updated => Ok(new { status = "updated" }),
			_ => Ok(new { status = "confirmationSent" }),
		};
	}

	/// <summary>Confirm the subscription of a confirmation link.</summary>
	/// <response code="200"><c>{ "website", "notificationType" }</c></response>
	/// <response code="404">Unknown token</response>
	[HttpPost("confirm")]
	public async Task<IActionResult> Confirm([FromBody] TokenRequest request, CancellationToken cancellationToken) =>
		await subscriptions.ConfirmAsync(request.Token, cancellationToken) is { } s
			? Ok(new { website = s.Website, notificationType = s.NotificationType })
			: NotFound(new { error = "Unknown or expired link" });

	/// <summary>Unsubscribe the subscription of an unsubscribe link.</summary>
	/// <response code="200"><c>{ "website", "notificationType" }</c></response>
	/// <response code="404">Unknown token</response>
	[HttpPost("unsubscribe")]
	public async Task<IActionResult> Unsubscribe([FromBody] TokenRequest request, CancellationToken cancellationToken) =>
		await subscriptions.UnsubscribeAsync(request.Token, cancellationToken) is { } s
			? Ok(new { website = s.Website, notificationType = s.NotificationType })
			: NotFound(new { error = "Unknown or expired link" });
}
