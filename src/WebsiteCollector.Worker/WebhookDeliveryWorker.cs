using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using WorkerFramework.Workers;
using WebsiteCollector.Worker.Infrastructure.Configuration;
using WebsiteCollector.Worker.Features.WebsiteCollector.Repositories;
using WebsiteCollector.Worker.Features.WebsiteCollector.Models;
using WebsiteCollector.Worker.Features.WebsiteCollector.Services;
using WebsiteCollector.Worker.Features.WebsiteCollector.Adapters;

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
