using System;
using Beep.ServiceBus;

namespace Beep.EventBus;

/// <summary>
/// Represents a claimed webhook delivery work item with lease token.
/// </summary>
public sealed class ClaimedEventBus : IClaimedItem<PendingEventBus>
{
	public ClaimedEventBus(PendingEventBus item)
	{
		Item = item ?? throw new ArgumentNullException(nameof(item));
		LeaseToken = Guid.NewGuid(); // Each claim generates a unique lease token
	}

	public PendingEventBus Item { get; }
	public Guid LeaseToken { get; }
}
