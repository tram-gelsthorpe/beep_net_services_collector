namespace Beep.Scheduler;

/// <summary>
/// Information about a scheduled worker
/// </summary>
public class ScheduleInfo
{
	public Guid Id { get; set; }
	public string HandlerType { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string Frequency { get; set; } = string.Empty;
	/// <summary>The local times of day the schedule runs at, earliest first (none for Hourly)</summary>
	public IReadOnlyList<TimeOnly> StartTimes { get; set; } = [];
	/// <summary>Windows time zone id that <see cref="StartTimes"/> are in</summary>
	public string TimeZone { get; set; } = "UTC";
	public bool IsActive { get; set; }
	public DateTimeOffset CreatedAt { get; set; }
	public DateTimeOffset UpdatedAt { get; set; }
	public DateTimeOffset? LastExecutedAt { get; set; }
}
