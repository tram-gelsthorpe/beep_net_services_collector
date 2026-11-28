using Beep.ServiceBus;
using Microsoft.Extensions.Logging;

namespace Beep.Notification;

/// <summary>
/// Sends queued emails. Claims work items of <see cref="NotificationEmailOptions.WorkerType"/> through the
/// ServiceBus, so any number of instances can run without sending an email twice.
/// </summary>
public sealed class NotificationEmailWorker : GenericWorkerBase<IClaimedWorkItem>
{
	public NotificationEmailWorker(
		IWorkItemRepository workItems,
		NotificationEmailWorkProcessor workProcessor,
		NotificationEmailOptions options,
		ILogger<NotificationEmailWorker> logger)
		: base(new WorkItemLeaseManager(workItems, options.WorkerType), workProcessor, options, logger)
	{
		options.Validate();
	}
}
