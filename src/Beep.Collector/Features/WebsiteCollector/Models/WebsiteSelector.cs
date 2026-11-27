namespace Beep.Collector;

public sealed record WebsiteSelector(
	Guid Id,
	Guid WebsitesId,
	string CollectionUrl,
	string CollectionType,
	string? ContentSelector,
	int TimeoutSeconds,
	int MaxAttempts,
	bool IsEnabled);
