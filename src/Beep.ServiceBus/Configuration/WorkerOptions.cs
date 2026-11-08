using System;

namespace Beep.ServiceBus;

/// <summary>
/// Default implementation of IWorkerConfiguration.
/// Provides standard worker framework settings for any GenericWorkerBase worker.
/// </summary>
public class WorkerOptions : IWorkerOption
{
	/// <summary>
	/// Maximum number of items to process concurrently.
	/// Default: 10. Range: 1-256
	/// </summary>
	public int MaxConcurrentItems { get; set; } = 10;

	/// <summary>
	/// How often (in seconds) to poll the queue for unclaimed items.
	/// Default: 5 seconds
	/// </summary>
	public int PollIntervalSeconds { get; set; } = 5;

	/// <summary>
	/// Duration (in seconds) that a claimed item stays locked to this worker.
	/// Default: 600 seconds (10 minutes)
	/// </summary>
	public int LeaseSeconds { get; set; } = 600;

	/// <summary>
	/// Maximum response size (in bytes) for HTTP operations.
	/// Default: 2MB (2,000,000 bytes). Set to 0 to disable size checks.
	/// </summary>
	public long MaxResponseBytes { get; set; } = 2_000_000;

	/// <summary>
	/// Validates that settings are within acceptable ranges.
	/// </summary>
	public virtual void Validate()
	{
		if (MaxConcurrentItems < 1 || MaxConcurrentItems > 256)
			throw new ArgumentException("MaxConcurrentItems must be between 1 and 256", nameof(MaxConcurrentItems));

		if (PollIntervalSeconds < 1)
			throw new ArgumentException("PollIntervalSeconds must be at least 1", nameof(PollIntervalSeconds));

		if (LeaseSeconds < 60 || LeaseSeconds > 3600)
			throw new ArgumentException("LeaseSeconds must be between 60 and 3600 seconds", nameof(LeaseSeconds));

		if (MaxResponseBytes < 0)
			throw new ArgumentException("MaxResponseBytes cannot be negative", nameof(MaxResponseBytes));
	}
}
