namespace WebsiteCollector.Worker;

public sealed record ClaimedWebsiteSelector(
	Guid WebsiteSelectorsId,
	Guid WebsitesId,
	string CollectionUrl,
	string CollectionType,
	string? ContentSelector,
	string? TableSelector,
	int TimeoutSeconds,
	int MaxAttempts,
	int AttemptCount,
	Guid LeaseToken);
