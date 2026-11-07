using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Beep.ServiceBus.Leasing;
using Beep.WebhookBus.Domain;
using Beep.WebhookBus.Repository;

namespace Beep.WebhookBus.Workers.Adapters;

/// <summary>
/// Lease manager adapter for webhook delivery work items.
/// Abstracts the repository operations over ILeaseManager interface.
/// </summary>
public sealed class DeliveryLeaseManager : ILeaseManager<PendingWebhookDelivery>
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
		var deliveries = await _repository.GetPendingDeliveriesAsync(maxItems, ct);
		return [.. deliveries
			.Select(d => new ClaimedDeliveryItem(d))
			.Cast<IClaimedItem<PendingWebhookDelivery>>()];
	}

	public async Task<bool> RenewAsync(object item, Guid leaseToken, int leaseSeconds, CancellationToken ct)
	{
		// For webhook delivery, renewals are implicit if the delivery is still in progress
		// and hasn't timed out. In a real implementation, this might touch the database
		// to confirm the item still exists and is not abandoned.
		return await Task.FromResult(true);
	}

	public async Task ReleaseAsync(object item, Guid leaseToken, CancellationToken ct)
	{
		// Release is a no-op for webhook delivery; completion is recorded via repository
		await Task.CompletedTask;
	}
}
