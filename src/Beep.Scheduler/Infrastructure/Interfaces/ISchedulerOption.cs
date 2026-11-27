namespace Beep.Scheduler;

public interface ISchedulerOption
{
	public string WorkerType { get; }
	public string HandlerType { get; }
	public string Description { get; }
	public string Frequency { get; }
	public TimeOnly[]? StartTimes { get; }
	public int? DayOfWeek { get; }
	public int? DayOfMonth { get; }
	public int? DayOfYear { get; }
	public string? TimeZone { get; }
	public int PollIntervalSeconds { get; }
}
