using Beep.EventBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WebsiteCollector.Worker.Worker;

/// <summary>
/// Webhook delivery background worker for WebsiteCollector.Worker.
/// Inherits from the generic Beep.WebhookBus WebhookWorkerBase for all delivery processing.
/// No project-specific logic - pure generic webhook delivery handling.
/// </summary>
public sealed class EventBusWorker : EventBusWorkerBase
{
	public EventBusWorker(
		IEventBusRepository repository,
		IEventBusService deliveryService,
		IOptions<EventBusOptions> options,
		ILoggerFactory loggerFactory,
		ILogger<EventBusWorker> logger)
		: base(
			new EventBusLeaseManager(repository),
			new EventBusWorkProcessor(deliveryService, loggerFactory.CreateLogger<EventBusWorkProcessor>()),
			options.Value,
			logger)
	{
	}
}
