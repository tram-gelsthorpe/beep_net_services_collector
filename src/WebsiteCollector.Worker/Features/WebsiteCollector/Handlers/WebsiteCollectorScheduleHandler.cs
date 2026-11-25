using Beep.Scheduler;
using Beep.ServiceBus;
using Microsoft.Extensions.Logging;

namespace WebsiteCollector.Worker;

/// <summary>
/// Schedule handler that registers website selectors for daily collection
/// </summary>
public class WebsiteCollectorScheduleHandler : IScheduleHandler
{
	private const string DomainType = "WebsiteSelector";
	private readonly IWorkItemRepository _workItemRepository;
	private readonly SelectorManagementRepository _selectorManagementRepository;
	private readonly ILogger<WebsiteCollectorScheduleHandler> _logger;

	public string WorkerType { get; }
	public Guid WorkerId { get; }

	public WebsiteCollectorScheduleHandler(
		IWorkItemRepository workItemRepository,
		SelectorManagementRepository selectorManagementRepository,
		string workerType,
		Guid workerId,
		ILogger<WebsiteCollectorScheduleHandler> logger)
	{
		_workItemRepository = workItemRepository ?? throw new ArgumentNullException(nameof(workItemRepository));
		_selectorManagementRepository = selectorManagementRepository ?? throw new ArgumentNullException(nameof(selectorManagementRepository));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));

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

		try
		{
			_logger.LogInformation("WebsiteCollectorScheduleHandler: Fetching active selectors for registration");

			// Get all active selectors
			var activeSelectors = await _selectorManagementRepository.GetActiveSelectorsAsync(cancellationToken);

			if (activeSelectors.Count == 0)
			{
				_logger.LogWarning("WebsiteCollectorScheduleHandler: No active selectors found");
				result.ItemsRegistered = 0;
				result.Success = true;
				result.Details = "No active selectors to register";
				return result;
			}

			_logger.LogInformation(
				"WebsiteCollectorScheduleHandler: Found {SelectorCount} active selectors. Registering work items...",
				activeSelectors.Count);

			int registeredCount = 0;
			int failedCount = 0;
			var failedSelectors = new List<Guid>();

			// Register a work item for each active selector
			foreach (var selector in activeSelectors)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					_logger.LogWarning("WebsiteCollectorScheduleHandler: Registration cancelled by request");
					break;
				}

				try
				{
					// Create work item: workerType, workerId (domainId), domainId (selectorId), domainType, maxAttempts, scheduleForNow
					var workItemId = await _workItemRepository.EnqueueAsync(
						workerType: WorkerType,
						domainId: selector.Id,
						domainType: DomainType,
						maxAttempts: selector.MaxAttempts,
						scheduleForNow: true,
						cancellationToken: cancellationToken);

					_logger.LogDebug(
						"WebsiteCollectorScheduleHandler: Registered work item {WorkItemId} for selector {SelectorId}",
						workItemId, selector.Id);

					registeredCount++;
				}
				catch (Exception ex)
				{
					_logger.LogError(
						ex,
						"WebsiteCollectorScheduleHandler: Failed to register work item for selector {SelectorId}",
						selector.Id);

					failedCount++;
					failedSelectors.Add(selector.Id);
				}
			}

			result.ItemsRegistered = registeredCount;
			result.Success = failedCount == 0;
			result.Details = $"Registered: {registeredCount}, Failed: {failedCount}, Total: {activeSelectors.Count}";

			if (failedCount > 0)
			{
				result.Error = $"Failed to register {failedCount} of {activeSelectors.Count} selectors";
			}

			_logger.LogInformation(
				"WebsiteCollectorScheduleHandler: Registration complete. " +
				"Registered: {RegisteredCount}, Failed: {FailedCount}, Total: {TotalCount}",
				registeredCount, failedCount, activeSelectors.Count);

			return result;
		}
		catch (Exception ex)
		{
			_logger.LogError(
				ex,
				"WebsiteCollectorScheduleHandler: Unexpected error during schedule registration");

			result.Success = false;
			result.Error = ex.Message;
			result.Details = ex.ToString();

			return result;
		}
	}
}
