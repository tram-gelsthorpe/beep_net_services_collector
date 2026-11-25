using Beep.EventBus;
using Beep.Scheduler;
using Beep.ServiceBus;
using Microsoft.Extensions.Logging;

namespace WebsiteCollector.Worker;

/// <summary>
/// Schedule handler that, for every enabled website selector, registers its EventBus endpoint
/// (its DispatchUrl) and enqueues a collection work item.
/// </summary>
/// <remarks>
/// Selectors are read and processed one page at a time, so memory use and the size of each
/// database call stay constant however many selectors there are. Both steps are safe to repeat:
/// endpoint registration only changes what differs, and selectors that still have an unfinished
/// work item are skipped, so an interrupted run that is retried does not create duplicates.
/// Endpoints are registered before the work items are queued, so they exist by the time a
/// collection completes and its result is published.
/// </remarks>
public class WebsiteCollectorScheduleHandler : IScheduleHandler
{
	/// <summary>Default number of selectors read and processed per database call.</summary>
	public const int DefaultPageSize = 5000;

	private const string DomainType = "WebsiteSelector";
	private readonly IWorkItemRepository _workItemRepository;
	private readonly IEventBusRepository _eventBusRepository;
	private readonly SelectorManagementRepository _selectorManagementRepository;
	private readonly ILogger<WebsiteCollectorScheduleHandler> _logger;
	private readonly int _pageSize;

	/// <summary>Handler type schedules use to run this handler (the <c>Scheduler:HandlerType</c> setting).</summary>
	public const string Type = "WebsiteCollector";

	public string HandlerType => Type;

	/// <summary>ServiceBus worker type of the collection work items it enqueues.</summary>
	public string WorkerType { get; }

	public Guid WorkerId { get; }

	public WebsiteCollectorScheduleHandler(
		IWorkItemRepository workItemRepository,
		IEventBusRepository eventBusRepository,
		SelectorManagementRepository selectorManagementRepository,
		string workerType,
		Guid workerId,
		ILogger<WebsiteCollectorScheduleHandler> logger,
		int pageSize = DefaultPageSize)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

		_workItemRepository = workItemRepository ?? throw new ArgumentNullException(nameof(workItemRepository));
		_eventBusRepository = eventBusRepository ?? throw new ArgumentNullException(nameof(eventBusRepository));
		_selectorManagementRepository = selectorManagementRepository ?? throw new ArgumentNullException(nameof(selectorManagementRepository));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_pageSize = pageSize;

		WorkerType = workerType ?? throw new ArgumentNullException(nameof(workerType));
		WorkerId = workerId;
	}

	/// <summary>
	/// Register endpoints and work items for all active website selectors
	/// </summary>
	public async Task<ScheduleHandlerResult> RegisterItemsAsync(
		WorkerSchedule schedule,
		CancellationToken cancellationToken)
	{
		var result = new ScheduleHandlerResult();
		var counts = new Counts();

		try
		{
			_logger.LogInformation("WebsiteCollectorScheduleHandler: Registering active selectors (page size {PageSize})", _pageSize);

			var afterId = Guid.Empty;
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var page = await _selectorManagementRepository.GetActiveSelectorsPageAsync(afterId, _pageSize, cancellationToken);
				if (page.Count == 0)
					break;

				afterId = page[^1].Id;
				counts.Total += page.Count;

				// Keep going after a failed page: pages are independent, and the next run retries it.
				await RegisterEndpointsAsync(page, afterId, counts, cancellationToken);
				await EnqueueWorkItemsAsync(page, afterId, counts, cancellationToken);

				_logger.LogDebug("WebsiteCollectorScheduleHandler: {Details}", counts);

				if (page.Count < _pageSize)
					break;
			}

			var failed = counts.FailedWorkItems + counts.FailedEndpoints;
			result.ItemsRegistered = counts.Registered;
			result.Success = failed == 0;
			result.Details = counts.ToString();
			if (failed > 0)
				result.Error = $"Failed to register {counts.FailedWorkItems} work items and {counts.FailedEndpoints} endpoints of {counts.Total} selectors";

			if (counts.Total == 0)
				_logger.LogWarning("WebsiteCollectorScheduleHandler: No active selectors found");

			_logger.LogInformation("WebsiteCollectorScheduleHandler: Registration complete. {Details}", result.Details);
			return result;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			_logger.LogWarning("WebsiteCollectorScheduleHandler: Registration cancelled. {Details}", counts);
			throw;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "WebsiteCollectorScheduleHandler: Unexpected error during schedule registration");

			result.ItemsRegistered = counts.Registered;
			result.Success = false;
			result.Error = ex.Message;
			result.Details = ex.ToString();
			return result;
		}
	}

	private async Task RegisterEndpointsAsync(
		IReadOnlyList<ActiveWebsiteSelector> page, Guid lastId, Counts counts, CancellationToken ct)
	{
		try
		{
			// Every selector in the page is listed, including those without a DispatchUrl, so an
			// endpoint whose URL was removed or changed is disabled.
			var endpoints = page
				.Select(s => new EventBusEndpoint(s.Id, s.DispatchUrl, s.TimeoutSeconds, s.MaxAttempts))
				.ToList();

			var registration = await _eventBusRepository.RegisterEndpointsAsync(endpoints, ct);
			counts.EndpointsChanged += registration.Changed;
			counts.EndpointsDisabled += registration.Disabled;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogError(ex,
				"WebsiteCollectorScheduleHandler: Failed to register EventBus endpoints for {Count} selectors up to {LastSelectorId}",
				page.Count, lastId);
			counts.FailedEndpoints += page.Count;
		}
	}

	private async Task EnqueueWorkItemsAsync(
		IReadOnlyList<ActiveWebsiteSelector> page, Guid lastId, Counts counts, CancellationToken ct)
	{
		try
		{
			var inserted = await _workItemRepository.EnqueueBatchAsync(
				WorkerType,
				DomainType,
				page.Select(s => new WorkItemRequest(s.Id, s.MaxAttempts)).ToList(),
				ct);

			counts.Registered += inserted;
			counts.AlreadyQueued += page.Count - inserted;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogError(ex,
				"WebsiteCollectorScheduleHandler: Failed to register {Count} work items up to {LastSelectorId}",
				page.Count, lastId);
			counts.FailedWorkItems += page.Count;
		}
	}

	private sealed class Counts
	{
		public int Total, Registered, AlreadyQueued, FailedWorkItems;
		public int EndpointsChanged, EndpointsDisabled, FailedEndpoints;

		public override string ToString() =>
			$"Registered: {Registered}, Already queued: {AlreadyQueued}, Failed: {FailedWorkItems}, Total: {Total}; " +
			$"Endpoints changed: {EndpointsChanged}, disabled: {EndpointsDisabled}, failed: {FailedEndpoints}";
	}
}
