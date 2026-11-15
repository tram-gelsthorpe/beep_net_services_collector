namespace WebsiteCollector.Worker;

public sealed record WebsiteSelector(
	Guid Id,
	Guid WebsitesId,
	string CollectionUrl,
	string CollectionType,
	string? ContentSelector,
	string? TableSelector,
	int TimeoutSeconds,
	bool IsEnabled);