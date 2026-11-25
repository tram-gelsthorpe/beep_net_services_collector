using Beep.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Beep.Collector;

/// <summary>
/// Repository that adapts the generic WorkerFramework to website selectors domain.
/// Uses the generic WorkItems interface for all work claiming, leasing, and completion.
/// </summary>
public sealed class WebsiteSelectorsRepository : IWebsiteSelectorsRepository
{
	private readonly IOptions<WebsiteCollectorOptions> _websiteCollectorOptions;
	private readonly IWorkItemRepository _workItemRepository;
	private readonly SelectorManagementRepository _selectorManagementRepository;
	private readonly ILogger<WebsiteSelectorsRepository> _logger;

	public WebsiteSelectorsRepository(IWorkItemRepository workItemRepository,
		SelectorManagementRepository selectorManagementRepository,
		IOptions<WebsiteCollectorOptions> websiteCollectorOptions,
		ILogger<WebsiteSelectorsRepository> logger)
	{
		_workItemRepository = workItemRepository ?? throw new ArgumentNullException(nameof(workItemRepository));
		_selectorManagementRepository = selectorManagementRepository ?? throw new ArgumentNullException(nameof(selectorManagementRepository));
		_websiteCollectorOptions = websiteCollectorOptions ?? throw new ArgumentNullException(nameof(websiteCollectorOptions));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public async Task<IReadOnlyList<ClaimedWebsiteSelector>> ClaimAsync(Guid workerId, int count, int lease, CancellationToken ct)
	{
		_logger.LogDebug(
			"ClaimAsync: Requesting {Count} work items for worker {WorkerId} with {LeaseSeconds}s lease",
			count, workerId, lease);

		var items = await _workItemRepository.ClaimAsync(
			workerType: _websiteCollectorOptions.Value.WorkerType,
			workerId: workerId,
			batchSize: count,
			leaseSeconds: lease,
			cancellationToken: ct);

		if (items.Count == 0)
		{
			_logger.LogDebug(
				"ClaimAsync: No items claimed. WorkerId={WorkerId}, WorkerType={WorkerType}",
				workerId, _websiteCollectorOptions.Value.WorkerType);
			return [];
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
					// Disabled or deleted after it was queued: nothing to collect. Close the work item
					// so it is not left leased and reclaimed until it is abandoned.
					_logger.LogInformation(
						"ClaimAsync: Selector {DomainId} is disabled or no longer exists; closing work item {WorkItemId} without collecting",
						item.DomainId, item.WorkItemId);
					await CloseUnprocessedAsync(item, succeeded: true, error: null, SkippedResult);
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
			catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
			{
				// Could not read the selector (e.g. the database is unavailable): fail the attempt so the
				// ServiceBus retries it with back-off, instead of leaving it leased.
				_logger.LogError(ex,
					"ClaimAsync: Error processing claimed item {WorkItemId} DomainId={DomainId}. It will be retried.",
					item.WorkItemId, item.DomainId);
				await CloseUnprocessedAsync(item, succeeded: false, $"Could not load selector: {ex.Message}", resultData: null);
			}
		}

		_logger.LogInformation(
			"ClaimAsync: Returning {ResultCount} enriched items (claimed {ClaimedCount}, skipped {SkippedCount})",
			result.Count, items.Count, items.Count - result.Count);

		return result.AsReadOnly();
	}

	/// <summary>ResultData of a work item closed because its selector is disabled or gone.</summary>
	public const string SkippedResult = """{"skipped":"Selector disabled or not found"}""";

	/// <summary>
	/// Complete a claimed work item that will not be handed to the worker. Never throws: if it cannot
	/// be completed, its lease expires and the ServiceBus reclaims it.
	/// </summary>
	private async Task CloseUnprocessedAsync(IClaimedWorkItem item, bool succeeded, string? error, string? resultData)
	{
		try
		{
			// Not the caller's token: a claimed item must still be closed when the worker is stopping.
			var completed = await _workItemRepository.CompleteAsync(
				item.WorkItemId, item.LeaseToken, succeeded, error, resultData, cancellationToken: CancellationToken.None);
			if (!completed)
				_logger.LogWarning("ClaimAsync: Lease on work item {WorkItemId} was lost before it could be closed", item.WorkItemId);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex,
				"ClaimAsync: Could not close work item {WorkItemId}; it will be reclaimed when its lease expires",
				item.WorkItemId);
		}
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

	public async Task<CollectionFinishResult> FinishAsync(Guid workItemId,
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
		Guid? collectedDataId = null;
		if (completed)
		{
			collectedDataId = await _selectorManagementRepository.SaveCollectionResultAsync(
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

		return new CollectionFinishResult(completed, collectedDataId);
	}
}
