namespace Beep.Scheduler;

public interface ISchedulerOption
{
	public string WorkerType { get; }
	public string Description { get; }
	public string Frequency { get; }
	public TimeOnly? StartTime { get; }
	public int? DayOfWeek { get; }
	public int? DayOfMonth { get; }
	public int? DayOfYear { get; }
	public int PollIntervalSeconds { get; }
}

public class SchedulerOption : ISchedulerOption
{
	public const string Name = "Scheduler";

	public string WorkerType { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string Frequency { get; set; } = string.Empty;
	public TimeOnly? StartTime { get; set; } = null;
	public int? DayOfWeek { get; set; }
	public int? DayOfMonth { get; set; }
	public int? DayOfYear { get; set; }
	public int PollIntervalSeconds { get; set; } = 30;

	public virtual void Validate()
	{
		if (string.IsNullOrWhiteSpace(WorkerType))
			throw new InvalidOperationException("WorkerType is required.");

		if (string.IsNullOrWhiteSpace(Frequency))
			throw new InvalidOperationException("Frequency is required.");

		if (StartTime == null)
			throw new InvalidOperationException("StartTime is required.");

		if (Enum.TryParse<ScheduleFrequency>(Frequency, out ScheduleFrequency result))
		{
			switch (result)
			{
				case ScheduleFrequency.Daily:
					if (StartTime == null)
						throw new InvalidOperationException("StartTime is required for Daily frequency.");
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