using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebsiteCollector.Features.WebsiteCollection.Models;
using WebsiteCollector.Features.WebsiteCollection.Repositories;
using WebsiteCollector.Features.WebsiteCollection.Services;

namespace WebsiteCollector.Features.WebsiteCollection;

/// <summary>
/// Background hosted service for asynchronous webhook delivery processing.
/// Polls the delivery queue, claims pending deliveries, and processes them with retry logic.
/// Option B: Separate async worker for webhook delivery.
/// </summary>
public sealed class WebhookDeliveryWorker(
	IWebhookDeliveryRepository deliveryRepository,
	IWebhookDeliveryService deliveryService,
	ILogger<WebhookDeliveryWorker> logger) : BackgroundService
{
	private readonly int _pollIntervalSeconds = 5;
	private readonly int _maxDeliveriesPerBatch = 10;

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		logger.LogInformation("WebhookDeliveryWorker started");

		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				// Claim pending deliveries
				var deliveries = await deliveryRepository.ClaimPendingDeliveriesAsync(_maxDeliveriesPerBatch, stoppingToken);

				if (deliveries.Count > 0)
				{
					logger.LogInformation("Claimed {Count} webhook deliveries for processing", deliveries.Count);

					// Process each delivery
					var tasks = deliveries.Select(delivery => ProcessDeliveryAsync(delivery, stoppingToken));
					await Task.WhenAll(tasks);
				}

				// Wait before next poll
				await Task.Delay(TimeSpan.FromSeconds(_pollIntervalSeconds), stoppingToken);
			}
			catch (OperationCanceledException)
			{
				logger.LogInformation("WebhookDeliveryWorker gracefully shutting down");
				break;
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error in webhook delivery poll loop");
				await Task.Delay(TimeSpan.FromSeconds(_pollIntervalSeconds), stoppingToken);
			}
		}

		logger.LogInformation("WebhookDeliveryWorker stopped");
	}

	private async Task ProcessDeliveryAsync(PendingWebhookDelivery delivery, CancellationToken ct)
	{
		try
		{
			logger.LogInformation("Processing webhook delivery {DeliveryId}, attempt {Attempt}", delivery.DeliveryId, delivery.AttemptNumber);
			await deliveryService.DeliverAsync(delivery, ct);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error processing webhook delivery {DeliveryId}", delivery.DeliveryId);
		}
	}
}
