using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Beep.ServiceBus;

/// <summary>
/// Generic work item repository providing centralized work claiming,
/// lease management, and completion tracking for all workers.
/// This is the primary WorkerFramework abstraction for database operations.
/// </summary>
public interface IWorkItemRepository
{
	/// <summary>
	/// Atomically claim a batch of work items that are ready for processing.
	/// Returns items sorted by attempt count (retries first) then by due time.
	/// </summary>
	/// <param name="workerType">Category of work/worker type (e.g., 'SelectorCollector', 'WebhookDelivery')</param>
	/// <param name="workerId">ID of the worker process claiming the lease. This becomes the WorkerId in the database.</param>
	/// <param name="batchSize">Maximum number of items to claim (1-500)</param>
	/// <param name="leaseSeconds">Duration to hold the lease (60-3600)</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>List of claimed items; empty if none available</returns>
	Task<IReadOnlyList<IClaimedWorkItem>> ClaimAsync(
		string workerType,
		Guid workerId,
		int batchSize,
		int leaseSeconds,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Renew the lease on an active work item (extend LeaseUntil).
	/// Called during heartbeat to prevent lease expiration during long processing.
	/// </summary>
	/// <param name="workItemId">ID of the work item to renew</param>
	/// <param name="leaseToken">Token proving ownership (must match current lease)</param>
	/// <param name="leaseSeconds">New lease duration</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>True if renewed successfully; false if lease expired/invalid</returns>
	Task<bool> RenewLeaseAsync(
		Guid workItemId,
		Guid leaseToken,
		int leaseSeconds,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Release a lease without changing the item's status.
	/// Used for graceful cancellation or timeout handling.
	/// </summary>
	/// <param name="workItemId">ID of the work item</param>
	/// <param name="leaseToken">Token proving ownership</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>True if released; false if lease not found</returns>
	Task<bool> ReleaseLeaseAsync(
		Guid workItemId,
		Guid leaseToken,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Complete a work item with success or failure.
	/// On success, item is marked SUCCEEDED. On failure, calculates retry backoff.
	/// </summary>
	/// <param name="workItemId">ID of the work item</param>
	/// <param name="leaseToken">Token proving ownership</param>
	/// <param name="succeeded">True to mark success; false to fail and schedule retry</param>
	/// <param name="error">Error message (if failed)</param>
	/// <param name="resultData">Optional result payload (JSON)</param>
	/// <param name="backoffStrategy">Retry backoff strategy: 'exponential', 'linear', or 'fixed'</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>True if completed successfully</returns>
	Task<bool> CompleteAsync(
		Guid workItemId,
		Guid leaseToken,
		bool succeeded,
		string? error = null,
		string? resultData = null,
		string backoffStrategy = "exponential",
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Record a status snapshot / attempt detail without changing item state.
	/// Used for audit trails and intermediate heartbeat logging.
	/// </summary>
	/// <param name="workItemId">ID of the work item</param>
	/// <param name="attemptNumber">Current attempt number</param>
	/// <param name="statusSnapshot">Status name (e.g., 'RUNNING', 'PROCESSING_STEP_1')</param>
	/// <param name="errorMessage">Optional error details</param>
	/// <param name="resultData">Optional partial result or heartbeat data</param>
	/// <param name="cancellationToken">Cancellation token</param>
	Task RecordAttemptAsync(
		Guid workItemId,
		int attemptNumber,
		string statusSnapshot,
		string? errorMessage = null,
		string? resultData = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Create PENDING work items, due now, for many domain entities in one round trip.
	/// An entity that already has an unfinished work item (any status except SUCCEEDED or
	/// ABANDONED) for the same worker and domain type is skipped, so calling this again
	/// after a partial run does not create duplicates.
	/// </summary>
	/// <param name="workerType">Category of work/worker type</param>
	/// <param name="domainType">Domain entity type (e.g., 'WebsiteSelector')</param>
	/// <param name="items">Entities to enqueue; keep batches to a few thousand items</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>The number of work items created</returns>
	Task<int> EnqueueBatchAsync(
		string workerType,
		string domainType,
		IReadOnlyCollection<WorkItemRequest> items,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Get details of a work item for display/debugging.
	/// </summary>
	/// <param name="workItemId">ID of the work item</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>Work item metadata with attempt history</returns>
	Task<WorkItemDetail?> GetDetailAsync(
		Guid workItemId,
		CancellationToken cancellationToken = default);
}
