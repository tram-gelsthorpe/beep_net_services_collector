using System;

namespace WorkerFramework.Data;

/// <summary>
/// Single attempt record in a work item's history.
/// </summary>
public record WorkItemAttemptRecord(
	int AttemptNumber,
	DateTimeOffset StartedAt,
	DateTimeOffset? FinishedAt,
	string? Status,
	string? ErrorMessage
);
