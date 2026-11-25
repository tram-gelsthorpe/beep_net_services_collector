using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Beep.Scheduler;

/// <summary>
/// Orchestrates schedule execution and work item registration
/// </summary>
public class SchedulerService : BackgroundService
{
	private readonly SchedulerRepository _database;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger<SchedulerService> _logger;
	private readonly int _pollIntervalSeconds;
	private readonly int _claimLeaseSeconds;

	/// <param name="database">Scheduler repository</param>
	/// <param name="scopeFactory">Creates a DI scope per execution to resolve handlers</param>
	/// <param name="logger">Logger</param>
	/// <param name="pollIntervalSeconds">How often to poll for due schedules (minimum 10)</param>
	/// <param name="claimLeaseSeconds">
	/// How long a claimed schedule is reserved for this instance. Must be longer than the slowest
	/// handler run; if the instance dies, another one runs the schedule once the lease expires.
	/// </param>
	public SchedulerService(
		SchedulerRepository database,
		IServiceScopeFactory scopeFactory,
		ILogger<SchedulerService> logger,
		int pollIntervalSeconds = 30,
		int claimLeaseSeconds = 3600)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(claimLeaseSeconds);

		_database = database ?? throw new ArgumentNullException(nameof(database));
		_scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_pollIntervalSeconds = Math.Max(10, pollIntervalSeconds); // Minimum 10 seconds
		_claimLeaseSeconds = claimLeaseSeconds;
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
	/// Poll database for due schedules and execute them
	/// </summary>
	private async Task PollAndExecuteSchedulesAsync(CancellationToken cancellationToken)
	{
		// Claiming means another worker instance polling at the same time does not get the same schedules.
		var schedules = await _database.ClaimDueSchedulesAsync(_claimLeaseSeconds, cancellationToken);

		if (schedules.Count == 0)
		{
			_logger.LogDebug("No schedules due for execution");
			return;
		}

		_logger.LogInformation("Claimed {ScheduleCount} schedule(s) due for execution", schedules.Count);

		var next = 0;
		try
		{
			for (; next < schedules.Count; next++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				await ExecuteScheduleAsync(schedules[next], cancellationToken);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Shutting down: hand back the claims of the interrupted and not-yet-run schedules so
			// they are due again as soon as any instance polls, rather than after the lease expires.
			foreach (var schedule in schedules.Skip(next))
			{
				try
				{
					await _database.ReleaseClaimAsync(schedule.Id, CancellationToken.None);
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "Could not release claim on schedule {ScheduleId}; it is retried when the claim expires", schedule.Id);
				}
			}
			throw;
		}
	}

	/// <summary>
	/// Execute a single schedule. Handlers are resolved from a fresh scope per execution.
	/// </summary>
	private async Task ExecuteScheduleAsync(WorkerSchedule schedule, CancellationToken cancellationToken)
	{
		var startedAt = DateTimeOffset.UtcNow;

		try
		{
			_logger.LogInformation(
				"Executing schedule {ScheduleId} for worker type {WorkerType} (Frequency: {Frequency})",
				schedule.Id, schedule.WorkerType, schedule.Frequency);

			await using var scope = _scopeFactory.CreateAsyncScope();
			var handler = scope.ServiceProvider
				.GetServices<IScheduleHandler>()
				.FirstOrDefault(h => h.WorkerType == schedule.WorkerType);

			if (handler == null)
			{
				var errorMsg = $"No handler registered for worker type '{schedule.WorkerType}'";
				_logger.LogWarning("No handler registered for worker type {WorkerType}", schedule.WorkerType);

				await _database.LogExecutionAsync(schedule.Id, "Skipped", 0, errorMsg, null, startedAt, cancellationToken);
				return;
			}

			var result = await handler.RegisterItemsAsync(schedule, cancellationToken);

			var status = result.Success ? "Success" : "Failed";
			var error = result.Success ? null : result.Error;

			_logger.LogInformation(
				"Schedule {ScheduleId} executed. Status: {Status}, Items: {ItemCount}, Error: {Error}",
				schedule.Id, status, result.ItemsRegistered, error);

			await _database.LogExecutionAsync(
				schedule.Id,
				status,
				result.ItemsRegistered,
				error,
				handler.GetType().Name,
				startedAt,
				cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			_logger.LogError(
				ex,
				"Exception executing schedule {ScheduleId} for worker type {WorkerType}",
				schedule.Id, schedule.WorkerType);

			try
			{
				await _database.LogExecutionAsync(schedule.Id, "Failed", 0, ex.Message, null, startedAt, cancellationToken);
			}
			catch (Exception logEx)
			{
				_logger.LogError(logEx, "Failed to log execution error for schedule {ScheduleId}", schedule.Id);
			}
		}
	}
}
