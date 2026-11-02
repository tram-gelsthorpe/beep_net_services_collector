namespace WebsiteCollector.Features.WebsiteCollection.Models;

/// <summary>
/// Represents a registered webhook for collection notifications.
/// </summary>
public record WebsiteSelectionWebhook
{
	public required Guid Id { get; init; }
	public required Guid WebsiteSelectorsId { get; init; }
	public required string WebhookUrl { get; init; }
	public required bool IsEnabled { get; init; }
	public required int RetryAttempts { get; init; }
	public required int MaxRetryAttempts { get; init; }
	public DateTimeOffset? LastCalledAt { get; init; }
	public int? LastStatusCode { get; init; }
	public string? LastErrorMessage { get; init; }
}

/// <summary>
/// Payload sent to webhooks when collection completes.
/// </summary>
public record CollectionNotificationPayload
{
	public required Guid EventId { get; init; }
	public required Guid WebsiteSelectorsId { get; init; }
	public required Guid WebsiteId { get; init; }
	public required bool IsSuccess { get; init; }
	public int? HttpStatusCode { get; init; }
	public string? Error { get; init; }
	public string? ResponseContent { get; init; }
	public string? ExtractedJson { get; init; }
	public string? ContentHashBase64 { get; init; }
	public required DateTimeOffset CollectionStartedAt { get; init; }
	public required DateTimeOffset CollectionFinishedAt { get; init; }
	public required int AttemptNumber { get; init; }
	public required DateTimeOffset NotificationSentAt { get; init; }
}
