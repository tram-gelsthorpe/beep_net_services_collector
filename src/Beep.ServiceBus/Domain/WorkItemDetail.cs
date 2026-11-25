using System;
using System.Collections.Generic;

namespace Beep.ServiceBus;

/// <summary>
/// Detailed information about a work item, including attempt history.
/// </summary>
public record WorkItemDetail(
	Guid Id,
	Guid DomainId,
	string DomainType,
	string Status,
	string WorkerType,
	Guid? WorkerId,
	int AttemptCount,
	int MaxAttempts,
	DateTimeOffset NextRunAt,
	string? LastError,
	DateTimeOffset CreatedAt,
	DateTimeOffset? StartedAt,
	DateTimeOffset? CompletedAt,
	IReadOnlyList<WorkItemAttempt> Attempts
);
