
namespace WebsiteCollector.Worker
{
	public interface IWebsiteCollectionService
	{
		Task<IReadOnlyList<ClaimedWebsiteSelector>> ClaimSelectorsAsync(Guid worker, int count, int lease, CancellationToken ct);
		Task StartExecutionAsync(Guid websiteSelectorsId, Guid leaseToken, Guid worker, CancellationToken ct);
		Task<bool> RenewLeaseAsync(Guid websiteSelectorsId, Guid leaseToken, int leaseSeconds, CancellationToken ct);
		Task<int> CompleteExecutionAsync(Guid websiteSelectorsId, Guid leaseToken, bool success, int? httpStatus, string? error, byte[]? hash, string? raw, string? extracted, CancellationToken ct, Guid? websiteId = null, Guid? collectedDataId = null, DateTimeOffset? startedAt = null, int? attemptNumber = null);
	}
}
