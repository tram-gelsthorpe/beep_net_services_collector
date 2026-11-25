using Beep.ServiceBus;
using Microsoft.Extensions.Logging;

namespace Beep.EventBus;

/// <summary>
/// Background worker that delivers queued EventBus notifications.
/// </summary>
public sealed class EventBusWorker : GenericWorkerBase<PendingEventBus>
{
	public EventBusWorker(
		EventBusLeaseManager leaseManager,
		EventBusWorkProcessor workProcessor,
		EventBusOptions options,
		ILogger<EventBusWorker> logger) : base(leaseManager, workProcessor, options, logger)
	{
		options.Validate();
	}
}
