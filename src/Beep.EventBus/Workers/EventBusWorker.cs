using Beep.ServiceBus;
using Microsoft.Extensions.Logging;

namespace Beep.EventBus;

/// <summary>
/// Delivers queued EventBus deliveries. Claims work items of <see cref="EventBusOptions.WorkerType"/>
/// through the ServiceBus, so any number of worker instances can run without posting a delivery twice.
/// </summary>
public sealed class EventBusWorker : GenericWorkerBase<IClaimedWorkItem>
{
	public EventBusWorker(
		IWorkItemRepository workItems,
		EventBusWorkProcessor workProcessor,
		EventBusOptions options,
		ILogger<EventBusWorker> logger)
		: base(new WorkItemLeaseManager(workItems, options.WorkerType), workProcessor, options, logger)
	{
		options.Validate();
	}
}
