using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Beep.ServiceBus;

/// <summary>
/// <see cref="ILeaseManager{T}"/> over the ServiceBus work item queue for one worker type.
/// Use it with <see cref="GenericWorkerBase{T}"/> so a library gets claiming, lease renewal,
/// crash recovery and retries from the ServiceBus instead of implementing its own.
/// The processor loads its domain data from <see cref="IClaimedWorkItem.DomainId"/> and
/// finishes the item with <see cref="IWorkItemRepository.CompleteAsync"/>.
/// </summary>
public sealed class WorkItemLeaseManager : ILeaseManager<IClaimedWorkItem>
{
	private readonly IWorkItemRepository _workItems;
	private readonly string _workerType;

	public WorkItemLeaseManager(IWorkItemRepository workItems, string workerType)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(workerType);
		_workItems = workItems ?? throw new ArgumentNullException(nameof(workItems));
		_workerType = workerType;
	}

	public async Task<IReadOnlyList<IClaimedItem<IClaimedWorkItem>>> ClaimAsync(
		Guid workerId, int maxItems, int leaseSeconds, CancellationToken ct)
	{
		var items = await _workItems.ClaimAsync(_workerType, workerId, maxItems, leaseSeconds, ct);
		return [.. items.Select(item => new ClaimedWorkItemLease(item))];
	}

	public Task<bool> RenewAsync(object item, Guid leaseToken, int leaseSeconds, CancellationToken ct) =>
		item is IClaimedWorkItem workItem
			? _workItems.RenewLeaseAsync(workItem.WorkItemId, leaseToken, leaseSeconds, ct)
			: Task.FromResult(false);

	public Task ReleaseAsync(object item, Guid leaseToken, CancellationToken ct) =>
		item is IClaimedWorkItem workItem
			? _workItems.ReleaseLeaseAsync(workItem.WorkItemId, leaseToken, ct)
			: Task.CompletedTask;
}
