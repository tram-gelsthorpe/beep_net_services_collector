namespace WebsiteCollector.Worker;

public sealed record ClaimedWebsiteSelector(
	Guid WebsiteSelectorsId,
	Guid WebsitesId,
	string CollectionUrl,
	string CollectionType,
	string? ContentSelector,
	string? TableSelector,
	int IntervalSeconds,
	int TimeoutSeconds,
	int MaxAttempts,
	int AttemptCount,
	Guid LeaseToken);
