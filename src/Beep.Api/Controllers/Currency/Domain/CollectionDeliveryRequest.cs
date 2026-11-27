namespace Beep.Api.Controllers.Currency;

/// <summary>
/// Body POSTed by the Beep.Collector EventBus when a selector's collection is saved.
/// </summary>
public sealed class CollectionDeliveryRequest
{
	public required Guid WebsiteSelectorsId { get; init; }
	public required Guid CollectedDataId { get; init; }
	public DateTimeOffset CollectedAt { get; init; }
}
