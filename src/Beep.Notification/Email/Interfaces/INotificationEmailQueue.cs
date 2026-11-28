namespace Beep.Notification;

/// <summary>
/// Queues emails: one <c>dbo.User_Notification_Email</c> row per subscription, each sent (and retried when
/// sending fails) by a ServiceBus work item.
/// </summary>
public interface INotificationEmailQueue
{
	/// <summary>Queues one email of <paramref name="kind"/> per subscription; returns how many were queued.</summary>
	/// <param name="subscriptionIds"><c>dbo.User_Notification.Id</c>s</param>
	/// <param name="kind">Confirmation or alert</param>
	/// <param name="data">What the email is about, JSON, given back to <see cref="INotificationType.AlertEmailAsync"/></param>
	/// <param name="cancellationToken">Cancellation token</param>
	Task<int> EnqueueAsync(
		IReadOnlyCollection<Guid> subscriptionIds, NotificationEmailKind kind, string? data, CancellationToken cancellationToken);
}
