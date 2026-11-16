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
	private const string WorkerType = "WebsiteCollector";
	private IWorkItemRepository _workItemRepository;
	private SelectorManagementRepository _selectorManagementRepository;
	private readonly ILogger<WebsiteSelectorsRepository> _logger;

	public WebsiteSelectorsRepository(IConfiguration config, string serviceBusDatabaseName, string selectorDatabaseName, ILoggerFactory loggerFactory)
	{
		_logger = loggerFactory.CreateLogger<WebsiteSelectorsRepository>();

		_workItemRepository = new WorkItemRepository(config.GetConnectionString(serviceBusDatabaseName) ?? throw new InvalidOperationException("Missing ServiceBus connection string"),
			loggerFactory.CreateLogger<WorkItemRepository>());

		_selectorManagementRepository = new SelectorManagementRepository(config.GetConnectionString(selectorDatabaseName) ?? throw new InvalidOperationException("Missing Selector connection string"),
			loggerFactory.CreateLogger<SelectorManagementRepository>());
	}

	public async Task<IReadOnlyList<ClaimedWebsiteSelector>> ClaimAsync(Guid worker, int count, int lease, CancellationToken ct)
	{
		_logger.LogInformation(
			"ClaimAsync: Requesting {Count} work items for worker {Worker} with {LeaseSeconds}s lease",
			count, worker, lease);

		var items = await _workItemRepository.ClaimAsync(
			workerType: WorkerType,
			workerId: worker,
			batchSize: count,
			leaseSeconds: lease,
			cancellationToken: ct);

		_logger.LogInformation(
			"ClaimAsync: WorkItemRepository returned {ItemCount} items",
			items.Count);

		if (items.Count == 0)
		{
			_logger.LogWarning(
				"ClaimAsync: No items claimed. Queue may be empty or all items already leased. " +
				"Worker={Worker}, WorkerType={WorkerType}",
				worker, WorkerType);
			return new List<ClaimedWebsiteSelector>().AsReadOnly();
		}

		var result = new List<ClaimedWebsiteSelector>();
		foreach (var item in items)
		{
			try
			{
				_logger.LogDebug(
					"ClaimAsync: Processing claimed item {WorkItemId}, DomainId={DomainId}",
					item.WorkItemId, item.DomainId);

				var selectors = await _selectorManagementRepository.GetWebsiteSelectorAsync(item.DomainId, ct);

				if (selectors.Count == 0)
				{
					_logger.LogError(
						"ClaimAsync: No selectors found for work item {WorkItemId} DomainId={DomainId}. " +
						"This item will be skipped.",
						item.WorkItemId, item.DomainId);
					continue;
				}

				var selector = selectors.First();

				result.Add(new ClaimedWebsiteSelector(
					WebsiteSelectorsId: item.DomainId,
					WebsitesId: selector.WebsitesId,
					CollectionUrl: selector.CollectionUrl,
					CollectionType: selector.CollectionType,
					ContentSelector: selector.ContentSelector,
					TableSelector: selector.TableSelector,
					TimeoutSeconds: selector.TimeoutSeconds, // Default timeout
					MaxAttempts: item.MaxAttempts,
					AttemptCount: item.AttemptNumber - 1,
					LeaseToken: item.LeaseToken));

				_logger.LogDebug(
					"ClaimAsync: Successfully enriched item {WorkItemId} with selector data",
					item.WorkItemId);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex,
					"ClaimAsync: Error processing claimed item {WorkItemId} DomainId={DomainId}. Skipping.",
					item.WorkItemId, item.DomainId);
				// Continue processing remaining items
			}
		}

		_logger.LogInformation(
			"ClaimAsync: Returning {ResultCount} enriched items (claimed {ClaimedCount}, skipped {SkippedCount})",
			result.Count, items.Count, items.Count - result.Count);

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
