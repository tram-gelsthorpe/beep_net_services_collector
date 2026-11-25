namespace Beep.Scheduler;

public interface ISchedulerOption
{
	public string WorkerType { get; }
	public string HandlerType { get; }
	public string Description { get; }
	public string Frequency { get; }
	public TimeOnly? StartTime { get; }
	public int? DayOfWeek { get; }
	public int? DayOfMonth { get; }
	public int? DayOfYear { get; }
	public string? TimeZone { get; }
	public int PollIntervalSeconds { get; }
}

public class SchedulerOption : ISchedulerOption
{
	public const string Name = "Scheduler";

	/// <summary>
	/// ServiceBus worker type (<c>WorkItems.WorkerType</c>) of schedule runs, like the
	/// <c>WorkerType</c> of the other workers. Default: "Scheduler".
	/// </summary>
	public string WorkerType { get; set; } = new SchedulerWorkerOptions().WorkerType;

	/// <summary>
	/// <see cref="IScheduleHandler.HandlerType"/> of the handler this schedule runs, e.g. "WebsiteCollector".
	/// Required; the worker does not start if no registered handler has this type.
	/// </summary>
	public string HandlerType { get; set; } = string.Empty;

	public string Description { get; set; } = string.Empty;
	public string Frequency { get; set; } = string.Empty;
	public TimeOnly? StartTime { get; set; } = null;
	public int? DayOfWeek { get; set; }
	public int? DayOfMonth { get; set; }
	public int? DayOfYear { get; set; }

	/// <summary>
	/// Time zone that <see cref="StartTime"/> and the day settings are in: an IANA id ("Asia/Bangkok")
	/// or a Windows id ("SE Asia Standard Time"). Empty means this machine's local time zone.
	/// </summary>
	public string? TimeZone { get; set; }

	public int PollIntervalSeconds { get; set; } = 30;

	public virtual void Validate()
	{
		if (string.IsNullOrWhiteSpace(WorkerType))
			throw new InvalidOperationException("WorkerType is required.");

		if (string.IsNullOrWhiteSpace(HandlerType))
			throw new InvalidOperationException("HandlerType is required: the name of the schedule handler to run, e.g. \"WebsiteCollector\".");

		try
		{
			ScheduleTimeZone.Resolve(TimeZone);
		}
		catch (ArgumentException ex)
		{
			throw new InvalidOperationException($"TimeZone is invalid: {ex.Message}", ex);
		}

		if (string.IsNullOrWhiteSpace(Frequency))
			throw new InvalidOperationException("Frequency is required.");

		if (StartTime == null)
			throw new InvalidOperationException("StartTime is required.");

		if (Enum.TryParse<ScheduleFrequency>(Frequency, out ScheduleFrequency result))
		{
			switch (result)
			{
				case ScheduleFrequency.Hourly:
				case ScheduleFrequency.Daily:
					break;

				case ScheduleFrequency.Weekly:
					if (DayOfWeek == null || DayOfWeek < 0 || DayOfWeek > 6)
						throw new InvalidOperationException("DayOfWeek must be between 0 (Sunday) and 6 (Saturday) for Weekly frequency.");
					break;

				case ScheduleFrequency.Monthly:
					if (DayOfMonth == null || DayOfMonth < 1 || DayOfMonth > 31)
						throw new InvalidOperationException("DayOfMonth must be between 1 and 31 for Monthly frequency.");
					break;

				case ScheduleFrequency.Yearly:
					if (DayOfYear == null || DayOfYear < 1 || DayOfYear > 366)
						throw new InvalidOperationException("DayOfYear must be between 1 and 366 for Yearly frequency.");
					break;

				default:
					throw new InvalidDataException("Invalid frequency value.");
			}
		}
		else
		{
			throw new InvalidDataException("Invalid frequency value.");
		}
	}
}