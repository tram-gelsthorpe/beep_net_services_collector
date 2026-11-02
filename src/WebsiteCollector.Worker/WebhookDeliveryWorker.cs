using System;
using Microsoft.Extensions.Logging;
using WebsiteCollector.Features.WebsiteCollection.Models;
using WebsiteCollector.Features.WebsiteCollection.Repositories;
using WebsiteCollector.Features.WebsiteCollection.Services;
using WorkerFramework.Workers;

namespace WebsiteCollector.Features.WebsiteCollection;

/// <summary>
/// Background hosted service for asynchronous webhook delivery processing.
/// Uses the generic worker framework to manage polling, concurrency, and lifecycle.
/// </summary>
public sealed class WebhookDeliveryWorker : GenericWorkerBase<PendingWebhookDelivery>
{
	private readonly int _pollIntervalSeconds = 5;
	private readonly int _maxDeliveriesPerBatch = 10;

	public WebhookDeliveryWorker(
		IWebhookDeliveryRepository deliveryRepository,
		IWebhookDeliveryService deliveryService,
		ILogger<WebhookDeliveryWorker> logger)
		: base(
			new DeliveryLeaseManager(deliveryRepository),
			new DeliveryWorkProcessor(deliveryService, logger),
			logger)
	{
	}

	protected override int PollIntervalSeconds => _pollIntervalSeconds;
	protected override int MaxConcurrentItems => _maxDeliveriesPerBatch;
	protected override int LeaseSeconds => 300; // 5 minutes, though webhooks should complete faster
}
