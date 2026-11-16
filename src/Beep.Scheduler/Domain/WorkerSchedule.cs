namespace Beep.Scheduler.Domain;

/// <summary>
/// Represents a schedule for a worker type
/// </summary>
public class WorkerSchedule
{
	public Guid Id { get; set; }

	/// <summary>
	/// Worker type identifier (e.g., "WebsiteCollector", "BigDataProcess")
	/// </summary>
	public string WorkerType { get; set; } = string.Empty;

	/// <summary>
	/// Human-readable description
	/// </summary>
	public string? Description { get; set; }

	/// <summary>
	/// Execution frequency
	/// </summary>
	public string Frequency { get; set; } = "Daily";

	/// <summary>
	/// Time of day to run (for daily/weekly/monthly)
	/// </summary>
	public TimeSpan StartTime { get; set; }

	/// <summary>
	/// Day of week for weekly schedules (0=Sunday, 6=Saturday)
	/// </summary>
	public int? DayOfWeek { get; set; }

	/// <summary>
	/// Day of month for monthly schedules (1-31)
	/// </summary>
	public int? DayOfMonth { get; set; }

	/// <summary>
	/// Day of year for yearly schedules (1-366, where 1=Jan 1, 32=Feb 1, etc.)
	/// </summary>
	public int? DayOfYear { get; set; }

	/// <summary>
	/// Is this schedule active?
	/// </summary>
	public bool IsActive { get; set; } = true;

	/// <summary>
	/// Maximum concurrent work items to register
	/// </summary>
	public int MaxConcurrentRegistrations { get; set; } = 10;

	/// <summary>
	/// When the schedule was created
	/// </summary>
	public DateTimeOffset CreatedAt { get; set; }

	/// <summary>
	/// When the schedule was last updated
	/// </summary>
	public DateTimeOffset UpdatedAt { get; set; }

	/// <summary>
	/// When the schedule last ran
	/// </summary>
	public DateTimeOffset? LastRunAt { get; set; }
}
