using WebsiteCollector.Infrastructure.Database;
using WebsiteCollector.Features.WebsiteCollection.Models;

namespace WebsiteCollector.Features.WebsiteCollection.Repositories;

public sealed class WebsiteCollectionRepository(Database db) : IWebsiteCollectionRepository
{
	public async Task<IReadOnlyList<ClaimedWebsiteSelector>> ClaimAsync(Guid worker, int count, int lease, CancellationToken ct) =>
		await db.ClaimAsync(worker, count, lease, ct);

	public async Task StartAsync(Guid websiteSelectorsId, Guid leaseToken, Guid worker, CancellationToken ct) =>
		await db.StartAsync(websiteSelectorsId, leaseToken, worker, ct);

	public async Task<bool> RenewAsync(Guid websiteSelectorsId, Guid leaseToken, int leaseSeconds, CancellationToken ct) =>
		await db.RenewAsync(websiteSelectorsId, leaseToken, leaseSeconds, ct);

	public async Task<int> FinishAsync(Guid websiteSelectorsId, Guid leaseToken, bool success, int? httpStatus, string? error, byte[]? hash, string? raw, string? extracted, CancellationToken ct) =>
		await db.FinishAsync(websiteSelectorsId, leaseToken, success, httpStatus, error, hash, raw, extracted, ct);
}
