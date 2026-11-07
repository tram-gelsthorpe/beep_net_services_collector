namespace Beep.ServiceBus.Domain;

/// <summary>
/// Standard status values for any work item in the WorkerFramework.
/// All workers use these same statuses; no domain-specific status variants.
/// </summary>
public enum WorkItemStatus
{
	/// <summary>
	/// Waiting to be claimed by a worker.
	/// Initial state for new items; also used for items after FAILED status when backoff expires.
	/// </summary>
	Pending = 1,

	/// <summary>
	/// Actively leased by a worker (lease token assigned, lease time not expired).
	/// Worker has exclusive access; other workers cannot claim until lease expires.
	/// </summary>
	Claimed = 2,

	/// <summary>
	/// Currently being processed by the worker.
	/// Intermediate state; helps distinguish "claimed but not started" from "claimed and running".
	/// </summary>
	Running = 3,

	/// <summary>
	/// Completed successfully; no more retries needed.
	/// Item will not be re-processed unless manually reset.
	/// </summary>
	Succeeded = 4,

	/// <summary>
	/// Last attempt failed, but more retries remain.
	/// After backoff period, item will return to PENDING for a new claim attempt.
	/// </summary>
	Failed = 5,

	/// <summary>
	/// Exceeded maximum retry attempts or marked unrecoverable.
	/// Item will not be retried; terminal state that indicates permanent failure.
	/// </summary>
	Abandoned = 6,

	/// <summary>
	/// Temporarily paused (e.g., circuit breaker open, quota exceeded, dependency unavailable).
	/// Item is not competed but should not be claimed; can be manually resumed.
	/// </summary>
	Deferred = 7
}

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
