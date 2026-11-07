namespace Beep.ServiceBus.Configuration;

/// <summary>
/// Defines the configuration contract for any GenericWorkerBase worker.
/// Implementations provide the settings needed by the generic worker framework.
/// </summary>
public interface IWorkerOption
{
	/// <summary>
	/// Maximum number of items to process concurrently.
	/// Be conservative with this on constrained systems (network, memory).
	/// </summary>
	int MaxConcurrentItems { get; }

	/// <summary>
	/// How often (in seconds) to poll the queue for unclaimed items.
	/// Lower values = more responsive, higher = less DB load.
	/// Typical: 2-10 seconds
	/// </summary>
	int PollIntervalSeconds { get; }

	/// <summary>
	/// Duration (in seconds) that a claimed item stays locked to this worker.
	/// If the worker crashes, after this time another worker can reclaim it.
	/// Must be longer than expected processing time.
	/// Typical: 300-1800 seconds (5-30 min)
	/// </summary>
	int LeaseSeconds { get; }

	/// <summary>
	/// Optional: Maximum response size (in bytes) for HTTP operations.
	/// Set to 0 to disable size checks.
	/// </summary>
	long MaxResponseBytes { get; }
}
