namespace Beep.Notification;

/// <summary>Sends one email now.</summary>
public interface INotificationEmailSender
{
	Task SendAsync(NotificationEmail email, CancellationToken cancellationToken);
}
