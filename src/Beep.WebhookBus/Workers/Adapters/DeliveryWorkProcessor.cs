using System;
using System.Threading;
using System.Threading.Tasks;
using Beep.ServiceBus.Processing;
using Beep.WebhookBus.Domain;
using Beep.WebhookBus.Services;
using Microsoft.Extensions.Logging;

namespace Beep.WebhookBus.Workers.Adapters;

/// <summary>
/// Work processor adapter that delegates webhook delivery to WebhookDeliveryService.
/// Bridges between the generic worker framework and domain-specific delivery logic.
/// </summary>
public sealed class DeliveryWorkProcessor : IWorkProcessor<PendingWebhook>
{
	private readonly IWebhookDeliveryService _service;
	private readonly ILogger<DeliveryWorkProcessor> _logger;

	public int HeartbeatIntervalSeconds => 30; // 30 seconds (1/10 of typical 5-minute lease)

	public DeliveryWorkProcessor(
		IWebhookDeliveryService service,
		ILogger<DeliveryWorkProcessor> logger)
	{
		_service = service ?? throw new ArgumentNullException(nameof(service));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public async Task InitializeAsync(
		PendingWebhook item,
		Guid leaseToken,
		Guid workerIdent,
		CancellationToken ct)
	{
		// Initialization: log that processing is starting
		_logger.LogDebug(
			"Initializing webhook delivery. DeliveryId={DeliveryId}, Webhook={Webhook}, Worker={Worker}",
			item.DeliveryId, item.WebhookUrl, workerIdent);

		await Task.CompletedTask;
	}

	public async Task<ProcessingResult> ProcessAsync(
		PendingWebhook item,
		Guid leaseToken,
		CancellationToken ct)
	{
		try
		{
			_logger.LogDebug(
				"Processing webhook delivery. DeliveryId={DeliveryId}, Attempt={Attempt}",
				item.DeliveryId, item.AttemptNumber);

			await _service.DeliverAsync(item, ct);

			return new ProcessingResult
			{
				Success = true,
				ResultData = new { DeliveryId = item.DeliveryId }
			};
		}
		catch (Exception ex)
		{
			_logger.LogError(
				ex,
				"Error processing webhook delivery. DeliveryId={DeliveryId}",
				item.DeliveryId);

			return new ProcessingResult
			{
				Success = false,
				ErrorMessage = ex.Message
			};
		}
	}

	public async Task CompleteAsync(
		PendingWebhook item,
		Guid leaseToken,
		ProcessingResult result,
		CancellationToken ct)
	{
		// Completion: log the result
		if (result.Success)
		{
			_logger.LogDebug(
				"Webhook delivery processing completed successfully. DeliveryId={DeliveryId}",
				item.DeliveryId);
		}
		else
		{
			_logger.LogWarning(
				"Webhook delivery processing completed with error. DeliveryId={DeliveryId}, Error={Error}",
				item.DeliveryId, result.ErrorMessage);
		}

		await Task.CompletedTask;
	}
}
