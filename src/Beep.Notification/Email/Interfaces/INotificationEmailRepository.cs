namespace Beep.Notification;

/// <summary>Queued emails (<c>dbo.User_Notification_Email</c>) in A_1_Beep_Global_Default.</summary>
public interface INotificationEmailRepository
{
	/// <summary>Adds one email of <paramref name="kind"/> per subscription; returns the new rows' ids.</summary>
	Task<IReadOnlyList<Guid>> AddAsync(
		IReadOnlyCollection<Guid> subscriptionIds, NotificationEmailKind kind, string? data, CancellationToken cancellationToken);

	/// <summary>The email and its subscription. Null when the email does not exist.</summary>
	Task<QueuedNotificationEmail?> GetAsync(Guid id, CancellationToken cancellationToken);

	/// <summary>Records that the email was sent: its SentAt, and the subscription's LastSentAt.</summary>
	Task MarkSentAsync(Guid id, CancellationToken cancellationToken);
}
