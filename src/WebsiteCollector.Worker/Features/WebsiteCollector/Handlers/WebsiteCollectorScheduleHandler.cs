using Beep.Scheduler;
using Beep.ServiceBus;
using Microsoft.Extensions.Logging;

namespace WebsiteCollector.Worker;

/// <summary>
/// Schedule handler that enqueues a collection work item for every enabled website selector.
/// </summary>
/// <remarks>
/// Selectors are read and enqueued one page at a time, so memory use and the size of each
/// database call stay constant however many selectors there are. Selectors that still have an
/// unfinished work item (for example from the previous run) are skipped, so a run that is
/// interrupted and retried does not create duplicates.
/// </remarks>
public class WebsiteCollectorScheduleHandler : IScheduleHandler
{
	/// <summary>Default number of selectors read and enqueued per database call.</summary>
	public const int DefaultPageSize = 5000;

	private const string DomainType = "WebsiteSelector";
	private readonly IWorkItemRepository _workItemRepository;
	private readonly SelectorManagementRepository _selectorManagementRepository;
	private readonly ILogger<WebsiteCollectorScheduleHandler> _logger;
	private readonly int _pageSize;

	public string WorkerType { get; }
	public Guid WorkerId { get; }

	public WebsiteCollectorScheduleHandler(
		IWorkItemRepository workItemRepository,
		SelectorManagementRepository selectorManagementRepository,
		string workerType,
		Guid workerId,
		ILogger<WebsiteCollectorScheduleHandler> logger,
		int pageSize = DefaultPageSize)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

		_workItemRepository = workItemRepository ?? throw new ArgumentNullException(nameof(workItemRepository));
		_selectorManagementRepository = selectorManagementRepository ?? throw new ArgumentNullException(nameof(selectorManagementRepository));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_pageSize = pageSize;

		WorkerType = workerType ?? throw new ArgumentNullException(nameof(workerType));
		WorkerId = workerId;
	}

	/// <summary>
	/// Register work items for all active website selectors
	/// </summary>
	public async Task<ScheduleHandlerResult> RegisterItemsAsync(
		WorkerSchedule schedule,
		CancellationToken cancellationToken)
	{
		var result = new ScheduleHandlerResult();
		int total = 0, registered = 0, skipped = 0, failed = 0;

		try
		{
			_logger.LogInformation("WebsiteCollectorScheduleHandler: Registering work items for active selectors (page size {PageSize})", _pageSize);

			var afterId = Guid.Empty;
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var page = await _selectorManagementRepository.GetActiveSelectorsPageAsync(afterId, _pageSize, cancellationToken);
				if (page.Count == 0)
					break;

				afterId = page[^1].Id;
				total += page.Count;

				try
				{
					var inserted = await _workItemRepository.EnqueueBatchAsync(
						WorkerType,
						DomainType,
						page.Select(s => new WorkItemRequest(s.Id, s.MaxAttempts)).ToList(),
						cancellationToken);

					registered += inserted;
					skipped += page.Count - inserted;
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					// Keep going: the remaining pages are independent, and the next run retries
					// this page (selectors enqueued meanwhile are skipped, not duplicated).
					_logger.LogError(ex,
						"WebsiteCollectorScheduleHandler: Failed to register {Count} selectors up to {LastSelectorId}",
						page.Count, afterId);
					failed += page.Count;
				}

				_logger.LogDebug(
					"WebsiteCollectorScheduleHandler: {Total} selectors processed ({Registered} registered, {Skipped} already queued)",
					total, registered, skipped);

				if (page.Count < _pageSize)
					break;
			}

			result.ItemsRegistered = registered;
			result.Success = failed == 0;
			result.Details = $"Registered: {registered}, Already queued: {skipped}, Failed: {failed}, Total: {total}";
			if (failed > 0)
				result.Error = $"Failed to register {failed} of {total} selectors";

			if (total == 0)
				_logger.LogWarning("WebsiteCollectorScheduleHandler: No active selectors found");

			_logger.LogInformation("WebsiteCollectorScheduleHandler: Registration complete. {Details}", result.Details);
			return result;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			_logger.LogWarning(
				"WebsiteCollectorScheduleHandler: Registration cancelled after {Total} selectors ({Registered} registered)",
				total, registered);
			throw;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "WebsiteCollectorScheduleHandler: Unexpected error during schedule registration");

			result.ItemsRegistered = registered;
			result.Success = false;
			result.Error = ex.Message;
			result.Details = ex.ToString();
			return result;
		}
	}
}
