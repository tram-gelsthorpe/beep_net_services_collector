using Beep.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WebsiteCollector.Worker;

/// <summary>
/// Repository that adapts the generic WorkerFramework to website selectors domain.
/// Uses the generic WorkItems interface for all work claiming, leasing, and completion.
/// </summary>
public sealed class WebsiteSelectorsRepository : IWebsiteSelectorsRepository
{
	private IWorkItemRepository _workItemRepository;
	private SelectorManagementRepository _selectorManagementRepository;

	public WebsiteSelectorsRepository(IConfiguration config, string serviceBusDatabaseName, string selectorDatabaseName, ILoggerFactory loggerFactory)
	{
		_workItemRepository = new WorkItemRepository(config.GetConnectionString(serviceBusDatabaseName) ?? throw new InvalidOperationException("Missing ServiceBus connection string"),
			loggerFactory.CreateLogger<WorkItemRepository>());

		_selectorManagementRepository = new SelectorManagementRepository(config.GetConnectionString(selectorDatabaseName) ?? throw new InvalidOperationException("Missing Selector connection string"),
			loggerFactory.CreateLogger<SelectorManagementRepository>());
	}

	public async Task<IReadOnlyList<ClaimedWebsiteSelector>> ClaimAsync(Guid worker, int count, int lease, CancellationToken ct)
	{
		var items = await _workItemRepository.ClaimAsync(
			workerType: "SelectorCollector",
			workerId: worker,
			batchSize: count,
			leaseSeconds: lease,
			cancellationToken: ct);

		var result = new List<ClaimedWebsiteSelector>();
		foreach (var item in items)
		{
			var selectors = await _selectorManagementRepository.GetWebsiteSelectorAsync(item.DomainId, ct);
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
		await _workItemRepository.RecordAttemptAsync(
			workItemId: websiteSelectorsId,
			attemptNumber: 1,
			statusSnapshot: "RUNNING",
			cancellationToken: ct);
	}

	public async Task<bool> RenewAsync(Guid websiteSelectorsId, Guid leaseToken, int leaseSeconds, CancellationToken ct)
	{
		return await _workItemRepository.RenewLeaseAsync(
			workItemId: websiteSelectorsId,
			leaseToken: leaseToken,
			leaseSeconds: leaseSeconds,
			cancellationToken: ct);
	}

	public async Task<int> FinishAsync(Guid websiteSelectorsId, Guid leaseToken, bool success, int? httpStatus, string? error, byte[]? hash, string? raw, string? extracted, CancellationToken ct)
	{
		var resultData = success ? extracted : error;

		var completed  = await _workItemRepository.CompleteAsync(
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
