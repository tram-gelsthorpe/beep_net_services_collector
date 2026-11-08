using Beep.Webhook.Infrastructure.Configuration;
using Beep.Webhook.Repository;
using Beep.Webhook.Services;
using Beep.Webhook.Workers;
using Beep.Webhook.Workers.Adapters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WebsiteCollector.Worker;

/// <summary>
/// Webhook delivery background worker for WebsiteCollector.Worker.
/// Inherits from the generic Beep.WebhookBus WebhookWorkerBase for all delivery processing.
/// No project-specific logic - pure generic webhook delivery handling.
/// </summary>
public sealed class WebhookDeliveryWorker : WebhookWorkerBase
{
	public WebhookDeliveryWorker(
		IWebhookRepository repository,
		IWebhookService deliveryService,
		IOptions<WebhookOptions> options,
		ILoggerFactory loggerFactory,
		ILogger<WebhookDeliveryWorker> logger)
		: base(
			new WebhookLeaseManager(repository),
			new WebhookWorkProcessor(deliveryService, loggerFactory.CreateLogger<WebhookWorkProcessor>()),
			options.Value,
			logger)
	{
	}
}
