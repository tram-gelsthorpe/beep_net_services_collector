namespace Beep.EventBus;

/// <summary>
/// Result of registering EventBus endpoints.
/// </summary>
public sealed class EventBusRegister
{
	/// <summary>
	/// Number of items registered/created
	/// </summary>
	public int ItemsRegistered { get; set; }

	/// <summary>
	/// Whether the registration succeeded
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
