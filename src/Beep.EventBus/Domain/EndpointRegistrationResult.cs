namespace Beep.EventBus;

/// <summary>Result of <see cref="IEventBusRepository.RegisterEndpointsAsync"/>.</summary>
/// <param name="Changed">Endpoints added, re-enabled or whose settings changed.</param>
/// <param name="Disabled">Endpoints disabled because their owner no longer lists them.</param>
public sealed record EndpointRegistrationResult(int Changed, int Disabled);
