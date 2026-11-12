using System;
using System.Threading;
using System.Threading.Tasks;
using Beep.ServiceBus;
using Microsoft.Extensions.Logging;

namespace Beep.EventBus;

/// <summary>
/// Work processor adapter that delegates webhook delivery to WebhookDeliveryService.
/// Bridges between the generic worker framework and domain-specific delivery logic.
/// </summary>
public sealed class EventBusWorkProcessor : IWorkProcessor<PendingEventBus>
{
	private readonly IEventBusService _service;
	private readonly ILogger<EventBusWorkProcessor> _logger;

	public int HeartbeatIntervalSeconds => 30; // 30 seconds (1/10 of typical 5-minute lease)

	public EventBusWorkProcessor(
		IEventBusService service,
		ILogger<EventBusWorkProcessor> logger)
	{
		_service = service ?? throw new ArgumentNullException(nameof(service));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public async Task InitializeAsync(
		PendingEventBus item,
		Guid leaseToken,
		Guid workerIdent,
		CancellationToken ct)
	{
		// Initialization: log that processing is starting
		_logger.LogDebug(
			"Initializing EventBus delivery. EventBusDeliveryId={EventBusDeliveryId}, EventBusId={EventBusId}, Worker={Worker}",
			item.EventBusDeliveryId, item.EventBusId, workerIdent);

		await Task.CompletedTask;
	}

	public async Task<ProcessingResult> ProcessAsync(
		PendingEventBus item,
		Guid leaseToken,
		CancellationToken ct)
	{
		try
		{
			_logger.LogDebug(
				"Processing EventBus delivery. EventBusDeliveryId={EventBusDeliveryId}, Attempt={Attempt}",
				item.EventBusDeliveryId, item.AttemptNumber);

			await _service.DeliverAsync(item, ct);

			return new ProcessingResult
			{
				Success = true,
				ResultData = new { DeliveryId = item.EventBusDeliveryId }
			};
		}
		catch (Exception ex)
		{
			_logger.LogError(
				ex,
				"Error processing EventBus delivery. EventBusDeliveryId={EventBusDeliveryId}",
				item.EventBusDeliveryId);

			return new ProcessingResult
			{
				Success = false,
				ErrorMessage = ex.Message
			};
		}
	}

	public async Task CompleteAsync(
		PendingEventBus item,
		Guid leaseToken,
		ProcessingResult result,
		CancellationToken ct)
	{
		// Completion: log the result
		if (result.Success)
		{
			_logger.LogDebug(
				"EventBus delivery processing completed successfully. EventBusDeliveryId={EventBusDeliveryId}",
				item.EventBusDeliveryId);
		}
		else
		{
			_logger.LogWarning(
				"EventBus delivery processing completed with error. EventBusDeliveryId={EventBusDeliveryId}, Error={Error}",
				item.EventBusDeliveryId, result.ErrorMessage);
		}

		await Task.CompletedTask;
	}
}
