namespace WebsiteCollector.Worker;

public sealed record WebsiteSelectorsMonitor(
	Guid Id,
	DateTimeOffset NextRunAt,
	int TimeoutSeconds,
	int MaxAttempts,
	int AttemptCount,
	Guid? LeaseOwner,
	Guid? LeaseToken,
	DateTimeOffset? LeaseUntil,
	DateTimeOffset? LastStartedAt,
	DateTimeOffset? LastSucceededAt,
	string? LastError,
	DateTimeOffset CreatedAt);