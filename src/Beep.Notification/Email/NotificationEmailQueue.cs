using Beep.ServiceBus;
using Microsoft.Extensions.Logging;

namespace Beep.Notification;

public sealed class NotificationEmailQueue(
	INotificationEmailRepository emails,
	IWorkItemRepository workItems,
	NotificationEmailOptions options,
	ILogger<NotificationEmailQueue> logger) : INotificationEmailQueue
{
	/// <summary>ServiceBus DomainType of email work items (DomainId = <c>dbo.User_Notification_Email.Id</c>).</summary>
	public const string DomainType = "NotificationEmail";

	/// <summary>Work items per ServiceBus round trip.</summary>
	private const int BatchSize = 1000;

	public async Task<int> EnqueueAsync(
		IReadOnlyCollection<Guid> subscriptionIds, NotificationEmailKind kind, string? data, CancellationToken cancellationToken)
	{
		if (subscriptionIds.Count == 0)
			return 0;

		var ids = await emails.AddAsync(subscriptionIds, kind, data, cancellationToken);
		var queued = 0;
		foreach (var batch in ids.Chunk(BatchSize))
			queued += await workItems.EnqueueBatchAsync(
				options.WorkerType, DomainType, [.. batch.Select(id => new WorkItemRequest(id, options.MaxAttempts))], cancellationToken);

		logger.LogInformation("Queued {Queued} {Kind} email(s) of {Subscriptions} subscription(s)", queued, kind, subscriptionIds.Count);
		return queued;
	}
}
