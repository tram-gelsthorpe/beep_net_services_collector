namespace Beep.Scheduler;

/// <summary>
/// Service for managing worker schedules dynamically at runtime
/// </summary>
public interface IScheduleService
{
	/// <summary>
	/// Create or update a schedule for a worker type
	/// </summary>
	/// <param name="workerType">Unique identifier for the worker type</param>
	/// <param name="description">Human-readable description of the schedule</param>
	/// <param name="frequency">Schedule frequency (Daily, Weekly, Monthly, Hourly, Yearly)</param>
	/// <param name="startTime">Local time of day to run the schedule, in <paramref name="timeZone"/></param>
	/// <param name="isActive">Whether the schedule should be active</param>
	/// <param name="timeZone">
	/// IANA ("Asia/Bangkok") or Windows ("SE Asia Standard Time") time zone id; null or empty means
	/// this machine's local time zone
	/// </param>
	/// <param name="dayOfWeek">Weekly: 0 (Sunday) to 6 (Saturday)</param>
	/// <param name="dayOfMonth">Monthly: 1 to 31</param>
	/// <param name="dayOfYear">Yearly: 1 to 366</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>The ID of the created/updated schedule</returns>
	Task<int> CreateOrUpdateScheduleAsync(
		string workerType,
		string description,
		string frequency,
		TimeOnly startTime,
		bool isActive = true,
		string? timeZone = null,
		int? dayOfWeek = null,
		int? dayOfMonth = null,
		int? dayOfYear = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Check if a schedule exists for a worker type
	/// </summary>
	/// <param name="workerType">Worker type to check</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>True if schedule exists, false otherwise</returns>
	Task<bool> ScheduleExistsAsync(string workerType, CancellationToken cancellationToken = default);

	/// <summary>
	/// Get a schedule by worker type
	/// </summary>
	/// <param name="workerType">Worker type to retrieve</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>Schedule details or null if not found</returns>
	Task<ScheduleInfo?> GetScheduleAsync(string workerType, CancellationToken cancellationToken = default);

	/// <summary>
	/// Update the active status of a schedule
	/// </summary>
	/// <param name="workerType">Worker type</param>
	/// <param name="isActive">New active status</param>
	/// <param name="cancellationToken">Cancellation token</param>
	Task UpdateScheduleStatusAsync(string workerType, bool isActive, CancellationToken cancellationToken = default);

	/// <summary>
	/// Delete a schedule
	/// </summary>
	/// <param name="workerType">Worker type to delete</param>
	/// <param name="cancellationToken">Cancellation token</param>
	Task<bool> DeleteScheduleAsync(string workerType, CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about a scheduled worker
/// </summary>
public class ScheduleInfo
{
	public Guid Id { get; set; }
	public string WorkerType { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string Frequency { get; set; } = string.Empty;
	public TimeOnly StartTime { get; set; }
	/// <summary>Windows time zone id that <see cref="StartTime"/> is in</summary>
	public string TimeZone { get; set; } = "UTC";
	public bool IsActive { get; set; }
	public DateTimeOffset CreatedAt { get; set; }
	public DateTimeOffset UpdatedAt { get; set; }
	public DateTimeOffset? LastExecutedAt { get; set; }
}
