using System;

namespace Beep.ServiceBus;

/// <summary>
/// Represents a claimed work item with lease information.
/// </summary>
/// <typeparam name="T">The type of work item</typeparam>
public interface IClaimedItem<T>
{
	T Item { get; }
	Guid LeaseToken { get; }
}
