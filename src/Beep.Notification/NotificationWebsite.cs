namespace Beep.Notification;

/// <param name="Name">Key used in <c>dbo.User_Notification.Website</c>, e.g. Currency</param>
/// <param name="DisplayName">Shown in emails, e.g. Beep Currency</param>
/// <param name="Url">The website's address, without a trailing slash; links in emails start with it</param>
public sealed record NotificationWebsite(string Name, string DisplayName, string Url)
{
	/// <summary>Confirmation link: the website's <c>/alerts/confirm</c> page.</summary>
	public string ConfirmUrl(string token) => $"{Url}/alerts/confirm?token={Uri.EscapeDataString(token)}";

	/// <summary>Unsubscribe link: the website's <c>/alerts/unsubscribe</c> page.</summary>
	public string UnsubscribeUrl(string token) => $"{Url}/alerts/unsubscribe?token={Uri.EscapeDataString(token)}";
}
