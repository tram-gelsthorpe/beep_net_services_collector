using System;

namespace Beep.ServiceBus;

/// <summary>
/// Single attempt record in a work item's history.
/// </summary>
public record WorkItemAttempt(
	int AttemptNumber,
	DateTimeOffset StartedAt,
	DateTimeOffset? FinishedAt,
	string? Status,
	string? ErrorMessage
);
