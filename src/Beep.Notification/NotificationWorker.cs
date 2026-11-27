using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Beep.Notification;

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
