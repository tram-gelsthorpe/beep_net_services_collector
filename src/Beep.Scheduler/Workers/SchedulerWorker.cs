using Beep.ServiceBus;
using Microsoft.Extensions.Logging;

namespace Beep.Scheduler;

/// <summary>
/// Runs due schedules. Claims schedule work items (queued by <see cref="SchedulerService"/>) through
/// the ServiceBus, so with any number of instances each run happens once; a run whose worker dies is
/// reclaimed when its lease expires.
/// </summary>
public sealed class SchedulerWorker : GenericWorkerBase<IClaimedWorkItem>
{
	public SchedulerWorker(
		IWorkItemRepository workItems,
		SchedulerWorkProcessor processor,
		SchedulerWorkerOptions options,
		ILogger<SchedulerWorker> logger)
		: base(new WorkItemLeaseManager(workItems, options.WorkerType), processor, options, logger)
	{
		options.Validate();
	}
}
