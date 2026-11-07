using Beep.ServiceBus.Workers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebsiteCollector.Worker.Features.WebhookDelivery.Adapters;
using WebsiteCollector.Worker.Features.WebhookDelivery.Models;
using WebsiteCollector.Worker.Features.WebhookDelivery.Repositories;
using WebsiteCollector.Worker.Features.WebhookDelivery.Services;
using WebsiteCollector.Worker.Infrastructure.Configuration;

namespace WebsiteCollector.Worker;

/// <summary>
/// Background hosted service for asynchronous webhook delivery processing.
/// Uses the generic worker framework with WebhookDeliveryOptions configuration.
/// Handles polling, concurrency, and lease management consistently with other workers.
/// </summary>
public sealed class WebhookDeliveryWorker : GenericWorkerBase<PendingWebhookDelivery>
{
	private readonly WebhookDeliveryOptions _options;

	public WebhookDeliveryWorker(
		IWebhookDeliveryRepository deliveryRepository,
		IWebhookDeliveryService deliveryService,
		IOptions<WebhookDeliveryOptions> options,
		ILogger<WebhookDeliveryWorker> logger)
		: base(
			new DeliveryLeaseManager(deliveryRepository),
			new DeliveryWorkProcessor(deliveryService, logger),
			options.Value,
			logger)
	{
		_options = options?.Value ?? throw new ArgumentNullException(nameof(options));
		_options.Validate();
	}

	protected override int PollIntervalSeconds => _options.PollIntervalSeconds;
	protected override int MaxConcurrentItems => _options.MaxConcurrentItems;
	protected override int LeaseSeconds => _options.LeaseSeconds;
}
