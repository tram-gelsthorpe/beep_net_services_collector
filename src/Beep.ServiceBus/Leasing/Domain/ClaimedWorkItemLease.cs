using System;

namespace Beep.ServiceBus;

/// <summary>A work item claimed by <see cref="WorkItemLeaseManager"/>, as the <see cref="IClaimedItem{T}"/> the worker holds.</summary>
internal sealed record ClaimedWorkItemLease(IClaimedWorkItem Item) : IClaimedItem<IClaimedWorkItem>
{
	public Guid LeaseToken => Item.LeaseToken;
}
