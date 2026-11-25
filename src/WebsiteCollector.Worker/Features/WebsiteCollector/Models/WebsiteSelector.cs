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

/// <summary>
/// An enabled selector as read by the schedule handler: enough to enqueue its collection work
/// item and to register its EventBus endpoint (<paramref name="DispatchUrl"/>, null when none).
/// </summary>
public sealed record ActiveWebsiteSelector(
	Guid Id,
	int MaxAttempts,
	int TimeoutSeconds,
	string? DispatchUrl);

/// <summary>
/// Result of finishing a collection: whether the work item was completed (lease still held)
/// and the Id of the collected data row, if one was saved.
/// </summary>
public sealed record CollectionFinishResult(bool Completed, Guid? CollectedDataId);
