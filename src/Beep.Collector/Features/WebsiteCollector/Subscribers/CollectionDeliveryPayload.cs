namespace Beep.Collector;

/// <summary>JSON body POSTed to a selector's DispatchUrl when new data is collected.</summary>
public sealed record CollectionDeliveryPayload(Guid WebsiteSelectorsId, Guid CollectedDataId, DateTimeOffset CollectedAt);
