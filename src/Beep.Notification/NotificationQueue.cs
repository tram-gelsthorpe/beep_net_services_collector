using System.Threading.Channels;

namespace Beep.Notification;

public sealed class NotificationQueue : INotificationQueue
{
	private readonly Channel<(string Description, Func<IServiceProvider, CancellationToken, Task> Work)> _channel =
		Channel.CreateUnbounded<(string, Func<IServiceProvider, CancellationToken, Task>)>(new UnboundedChannelOptions { SingleReader = true });

	public void Enqueue(string description, Func<IServiceProvider, CancellationToken, Task> work)
	{
		ArgumentNullException.ThrowIfNull(work);
		_channel.Writer.TryWrite((description, work));
	}

	internal ChannelReader<(string Description, Func<IServiceProvider, CancellationToken, Task> Work)> Reader => _channel.Reader;
}
