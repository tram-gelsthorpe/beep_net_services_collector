namespace Beep.Collector;

public sealed record ClaimedWebsiteSelector(
	Guid WorkItemId,
	Guid DomainId,
	Guid WebsitesId,
	string CollectionUrl,
	string CollectionType,
	string? ContentSelector,
	int TimeoutSeconds,
	int MaxAttempts,
	int AttemptCount,
	Guid LeaseToken);
