namespace WebsiteCollector.Worker;

/// <summary>
/// Repository interface for website selectors domain operations.
/// Adapts the generic ServiceBus work queue to selector-specific concerns.
/// </summary>
public interface IWebsiteSelectorsRepository
{
	/// <summary>
	/// Claim a batch of website selectors ready for collection.
	/// </summary>
	Task<IReadOnlyList<ClaimedWebsiteSelector>> ClaimAsync(Guid workerId, int count, int lease, CancellationToken ct);

	/// <summary>
	/// Record that collection has started for the given selector.
	/// </summary>
	Task StartAsync(Guid workItemId, Guid leaseToken, int leaseSeconds, Guid workerId, Guid domainId, int timeoutSeconds, int maxAttempts, CancellationToken ct);

	/// <summary>
	/// Renew the lease on an active collection task.
	/// </summary>
	Task<bool> RenewAsync(Guid workItemId, Guid leaseToken, int leaseSeconds, CancellationToken ct);

	/// <summary>
	/// Complete a collection task with success or failure. Returns 0 on success, -1 if the lease was lost.
	/// </summary>
	Task<CollectionFinishResult> FinishAsync(Guid workItemId, Guid leaseToken, bool success, int? httpStatus, string? error, byte[]? hash, string? raw, string? extracted, Guid domainId, CancellationToken ct);
}
