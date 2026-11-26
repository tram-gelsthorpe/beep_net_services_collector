using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

/// <summary>Runs queued work one item at a time; a failure is logged and the next item runs.</summary>
public sealed class NotificationWorker(NotificationQueue queue, IServiceScopeFactory scopes, ILogger<NotificationWorker> logger)
	: BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		await foreach (var (description, work) in queue.Reader.ReadAllAsync(stoppingToken))
		{
			try
			{
				await using var scope = scopes.CreateAsyncScope();
				await work(scope.ServiceProvider, stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Notification work failed: {Description}", description);
			}
		}
	}
}
