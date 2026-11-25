using System;

namespace Beep.EventBus;

/// <summary>
/// Where events published for an owner are delivered. Passed to
/// <see cref="IEventBusRepository.RegisterEndpointsAsync"/>.
/// </summary>
/// <param name="OwnerId">The publisher-side entity the events belong to (e.g. a website selector).</param>
/// <param name="DeliveryUrl">
/// Absolute http(s) URL the payload is POSTed to. <c>null</c> (or an invalid URL) means the owner
/// has no endpoint, so any endpoint previously registered for it is disabled.
/// </param>
/// <param name="TimeoutSeconds">HTTP timeout per attempt (clamped to 5-300).</param>
/// <param name="MaxAttempts">Attempts before the delivery is abandoned (clamped to 1-10).</param>
public sealed record EventBusEndpoint(Guid OwnerId, string? DeliveryUrl, int TimeoutSeconds = 30, int MaxAttempts = 3);

/// <summary>Result of <see cref="IEventBusRepository.RegisterEndpointsAsync"/>.</summary>
/// <param name="Changed">Endpoints added, re-enabled or whose settings changed.</param>
/// <param name="Disabled">Endpoints disabled because their owner no longer lists them.</param>
public sealed record EndpointRegistrationResult(int Changed, int Disabled);
