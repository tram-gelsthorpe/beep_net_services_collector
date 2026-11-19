namespace WebsiteCollector.Worker
{
	/// <summary>
	/// Repository interface for website selectors domain operations.
	/// Adapts the generic WorkerFramework to selector-specific concerns.
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
		Task StartAsync(Guid websiteSelectorsId, Guid leaseToken, Guid workerId, CancellationToken ct);

		/// <summary>
		/// Renew the lease on an active collection task.
		/// </summary>
		Task<bool> RenewAsync(Guid websiteSelectorsId, Guid leaseToken, int leaseSeconds, CancellationToken ct);

		/// <summary>
		/// Complete a collection task with success or failure.
		/// </summary>
		Task<int> FinishAsync(Guid websiteSelectorsId, Guid leaseToken, bool success, int? httpStatus, string? error, byte[]? hash, string? raw, string? extracted, CancellationToken ct);
	}
}
