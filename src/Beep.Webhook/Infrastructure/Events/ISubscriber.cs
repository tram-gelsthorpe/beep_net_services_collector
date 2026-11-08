using System.Threading;
using System.Threading.Tasks;

namespace Beep.EventBus.Infrastructure.Events;

/// <summary>
/// Contract for components that subscribe to domain events.
/// Implementers handle side effects when events are published.
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
