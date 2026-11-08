namespace WebsiteCollector.Worker.Features.WebsiteCollector.Models;

public sealed record Website(
	Guid Id,
	string Name,
	string Url,
	bool IsEnabled);