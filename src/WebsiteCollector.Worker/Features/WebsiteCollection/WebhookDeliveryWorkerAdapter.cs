using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WebsiteCollector.Features.WebsiteCollection.Models;
using WebsiteCollector.Features.WebsiteCollection.Repositories;
using WebsiteCollector.Features.WebsiteCollection.Services;
using WorkerFramework.Processing;
using WorkerFramework.Leasing;

namespace WebsiteCollector.Features.WebsiteCollection;

/// <summary>
/// Adapter for IClaimedItem to wrap PendingWebhookDelivery
/// </summary>
internal sealed class ClaimedDeliveryItem : IClaimedItem<PendingWebhookDelivery>
{
	public PendingWebhookDelivery Item { get; }
	public Guid LeaseToken { get; }

	public ClaimedDeliveryItem(PendingWebhookDelivery delivery, Guid leaseToken)
	{
		Item = delivery;
		LeaseToken = leaseToken;
	}
}

/// <summary>
/// Lease manager implementation for webhook deliveries using IWebhookDeliveryRepository
/// </summary>
internal sealed class DeliveryLeaseManager : ILeaseManager<PendingWebhookDelivery>
{
	private readonly IWebhookDeliveryRepository _repository;

	public DeliveryLeaseManager(IWebhookDeliveryRepository repository)
	{
		_repository = repository ?? throw new ArgumentNullException(nameof(repository));
	}

	public async Task<IReadOnlyList<IClaimedItem<PendingWebhookDelivery>>> ClaimAsync(
		Guid workerIdent,
		int maxItems,
		int leaseSeconds,
		CancellationToken ct)
	{
		var items = await _repository.ClaimPendingDeliveriesAsync(maxItems, ct);
		return items
			.Select(item => (IClaimedItem<PendingWebhookDelivery>)new ClaimedDeliveryItem(item, Guid.NewGuid()))
			.ToList();
	}

	public async Task<bool> RenewAsync(object item, Guid leaseToken, int leaseSeconds, CancellationToken ct)
	{
		// Webhook deliveries typically don't have heartbeat renewal in this implementation
		// They're meant to be completed quickly; return true to avoid lease loss
		await Task.CompletedTask;
		return true;
	}

	public async Task ReleaseAsync(object item, Guid leaseToken, CancellationToken ct)
	{
		// Release is handled via repository update in the processor
		await Task.CompletedTask;
	}
}

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
