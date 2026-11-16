using System.Text;
using Beep.Scheduler.Domain;
using Beep.Scheduler.Handlers;
using Beep.Scheduler.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Beep.Scheduler.Services;

/// <summary>
/// Orchestrates schedule execution and work item registration
/// </summary>
public class SchedulerService : BackgroundService
{
	private readonly SchedulerDatabase _database;
	private readonly IServiceProvider _serviceProvider;
	private readonly ILogger<SchedulerService> _logger;
	private readonly int _pollIntervalSeconds;

	public SchedulerService(
		SchedulerDatabase database,
		IServiceProvider serviceProvider,
		ILogger<SchedulerService> logger,
		int pollIntervalSeconds = 30)
	{
		_database = database ?? throw new ArgumentNullException(nameof(database));
		_serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
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
				await PollAndExecuteSchedulesAsync(stoppingToken);
			}
			catch (OperationCanceledException)
			{
				_logger.LogInformation("SchedulerService cancellation requested");
				break;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error in scheduler execution loop");
				// Continue on error - don't let one failure stop the entire scheduler
			}

			// Wait before next poll
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
	/// Poll database for due schedules and execute them
	/// </summary>
	private async Task PollAndExecuteSchedulesAsync(CancellationToken cancellationToken)
	{
		var schedules = await _database.GetDueSchedulesAsync(cancellationToken);

		if (schedules.Count == 0)
		{
			_logger.LogDebug("No schedules due for execution");
			return;
		}

		_logger.LogInformation("Found {ScheduleCount} schedule(s) due for execution", schedules.Count);

		foreach (var schedule in schedules)
		{
			if (cancellationToken.IsCancellationRequested)
				break;

			await ExecuteScheduleAsync(schedule, cancellationToken);
		}
	}

	/// <summary>
	/// Execute a single schedule
	/// </summary>
	private async Task ExecuteScheduleAsync(WorkerSchedule schedule, CancellationToken cancellationToken)
	{
		var startedAt = DateTimeOffset.UtcNow;
		var executionLog = new StringBuilder();

		try
		{
			_logger.LogInformation(
				"Executing schedule {ScheduleId} for worker type {WorkerType} (Frequency: {Frequency})",
				schedule.Id, schedule.WorkerType, schedule.Frequency);

			// Find handler for this worker type
			var handler = FindHandlerForWorkerType(schedule.WorkerType);
			if (handler == null)
			{
				var errorMsg = $"No handler registered for worker type '{schedule.WorkerType}'";
				_logger.LogWarning(errorMsg);

				await _database.LogExecutionAsync(
					schedule.Id,
					"Skipped",
					0,
					errorMsg,
					null,
					startedAt,
					cancellationToken);

				return;
			}

			executionLog.AppendLine($"Handler: {handler.GetType().Name}");

			// Execute the handler
			var result = await handler.RegisterItemsAsync(schedule, cancellationToken);

			executionLog.AppendLine($"Items Registered: {result.ItemsRegistered}");
			executionLog.AppendLine($"Success: {result.Success}");

			if (!string.IsNullOrEmpty(result.Details))
				executionLog.AppendLine($"Details: {result.Details}");

			var status = result.Success ? "Success" : "Failed";
			var errorMsg2 = result.Success ? null : result.Error;

			_logger.LogInformation(
				"Schedule {ScheduleId} executed. Status: {Status}, Items: {ItemCount}, Error: {Error}",
				schedule.Id, status, result.ItemsRegistered, errorMsg2);

			// Log execution
			await _database.LogExecutionAsync(
				schedule.Id,
				status,
				result.ItemsRegistered,
				errorMsg2,
				handler.GetType().Name,
				startedAt,
				cancellationToken);
		}
		catch (Exception ex)
		{
			_logger.LogError(
				ex,
				"Exception executing schedule {ScheduleId} for worker type {WorkerType}",
				schedule.Id, schedule.WorkerType);

			try
			{
				await _database.LogExecutionAsync(
					schedule.Id,
					"Failed",
					0,
					ex.Message,
					null,
					startedAt,
					cancellationToken);
			}
			catch (Exception logEx)
			{
				_logger.LogError(logEx, "Failed to log execution error for schedule {ScheduleId}", schedule.Id);
			}
		}
	}

	/// <summary>
	/// Find the registered handler for a worker type
	/// </summary>
	private IScheduleHandler? FindHandlerForWorkerType(string workerType)
	{
		try
		{
			// Get all registered handlers from the service provider
			// Handlers should be registered as IScheduleHandler with implementation factory
			var handlers = _serviceProvider.GetServices<IScheduleHandler>();
			return handlers.FirstOrDefault(h => h.WorkerType == workerType);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error finding handler for worker type {WorkerType}", workerType);
			return null;
		}
	}
}
