using WebsiteCollector.Worker.Infrastructure.Database;
using WebsiteCollector.Worker.Features.WebsiteCollector.Models;

namespace WebsiteCollector.Worker.Features.WebsiteCollector.Repositories;

/// <summary>
/// Repository that adapts the generic WorkerFramework to website collection domain.
/// Uses the generic WorkItems interface for all work claiming, leasing, and completion.
/// </summary>
public sealed class WebsiteCollectionRepository(Database db) : IWebsiteCollectionRepository
{
	public async Task<IReadOnlyList<ClaimedWebsiteSelector>> ClaimAsync(Guid worker, int count, int lease, CancellationToken ct)
	{
		// Use generic WorkerFramework to claim items
		var items = await db.WorkItems.ClaimAsync(
			workerType: "SelectorCollector",
			workerId: worker,
			batchSize: count,
			leaseSeconds: lease,
			cancellationToken: ct);

		// Map generic work items to domain-specific ClaimedWebsiteSelector
		var result = new List<ClaimedWebsiteSelector>();
		foreach (var item in items)
		{
			var selectors = await db.SelectorsAsync(item.DomainId, ct);
			var selector = selectors.First();

			result.Add(new ClaimedWebsiteSelector(
				WebsiteSelectorsId: item.DomainId,
				WebsitesId: selector.WebsitesId,
				CollectionUrl: selector.CollectionUrl,
				CollectionType: selector.CollectionType,
				ContentSelector: selector.ContentSelector,
				TableSelector: selector.TableSelector,
				IntervalSeconds: selector.IntervalSeconds,
				TimeoutSeconds: 30, // Default timeout
				MaxAttempts: item.MaxAttempts,
				AttemptCount: item.AttemptNumber - 1,
				LeaseToken: item.LeaseToken));
		}

		return result.AsReadOnly();
	}

	public async Task StartAsync(Guid websiteSelectorsId, Guid leaseToken, Guid worker, CancellationToken ct)
	{
		// Record the attempt in WorkerFramework
		await db.WorkItems.RecordAttemptAsync(
			workItemId: websiteSelectorsId,
			attemptNumber: 1,
			statusSnapshot: "RUNNING",
			cancellationToken: ct);
	}

	public async Task<bool> RenewAsync(Guid websiteSelectorsId, Guid leaseToken, int leaseSeconds, CancellationToken ct)
	{
		// Use generic WorkerFramework to renew lease
		return await db.WorkItems.RenewLeaseAsync(
			workItemId: websiteSelectorsId,
			leaseToken: leaseToken,
			leaseSeconds: leaseSeconds,
			cancellationToken: ct);
	}

	public async Task<int> FinishAsync(Guid websiteSelectorsId, Guid leaseToken, bool success, int? httpStatus, string? error, byte[]? hash, string? raw, string? extracted, CancellationToken ct)
	{
		// Use generic WorkerFramework to complete the work item
		var resultData = success ? extracted : error;

		var completed  = await db.WorkItems.CompleteAsync(
			workItemId: websiteSelectorsId,
			leaseToken: leaseToken,
			succeeded: success,
			error: error,
			resultData: resultData,
			backoffStrategy: "exponential",
			cancellationToken: ct);

		return completed ? 0 : -1;
	}
}
