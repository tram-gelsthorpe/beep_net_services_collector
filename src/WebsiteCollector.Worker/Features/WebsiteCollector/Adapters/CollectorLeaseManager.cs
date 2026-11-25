using Beep.ServiceBus;

namespace WebsiteCollector.Worker;

/// <summary>
/// Lease manager implementation for website selectors using IWebsiteSelectorsService
/// </summary>
internal sealed class CollectorLeaseManager : ILeaseManager<ClaimedWebsiteSelector>
{
	private readonly IWebsiteSelectorsService _collectionService;

	public CollectorLeaseManager(IWebsiteSelectorsService collectionService)
	{
		_collectionService = collectionService ?? throw new ArgumentNullException(nameof(collectionService));
	}

	public async Task<IReadOnlyList<IClaimedItem<ClaimedWebsiteSelector>>> ClaimAsync(
		Guid workerId,
		int maxItems,
		int leaseSeconds,
		CancellationToken ct)
	{
		var items = await _collectionService.ClaimSelectorsAsync(workerId, maxItems, leaseSeconds, ct);
		return [.. items.Select(item => new ClaimedSelectorItem(item, item.LeaseToken))];
	}

	public Task<bool> RenewAsync(object item, Guid leaseToken, int leaseSeconds, CancellationToken ct) =>
		item is ClaimedWebsiteSelector selector
			? _collectionService.RenewLeaseAsync(selector.WorkItemId, leaseToken, leaseSeconds, ct)
			: Task.FromResult(false);

	// The lease is released when the processor completes the work item.
	public Task ReleaseAsync(object item, Guid leaseToken, CancellationToken ct) => Task.CompletedTask;
}
