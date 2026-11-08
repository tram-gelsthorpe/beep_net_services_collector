namespace WebsiteCollector.Worker.Features.WebsiteCollector.Models;

public sealed record WebsiteSelector(
	Guid Id,
	Guid WebsitesId,
	string CollectionUrl,
	string CollectionType,
	string? ContentSelector,
	string? TableSelector,
	int IntervalSeconds,
	bool IsEnabled);