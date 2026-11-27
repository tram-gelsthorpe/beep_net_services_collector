namespace Beep.Collector;

/// <summary>
/// An enabled selector as read by the schedule handler: enough to enqueue its collection work
/// item and to register its EventBus endpoint (<paramref name="DispatchUrl"/>, null when none).
/// </summary>
public sealed record ActiveWebsiteSelector(
	Guid Id,
	int MaxAttempts,
	int TimeoutSeconds,
	string? DispatchUrl);
