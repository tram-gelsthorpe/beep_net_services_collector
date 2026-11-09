using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Beep.ServiceBus;

namespace Beep.EventBus;

/// <summary>
/// Lease manager adapter for EventBus delivery work items.
/// Abstracts the repository operations over ILeaseManager interface.
/// </summary>
public sealed class EventBusLeaseManager : ILeaseManager<PendingEventBus>
{
	private readonly IEventBusRepository _repository;

	public EventBusLeaseManager(IEventBusRepository repository)
	{
		_repository = repository ?? throw new ArgumentNullException(nameof(repository));
	}

	public async Task<IReadOnlyList<IClaimedItem<PendingEventBus>>> ClaimAsync(
		Guid workerIdent,
		int maxItems,
		int leaseSeconds,
		CancellationToken ct)
	{
		var deliveries = await _repository.GetPendingDeliveriesAsync(maxItems, ct);
		return [.. deliveries
			.Select(d => new ClaimedEventBus(d))
			.Cast<IClaimedItem<PendingEventBus>>()];
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
		// Release is a no-op for EventBus delivery; completion is recorded via repository
		await Task.CompletedTask;
	}
}
