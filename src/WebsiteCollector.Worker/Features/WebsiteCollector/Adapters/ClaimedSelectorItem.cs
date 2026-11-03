using WebsiteCollector.Worker.Features.WebsiteCollector.Models;
using WorkerFramework.Leasing;

namespace WebsiteCollector.Worker.Features.WebsiteCollector.Adapters;

/// <summary>
/// Adapter for IClaimedItem to wrap ClaimedWebsiteSelector
/// </summary>
internal sealed class ClaimedSelectorItem : IClaimedItem<ClaimedWebsiteSelector>
{
	public ClaimedWebsiteSelector Item { get; }
	public Guid LeaseToken { get; }

	public ClaimedSelectorItem(ClaimedWebsiteSelector selector, Guid leaseToken)
	{
		Item = selector;
		LeaseToken = leaseToken;
	}
}
