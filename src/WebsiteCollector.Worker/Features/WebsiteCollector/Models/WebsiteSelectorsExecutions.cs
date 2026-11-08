namespace WebsiteCollector.Worker.Features.WebsiteCollector.Models;

public sealed record WebsiteSelectorsExecutions(
	Guid Id,
	Guid WebsiteSelectorsId,
	Guid LeaseToken,
	Guid WorkerId,
	DateTimeOffset StartedAt,
	DateTimeOffset? FinishedAt,
	string Status,
	int AttemptNumber,
	int? HttpStatus,
	string? ErrorMessage);
