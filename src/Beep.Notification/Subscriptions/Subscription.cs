namespace Beep.Notification;

/// <summary>A subscription and its subscriber.</summary>
/// <param name="Id"><c>dbo.User_Notification.Id</c></param>
/// <param name="UserId"><c>dbo.User.Id</c></param>
/// <param name="Login">The user's 9-digit login</param>
/// <param name="Email">The user's email address</param>
/// <param name="Culture">The user's language (<c>CultureInfoUI</c>), e.g. vi</param>
/// <param name="Website">e.g. Currency</param>
/// <param name="NotificationType">e.g. BankRates</param>
/// <param name="Filter">The website's choices, JSON</param>
/// <param name="IsConfirmed">The email address was confirmed</param>
/// <param name="Token">Secret of the confirmation and unsubscribe links</param>
/// <param name="IsDeleted">Unsubscribed</param>
/// <param name="LastSentAt">When the last email (confirmation or alert) was sent</param>
public sealed record Subscription(
	Guid Id,
	Guid UserId,
	string Login,
	string Email,
	string? Culture,
	string Website,
	string NotificationType,
	string? Filter,
	bool IsConfirmed,
	string Token,
	bool IsDeleted,
	DateTimeOffset? LastSentAt);
