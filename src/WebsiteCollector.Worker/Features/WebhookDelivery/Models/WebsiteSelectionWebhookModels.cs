namespace WebsiteCollector.Worker;

/// <summary>
/// Represents a webhook configuration for a website selector.
/// </summary>
public sealed record WebsiteSelectionWebhook(
	Guid Id,
	Guid WebsiteSelectorsId,
	string WebhookUrl,
	bool IsEnabled,
	int RetryAttempts,
	int MaxRetryAttempts,
	DateTime? LastCalledAt,
	int? LastStatusCode,
	string? LastErrorMessage);
