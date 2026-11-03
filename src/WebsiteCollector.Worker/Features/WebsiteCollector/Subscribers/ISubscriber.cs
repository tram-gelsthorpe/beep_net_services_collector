namespace WebsiteCollector.Worker.Features.WebsiteCollector.Subscribers;

/// <summary>
/// Contract for components that subscribe to collection events.
/// Implementers handle side effects when collections complete.
/// </summary>
public interface ISubscriber
{
	/// <summary>
	/// Name of this subscriber for logging and diagnostics.
	/// </summary>
	string Name { get; }

	/// <summary>
	/// Called during application startup to register event handlers.
	/// </summary>
	Task RegisterAsync(CancellationToken ct = default);
}
