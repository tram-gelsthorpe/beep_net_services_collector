using Beep.ServiceBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Beep.Scheduler;

/// <summary>
/// Polls for due schedules and enqueues a ServiceBus work item for each one.
/// It does not run handlers itself: <see cref="SchedulerWorker"/> claims and runs the work items.
/// Every instance can poll: a schedule that already has an unfinished work item is skipped by the
/// ServiceBus, so only one run per schedule is ever queued at a time.
/// </summary>
public class SchedulerService : BackgroundService
{
	private readonly SchedulerRepository _database;
	private readonly IWorkItemRepository _workItems;
	private readonly SchedulerWorkerOptions _options;
	private readonly ILogger<SchedulerService> _logger;
	private readonly int _pollIntervalSeconds;

	/// <param name="database">Scheduler repository</param>
	/// <param name="workItems">ServiceBus work items</param>
	/// <param name="options">Worker type and attempts of schedule work items</param>
	/// <param name="logger">Logger</param>
	/// <param name="pollIntervalSeconds">How often to poll for due schedules (minimum 10)</param>
	public SchedulerService(
		SchedulerRepository database,
		IWorkItemRepository workItems,
		SchedulerWorkerOptions options,
		ILogger<SchedulerService> logger,
		int pollIntervalSeconds = 30)
	{
		_database = database ?? throw new ArgumentNullException(nameof(database));
		_workItems = workItems ?? throw new ArgumentNullException(nameof(workItems));
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_pollIntervalSeconds = Math.Max(10, pollIntervalSeconds); // Minimum 10 seconds
	}

	/// <summary>
	/// Main execution loop
	/// </summary>
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		_logger.LogInformation("SchedulerService starting. Poll interval: {PollInterval} seconds", _pollIntervalSeconds);

		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				await EnqueueDueSchedulesAsync(stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				// Don't let one failure stop the scheduler
				_logger.LogError(ex, "Error in scheduler execution loop");
			}

			try
			{
				await Task.Delay(TimeSpan.FromSeconds(_pollIntervalSeconds), stoppingToken);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}

		_logger.LogInformation("SchedulerService stopped");
	}

	/// <summary>
	/// Enqueue a run for every due schedule. Returns the number of runs queued (schedules that
	/// already have a queued or running run are not counted).
	/// </summary>
	public async Task<int> EnqueueDueSchedulesAsync(CancellationToken cancellationToken)
	{
		var schedules = await _database.GetDueSchedulesAsync(cancellationToken);
		if (schedules.Count == 0)
		{
			_logger.LogDebug("No schedules due for execution");
			return 0;
		}

		var queued = await _workItems.EnqueueBatchAsync(
			_options.WorkerType,
			SchedulerWorkerOptions.ScheduleDomainType,
			[.. schedules.Select(s => new WorkItemRequest(s.Id, _options.MaxAttempts))],
			cancellationToken);

		if (queued > 0)
			_logger.LogInformation("Queued {Queued} of {Due} due schedule(s)", queued, schedules.Count);
		else
			_logger.LogDebug("{Due} due schedule(s) already queued or running", schedules.Count);

		return queued;
	}
}
