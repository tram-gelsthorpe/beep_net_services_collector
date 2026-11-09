using Beep.ServiceBus;

namespace WebsiteCollector.Worker;

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
