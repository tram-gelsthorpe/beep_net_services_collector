namespace WebsiteCollector.Worker.Features.WebsiteCollector.Models;

public sealed record WebsiteSelectorsCollectedData(
	Guid Id,
	Guid WebsiteSelectorsExecutionsId,
	byte[] ContentHash,
	string ResponseContent,
	string? ExtractedJson,
	DateTimeOffset CollectedAt);
