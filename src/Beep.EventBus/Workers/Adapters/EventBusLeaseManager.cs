using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Beep.ServiceBus;

namespace Beep.EventBus;

/// <summary>
/// Lease manager adapter for EventBus delivery work items.
/// </summary>
/// <remarks>
/// Deliveries are not leased in the database: <see cref="ClaimAsync"/> reads pending rows
/// without marking them, and renew/release are no-ops.
/// </remarks>
public sealed class EventBusLeaseManager : ILeaseManager<PendingEventBus>
{
	private readonly IEventBusRepository _repository;

	public EventBusLeaseManager(IEventBusRepository repository)
	{
		_repository = repository ?? throw new ArgumentNullException(nameof(repository));
	}

	public async Task<IReadOnlyList<IClaimedItem<PendingEventBus>>> ClaimAsync(
		Guid workerId,
		int maxItems,
		int leaseSeconds,
		CancellationToken ct)
	{
		var deliveries = await _repository.GetPendingDeliveriesAsync(maxItems, ct);
		return [.. deliveries.Select(d => new ClaimedEventBus(d))];
	}

	public Task<bool> RenewAsync(object item, Guid leaseToken, int leaseSeconds, CancellationToken ct) =>
		Task.FromResult(true);

	public Task ReleaseAsync(object item, Guid leaseToken, CancellationToken ct) =>
		Task.CompletedTask;
}
