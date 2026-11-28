namespace Beep.Notification;

/// <summary>A row of <c>dbo.User_Notification_Email</c> and the subscription it is for.</summary>
/// <param name="Id"><c>dbo.User_Notification_Email.Id</c>, the work item's DomainId</param>
/// <param name="Kind">Confirmation or alert</param>
/// <param name="Data">What the email was queued with, JSON (e.g. <c>{"bank":"BFTVVNVX"}</c>); null for a confirmation</param>
/// <param name="SentAt">When it was sent; null until then</param>
/// <param name="Subscription">The subscription and its subscriber, as they are now</param>
public sealed record QueuedNotificationEmail(
	Guid Id,
	NotificationEmailKind Kind,
	string? Data,
	DateTimeOffset? SentAt,
	Subscription Subscription);
