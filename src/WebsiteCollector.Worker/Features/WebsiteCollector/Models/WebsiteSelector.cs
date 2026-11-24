namespace WebsiteCollector.Worker;

public sealed record WebsiteSelector(
	Guid Id,
	Guid WebsitesId,
	string CollectionUrl,
	string CollectionType,
	string? ContentSelector,
	int TimeoutSeconds,
	int MaxAttempts,
	bool IsEnabled);

public sealed record ActiveWebsiteSelector(
	Guid Id, 
	int MaxAttempts);	

public sealed record DispatchWebsiteSelector(
	Guid Id, 
	string DispatchUrl, 
	int MaxAttempts, 
	int TimeoutSeconds);