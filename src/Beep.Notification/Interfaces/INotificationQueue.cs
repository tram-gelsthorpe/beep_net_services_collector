namespace Beep.Notification;

/// <summary>
/// Work done after the request that asked for it has been answered: sending emails, so a subscriber or the
/// EventBus never waits for SMTP.
/// </summary>
public interface INotificationQueue
{
	/// <summary>Queues <paramref name="work"/>; it runs with its own service scope.</summary>
	void Enqueue(string description, Func<IServiceProvider, CancellationToken, Task> work);
}
