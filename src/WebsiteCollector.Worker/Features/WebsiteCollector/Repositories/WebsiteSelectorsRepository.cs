using Beep.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WebsiteCollector.Worker;

/// <summary>
/// Repository that adapts the generic WorkerFramework to website selectors domain.
/// Uses the generic WorkItems interface for all work claiming, leasing, and completion.
/// </summary>
public sealed class WebsiteSelectorsRepository : IWebsiteSelectorsRepository
{
	private readonly IOptions<WebsiteCollectorOptions> _websiteCollectorOptions;
	private IWorkItemRepository _workItemRepository;
	private SelectorManagementRepository _selectorManagementRepository;
	private readonly ILogger<WebsiteSelectorsRepository> _logger;

	public WebsiteSelectorsRepository(IWorkItemRepository workItemRepository,
		SelectorManagementRepository selectorManagementRepository,
		IOptions<WebsiteCollectorOptions> websiteCollectorOptions,
		ILoggerFactory loggerFactory)
	{
		_workItemRepository = workItemRepository;
		_selectorManagementRepository = selectorManagementRepository;
		_websiteCollectorOptions = websiteCollectorOptions ?? throw new ArgumentNullException(nameof(websiteCollectorOptions));
		_logger = loggerFactory.CreateLogger<WebsiteSelectorsRepository>();
	}

	public async Task<IReadOnlyList<ClaimedWebsiteSelector>> ClaimAsync(Guid workerId, int count, int lease, CancellationToken ct)
	{
		_logger.LogInformation(
			"ClaimAsync: Requesting {Count} work items for worker {WorkerId} with {LeaseSeconds}s lease",
			count, workerId, lease);

		var items = await _workItemRepository.ClaimAsync(
			workerType: _websiteCollectorOptions.Value.WorkerType,
			workerId: workerId,
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
				"WorkerId={WorkerId}, WorkerType={WorkerType}",
				workerId, _websiteCollectorOptions.Value.WorkerType);
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
					WorkItemId: item.WorkItemId,
					DomainId: item.DomainId,
					WebsitesId: selector.WebsitesId,
					CollectionUrl: selector.CollectionUrl,
					CollectionType: selector.CollectionType,
					ContentSelector: selector.ContentSelector,
					TimeoutSeconds: selector.TimeoutSeconds,
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

	public async Task StartAsync(Guid workItemId, 
		Guid leaseToken, 
		int leaseSeconds, 
		Guid workerId, Guid domainId, int timeoutSeconds, int maxAttempts, CancellationToken ct)
	{
		// Record attempt in generic WorkItems table
		await _workItemRepository.RecordAttemptAsync(
			workItemId: workItemId,
			attemptNumber: 1,
			statusSnapshot: "RUNNING",
			cancellationToken: ct);

		// Create execution record in domain-specific tables
		await _selectorManagementRepository.StartExecutionAsync(
			workerId: workerId,
			leaseToken: leaseToken,
			leaseSeconds: leaseSeconds,
			websiteSelectorsId: domainId,
			attemptNumber: 1,
			timeoutSeconds: timeoutSeconds,
			maxAttempts: maxAttempts,
			ct: ct);
	}

	public async Task<bool> RenewAsync(Guid workItemId, Guid leaseToken, int leaseSeconds, CancellationToken ct)
	{
		// Renew lease in generic WorkItems table
		var renewed = await _workItemRepository.RenewLeaseAsync(
			workItemId: workItemId,
			leaseToken: leaseToken,
			leaseSeconds: leaseSeconds,
			cancellationToken: ct);

		// Also renew in domain-specific monitor if renewed
		if (renewed)
		{
			await _selectorManagementRepository.RenewLeaseAsync(
				leaseToken: leaseToken,
				leaseSeconds: leaseSeconds,
				ct: ct);
		}

		return renewed;
	}

	public async Task<int> FinishAsync(Guid workItemId, 
		Guid leaseToken,
		bool success, 
		int? httpStatus, 
		string? error, 
		byte[]? hash, 
		string? raw, 
		string? extracted, 
		Guid domainId, 
		CancellationToken ct)
	{
		// Update ServiceBus WorkItems table
		var completed = await _workItemRepository.CompleteAsync(
			workItemId: workItemId,
			leaseToken: leaseToken,
			succeeded: success,
			error: error,
			resultData: success ? extracted : error,
			backoffStrategy: "exponential",
			cancellationToken: ct);

		// If completion was successful, also update WebsiteCollector domain tables
		if (completed)
		{
			await _selectorManagementRepository.SaveCollectionResultAsync(
				workItemId: workItemId,
				leaseToken: leaseToken,
				domainId: domainId,
				httpStatus: httpStatus,
				hash: hash,
				raw: raw,
				extracted: extracted,
				error: error,
				ct: ct);
		}

		return completed ? 0 : -1;
	}
}
