namespace WebsiteCollector.Worker;

public sealed record Website(
	Guid Id,
	string Name,
	string Url,
	bool IsEnabled);