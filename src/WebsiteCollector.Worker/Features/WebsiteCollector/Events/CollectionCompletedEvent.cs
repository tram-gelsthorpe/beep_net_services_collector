using Beep.EventBus;

namespace WebsiteCollector.Worker.Features.WebsiteCollector.Events;

/// <summary>
/// Event published when a website collection/selection completes.
/// Carries all relevant data for subscribers to process.
/// </summary>
public sealed class CollectionCompletedEvent : DomainEvent
{
	public override string EventType => nameof(CollectionCompletedEvent);

	/// <summary>
	/// The website selector that was just completed.
	/// </summary>
	public required Guid WebsiteSelectorsId { get; init; }

	/// <summary>
	/// The parent website.
	/// </summary>
	public required Guid WebsiteId { get; init; }

	/// <summary>
	/// The collected data ID for this collection (references WebsiteSelectorsCollectedData).
	/// Used to queue webhook deliveries with the collected data.
	/// NOTE: This will be populated by the repository/service after data is collected.
	/// </summary>
	public Guid CollectedDataId { get; init; }

	/// <summary>
	/// Whether the collection succeeded.
	/// </summary>
	public required bool IsSuccess { get; init; }

	/// <summary>
	/// HTTP status code if applicable.
	/// </summary>
	public int? HttpStatusCode { get; init; }

	/// <summary>
	/// Error message if the collection failed.
	/// </summary>
	public string? Error { get; init; }

	/// <summary>
	/// The raw response content collected.
	/// </summary>
	public string? ResponseContent { get; init; }

	/// <summary>
	/// The extracted JSON data.
	/// </summary>
	public string? ExtractedJson { get; init; }

	/// <summary>
	/// Hash of the content for deduplication.
	/// </summary>
	public byte[]? ContentHash { get; init; }

	/// <summary>
	/// When the collection started.
	/// </summary>
	public required DateTimeOffset CollectionStartedAt { get; init; }

	/// <summary>
	/// When the collection finished.
	/// </summary>
	public required DateTimeOffset CollectionFinishedAt { get; init; }

	/// <summary>
	/// Attempt number for this collection.
	/// </summary>
	public required int AttemptNumber { get; init; }
}
