namespace Beep.ServiceBus;

/// <summary>
/// Helper extensions for status handling.
/// </summary>
public static class WorkItemStatusExtensions
{
	/// <summary>
	/// Returns true if the status indicates the item is ready to be claimed.
	/// </summary>
	public static bool IsReadyToClaim(this WorkItemStatus status)
	{
		return status is WorkItemStatus.Pending or WorkItemStatus.Failed;
	}

	/// <summary>
	/// Returns true if the status is a terminal state (no more processing will occur).
	/// </summary>
	public static bool IsTerminal(this WorkItemStatus status)
	{
		return status is WorkItemStatus.Succeeded or WorkItemStatus.Abandoned;
	}

	/// <summary>
	/// Returns true if the status indicates an active lease.
	/// </summary>
	public static bool HasActiveLease(this WorkItemStatus status)
	{
		return status is WorkItemStatus.Claimed or WorkItemStatus.Running;
	}

	/// <summary>
	/// Returns the status name as used in stored procedures.
	/// </summary>
	public static string ToStatusName(this WorkItemStatus status)
	{
		return status switch
		{
			WorkItemStatus.Pending => "PENDING",
			WorkItemStatus.Claimed => "CLAIMED",
			WorkItemStatus.Running => "RUNNING",
			WorkItemStatus.Succeeded => "SUCCEEDED",
			WorkItemStatus.Failed => "FAILED",
			WorkItemStatus.Abandoned => "ABANDONED",
			WorkItemStatus.Deferred => "DEFERRED",
			_ => "UNKNOWN"
		};
	}

	/// <summary>
	/// Parses a status name from stored procedure results.
	/// </summary>
	public static WorkItemStatus? TryParseStatusName(string? statusName)
	{
		return statusName?.ToUpperInvariant() switch
		{
			"PENDING" => WorkItemStatus.Pending,
			"CLAIMED" => WorkItemStatus.Claimed,
			"RUNNING" => WorkItemStatus.Running,
			"SUCCEEDED" => WorkItemStatus.Succeeded,
			"FAILED" => WorkItemStatus.Failed,
			"ABANDONED" => WorkItemStatus.Abandoned,
			"DEFERRED" => WorkItemStatus.Deferred,
			_ => null
		};
	}
}
