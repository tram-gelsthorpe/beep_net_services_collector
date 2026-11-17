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
	/// <param name="startTime">Time of day to run the schedule</param>
	/// <param name="isActive">Whether the schedule should be active</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>The ID of the created/updated schedule</returns>
	Task<int> CreateOrUpdateScheduleAsync(
		string workerType,
		string description,
		string frequency,
		TimeOnly startTime,
		bool isActive = true,
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
	public int Id { get; set; }
	public string WorkerType { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string Frequency { get; set; } = string.Empty;
	public TimeOnly StartTime { get; set; }
	public bool IsActive { get; set; }
	public DateTime CreatedAt { get; set; }
	public DateTime? UpdatedAt { get; set; }
	public DateTime? LastExecutedAt { get; set; }
}
