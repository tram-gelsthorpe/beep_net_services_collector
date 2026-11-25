using Beep.ServiceBus;

namespace Beep.Scheduler;

/// <summary>
/// Settings for running schedules. Each due schedule becomes a ServiceBus work item of
/// <see cref="WorkerOptions.WorkerType"/>, which <see cref="SchedulerWorker"/> claims and runs.
/// </summary>
public sealed class SchedulerWorkerOptions : WorkerOptions
{
	/// <summary>ServiceBus DomainType of schedule work items (DomainId = Schedules.Id).</summary>
	public const string ScheduleDomainType = "WorkerSchedule";

	public SchedulerWorkerOptions()
	{
		WorkerType = "Scheduler";
		MaxConcurrentItems = 4;     // schedules are few; each handler may itself do a lot of work
		PollIntervalSeconds = 5;
		LeaseSeconds = 300;         // renewed by the heartbeat while a handler runs, so long runs are fine
		MaxResponseBytes = 0;
	}

	/// <summary>
	/// Attempts per run before it is recorded as failed and the schedule waits for its next period.
	/// Default: 3.
	/// </summary>
	public int MaxAttempts { get; set; } = 3;

	public override void Validate()
	{
		base.Validate();
		if (MaxAttempts < 1 || MaxAttempts > 20)
			throw new ArgumentException("MaxAttempts must be between 1 and 20", nameof(MaxAttempts));
	}
}
