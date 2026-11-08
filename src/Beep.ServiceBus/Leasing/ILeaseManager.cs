using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Beep.ServiceBus;

/// <summary>
/// Manages lease lifecycle for work items: claiming, renewing, and releasing.
/// Implements the generic lease pattern for any process/queue system.
/// </summary>
/// <typeparam name="T">The type of work item to manage</typeparam>
public interface ILeaseManager<T>
{
	/// <summary>
	/// Claims one or more available work items with a lease.
	/// </summary>
	/// <param name="workerIdent">Unique identifier for the claiming worker</param>
	/// <param name="maxItems">Maximum number of items to claim</param>
	/// <param name="leaseSeconds">Duration in seconds for the lease</param>
	/// <param name="ct">Cancellation token</param>
	/// <returns>List of claimed items with lease tokens</returns>
	Task<IReadOnlyList<IClaimedItem<T>>> ClaimAsync(Guid workerIdent, int maxItems, int leaseSeconds, CancellationToken ct);

	/// <summary>
	/// Renews an existing lease for a work item.
	/// </summary>
	/// <param name="item">Identifier for the item (implementation-specific)</param>
	/// <param name="leaseToken">The lease token to renew</param>
	/// <param name="leaseSeconds">New lease duration in seconds</param>
	/// <param name="ct">Cancellation token</param>
	/// <returns>True if renewal succeeded; false if lease was lost or rejected</returns>
	Task<bool> RenewAsync(object item, Guid leaseToken, int leaseSeconds, CancellationToken ct);

	/// <summary>
	/// Releases a lease after work is complete (success or failure).
	/// </summary>
	/// <param name="item">Identifier for the item (implementation-specific)</param>
	/// <param name="leaseToken">The lease token to release</param>
	/// <param name="ct">Cancellation token</param>
	Task ReleaseAsync(object item, Guid leaseToken, CancellationToken ct);
}
