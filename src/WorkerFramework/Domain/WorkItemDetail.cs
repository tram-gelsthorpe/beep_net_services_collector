using System;
using System.Collections.Generic;

namespace WorkerFramework.Domain;

/// <summary>
/// Detailed information about a work item, including attempt history.
/// </summary>
public record WorkItemDetail(
	Guid Id,
	string WorkerType,
	Guid DomainId,
	string DomainType,
	string Status,
	Guid? LeaseOwner,
	int AttemptCount,
	int MaxAttempts,
	DateTimeOffset NextRunAt,
	string? LastError,
	DateTimeOffset CreatedAt,
	DateTimeOffset? StartedAt,
	DateTimeOffset? CompletedAt,
	IReadOnlyList<WorkItemAttemptRecord> Attempts
);
