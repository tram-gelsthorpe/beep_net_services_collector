namespace WebsiteCollector.Features.WebsiteCollection.Models;

public sealed record Website(
	Guid Id,
	string Name,
	string Url,
	bool IsEnabled);

public sealed record WebsiteSelector(
	Guid Id,
	Guid WebsitesId,
	string CollectionUrl,
	string CollectionType,
	string? ContentSelector,
	string? TableSelector,
	int IntervalSeconds,
	bool IsEnabled);

public sealed record WebsiteSelectorsMonitor(
	Guid Id,
	DateTimeOffset NextRunAt,
	int TimeoutSeconds,
	int MaxAttempts,
	int AttemptCount,
	Guid? LeaseOwner,
	Guid? LeaseToken,
	DateTimeOffset? LeaseUntil,
	DateTimeOffset? LastStartedAt,
	DateTimeOffset? LastSucceededAt,
	string? LastError,
	DateTimeOffset CreatedAt);

public sealed record ClaimedWebsiteSelector(
	Guid WebsiteSelectorsId,
	Guid WebsitesId,
	string CollectionUrl,
	string CollectionType,
	string? ContentSelector,
	string? TableSelector,
	int IntervalSeconds,
	int TimeoutSeconds,
	int MaxAttempts,
	int AttemptCount,
	Guid LeaseToken);

public sealed record WebsiteExecution(
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

public sealed record WebsiteSelectorsCollectedData(
	Guid Id,
	Guid WebsiteSelectorsExecutionsId,
	byte[] ContentHash,
	string ResponseContent,
	string? ExtractedJson,
	DateTimeOffset CollectedAt);
