using Beep.ServiceBus.Leasing;
using WebsiteCollector.Worker.Features.WebsiteCollector.Models;
using WebsiteCollector.Worker.Features.WebsiteCollector.Services;

namespace WebsiteCollector.Worker.Features.WebsiteCollector.Adapters;

/// <summary>
/// Lease manager implementation for website selectors using IWebsiteCollectionService
/// </summary>
internal sealed class CollectorLeaseManager : ILeaseManager<ClaimedWebsiteSelector>
{
	private readonly IWebsiteCollectionService _collectionService;

	public CollectorLeaseManager(IWebsiteCollectionService collectionService)
	{
		_collectionService = collectionService ?? throw new ArgumentNullException(nameof(collectionService));
	}

	public async Task<IReadOnlyList<IClaimedItem<ClaimedWebsiteSelector>>> ClaimAsync(
		Guid workerIdent,
		int maxItems,
		int leaseSeconds,
		CancellationToken ct)
	{
		var items = await _collectionService.ClaimSelectorsAsync(workerIdent, maxItems, leaseSeconds, ct);
		return items
			.Select(item => (IClaimedItem<ClaimedWebsiteSelector>)new ClaimedSelectorItem(item, item.LeaseToken))
			.ToList();
	}

	public async Task<bool> RenewAsync(object item, Guid leaseToken, int leaseSeconds, CancellationToken ct)
	{
		if (item is not ClaimedWebsiteSelector selector)
			return false;

		return await _collectionService.RenewLeaseAsync(selector.WebsiteSelectorsId, leaseToken, leaseSeconds, ct);
	}

	public async Task ReleaseAsync(object item, Guid leaseToken, CancellationToken ct)
	{
		if (item is not ClaimedWebsiteSelector selector)
			return;

		// Release is handled via CompleteExecutionAsync in the processor
		await Task.CompletedTask;
	}
}
