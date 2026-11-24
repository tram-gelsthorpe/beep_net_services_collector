
namespace WebsiteCollector.Worker
{
	public interface IWebsiteSelectorsService
	{
		Task<IReadOnlyList<ClaimedWebsiteSelector>> ClaimSelectorsAsync(Guid workerId, int count, int lease, CancellationToken ct);
		Task StartExecutionAsync(Guid workItemId, Guid leaseToken, int leaseSeconds, Guid workerId, Guid domainId, int timeoutSeconds, int maxAttempts, CancellationToken ct);
		Task<bool> RenewLeaseAsync(Guid workItemId, Guid leaseToken, int leaseSeconds, CancellationToken ct);
		Task<int> CompleteExecutionAsync(Guid workItemId, Guid domainId, Guid leaseToken, bool success, int? httpStatus, string? error, byte[]? hash, string? raw, string? extracted, CancellationToken ct, Guid? websiteId = null, Guid? collectedDataId = null, DateTimeOffset? startedAt = null, int? attemptNumber = null);
	}
}
