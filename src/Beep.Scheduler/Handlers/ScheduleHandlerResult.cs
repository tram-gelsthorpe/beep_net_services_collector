namespace Beep.Scheduler;

/// <summary>
/// Represents the result of a schedule handler execution
/// </summary>
public class ScheduleHandlerResult
{
	/// <summary>
	/// Number of items registered/created
	/// </summary>
	public int ItemsRegistered { get; set; }

	/// <summary>
	/// Whether the handler succeeded
	/// </summary>
	public bool Success { get; set; } = true;

	/// <summary>
	/// Error message if execution failed
	/// </summary>
	public string? Error { get; set; }

	/// <summary>
	/// Optional additional details
	/// </summary>
	public string? Details { get; set; }
}
