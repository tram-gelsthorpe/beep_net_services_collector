using Beep.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Beep.Scheduler;

/// <summary>
/// Runs one schedule work item: checks the schedule is still due, runs its
/// <see cref="IScheduleHandler"/> in a fresh DI scope, records the execution and completes the
/// work item. Claiming, lease renewal, crash recovery and retries are done by the ServiceBus.
/// </summary>
public sealed class SchedulerWorkProcessor : IWorkProcessor<IClaimedWorkItem>
{
	private readonly SchedulerRepository _database;
	private readonly IWorkItemRepository _workItems;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger<SchedulerWorkProcessor> _logger;

	public int HeartbeatIntervalSeconds { get; }

	public SchedulerWorkProcessor(
		SchedulerRepository database,
		IWorkItemRepository workItems,
		IServiceScopeFactory scopeFactory,
		SchedulerWorkerOptions options,
		ILogger<SchedulerWorkProcessor> logger)
	{
		_database = database ?? throw new ArgumentNullException(nameof(database));
		_workItems = workItems ?? throw new ArgumentNullException(nameof(workItems));
		_scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		ArgumentNullException.ThrowIfNull(options);
		HeartbeatIntervalSeconds = Math.Max(10, options.LeaseSeconds / 3);
	}

	public Task InitializeAsync(IClaimedWorkItem item, Guid leaseToken, Guid workerId, CancellationToken ct) =>
		Task.CompletedTask;

	public async Task<ProcessingResult> ProcessAsync(IClaimedWorkItem item, Guid leaseToken, CancellationToken ct)
	{
		if (item.DomainType != SchedulerWorkerOptions.ScheduleDomainType)
			return Failed($"Unexpected domain type '{item.DomainType}'");

		// A work item can outlive its schedule's due window, e.g. one queued by another instance just
		// before this run was recorded. Checking again makes such a duplicate a no-op.
		var startedAt = await _database.CheckDueAsync(item.DomainId, ct);
		var schedule = startedAt is null ? null : await _database.GetScheduleAsync(item.DomainId, ct);
		if (startedAt is null || schedule is null)
		{
			_logger.LogInformation("Schedule {ScheduleId} is no longer due; skipping work item {WorkItemId}", item.DomainId, item.WorkItemId);
			return new ProcessingResult { Success = true };
		}

		_logger.LogInformation(
			"Executing schedule {ScheduleId} for worker type {WorkerType} (Frequency: {Frequency}, attempt {Attempt} of {MaxAttempts})",
			schedule.Id, schedule.WorkerType, schedule.Frequency, item.AttemptNumber, item.MaxAttempts);

		await using var scope = _scopeFactory.CreateAsyncScope();
		var handler = scope.ServiceProvider
			.GetServices<IScheduleHandler>()
			.FirstOrDefault(h => h.WorkerType == schedule.WorkerType);

		if (handler is null)
		{
			var error = $"No handler registered for worker type '{schedule.WorkerType}'";
			_logger.LogWarning("No handler registered for worker type {WorkerType}", schedule.WorkerType);
			await _database.LogExecutionAsync(schedule.Id, "Skipped", 0, error, null, startedAt.Value, ct);
			return new ProcessingResult { Success = true };
		}

		ScheduleHandlerResult result;
		try
		{
			result = await handler.RegisterItemsAsync(schedule, ct);
		}
		catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
		{
			_logger.LogError(ex, "Schedule handler {Handler} failed for schedule {ScheduleId}", handler.GetType().Name, schedule.Id);
			result = new ScheduleHandlerResult { Success = false, Error = ex.Message };
		}

		// A failed attempt is retried by the ServiceBus; only the last one finishes the run, so the
		// schedule then waits for its next period instead of being queued again straight away.
		var lastAttempt = item.AttemptNumber >= item.MaxAttempts;
		var status = result.Success ? "Success" : lastAttempt ? "Failed" : "Retrying";

		_logger.LogInformation(
			"Schedule {ScheduleId} executed. Status: {Status}, Items: {ItemCount}, Error: {Error}",
			schedule.Id, status, result.ItemsRegistered, result.Error);

		await _database.LogExecutionAsync(
			schedule.Id,
			status,
			result.ItemsRegistered,
			result.Success ? null : result.Error,
			handler.GetType().Name,
			startedAt.Value,
			ct,
			completesRun: result.Success || lastAttempt);

		return result.Success ? new ProcessingResult { Success = true } : Failed(result.Error ?? "Schedule handler failed");
	}

	public async Task CompleteAsync(IClaimedWorkItem item, Guid leaseToken, ProcessingResult result, CancellationToken ct)
	{
		var completed = await _workItems.CompleteAsync(
			item.WorkItemId, leaseToken, result.Success, result.ErrorMessage, cancellationToken: ct);

		if (!completed)
			_logger.LogWarning("Lease on schedule work item {WorkItemId} was lost before it could be completed", item.WorkItemId);
	}

	private static ProcessingResult Failed(string error) =>
		new() { Success = false, ErrorMessage = error.Length > 2000 ? error[..2000] : error };
}
