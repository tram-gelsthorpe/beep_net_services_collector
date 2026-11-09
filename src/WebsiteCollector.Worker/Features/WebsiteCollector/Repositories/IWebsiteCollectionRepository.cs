namespace WebsiteCollector.Worker;

public interface IWebsiteCollectionRepository
{
	Task<IReadOnlyList<ClaimedWebsiteSelector>> ClaimAsync(Guid worker, int count, int lease, CancellationToken ct);
	Task StartAsync(Guid websiteSelectorsId, Guid leaseToken, Guid worker, CancellationToken ct);
	Task<bool> RenewAsync(Guid websiteSelectorsId, Guid leaseToken, int leaseSeconds, CancellationToken ct);
	Task<int> FinishAsync(Guid websiteSelectorsId, Guid leaseToken, bool success, int? httpStatus, string? error, byte[]? hash, string? raw, string? extracted, CancellationToken ct);
}
