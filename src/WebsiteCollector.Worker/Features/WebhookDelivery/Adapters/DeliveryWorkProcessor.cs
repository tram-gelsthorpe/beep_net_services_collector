using Microsoft.Extensions.Logging;
using WebsiteCollector.Worker.Features.WebhookDelivery.Models;
using WebsiteCollector.Worker.Features.WebhookDelivery.Services;
using WorkerFramework.Processing;

namespace WebsiteCollector.Worker.Features.WebhookDelivery.Adapters;

/// <summary>
/// Work processor implementation for webhook delivery
/// </summary>
internal sealed class DeliveryWorkProcessor : IWorkProcessor<PendingWebhookDelivery>
{
	private readonly IWebhookDeliveryService _deliveryService;
	private readonly ILogger _logger;

	public int HeartbeatIntervalSeconds { get; } = int.MaxValue; // No heartbeat for quick webhooks

	public DeliveryWorkProcessor(
		IWebhookDeliveryService deliveryService,
		ILogger logger)
	{
		_deliveryService = deliveryService ?? throw new ArgumentNullException(nameof(deliveryService));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public async Task InitializeAsync(PendingWebhookDelivery item, Guid leaseToken, Guid workerIdent, CancellationToken ct)
	{
		_logger.LogInformation("Processing webhook delivery {DeliveryId}, attempt {Attempt}", item.DeliveryId, item.AttemptNumber);
		await Task.CompletedTask;
	}

	public async Task<ProcessingResult> ProcessAsync(PendingWebhookDelivery item, Guid leaseToken, CancellationToken ct)
	{
		try
		{
			await _deliveryService.DeliverAsync(item, ct);
			return new ProcessingResult { Success = true };
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error delivering webhook {DeliveryId}", item.DeliveryId);
			return new ProcessingResult
			{
				Success = false,
				ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message
			};
		}
	}

	public async Task CompleteAsync(
		PendingWebhookDelivery item,
		Guid leaseToken,
		ProcessingResult result,
		CancellationToken ct)
	{
		if (result.Success)
		{
			_logger.LogInformation("Webhook delivery {DeliveryId} completed successfully", item.DeliveryId);
		}
		else
		{
			_logger.LogError("Webhook delivery {DeliveryId} failed: {Error}", item.DeliveryId, result.ErrorMessage);
		}

		await Task.CompletedTask;
	}
}
