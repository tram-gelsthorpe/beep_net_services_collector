using System;
using System.Threading;
using System.Threading.Tasks;

namespace Beep.ServiceBus;

/// <summary>
/// Processor for claimed work items.
/// Implementations handle initialization, processing, and completion of items.
/// </summary>
/// <typeparam name="T">The type of work item to process</typeparam>
public interface IWorkProcessor<T>
{
	/// <summary>
	/// Initializes processing for a claimed item (e.g., mark as started in database).
	/// Called after claiming the item but before actual processing.
	/// </summary>
	/// <param name="item">The claimed item</param>
	/// <param name="leaseToken">The lease token for the item</param>
	/// <param name="workerIdent">The ID of the processing worker</param>
	/// <param name="ct">Cancellation token</param>
	Task InitializeAsync(T item, Guid leaseToken, Guid workerIdent, CancellationToken ct);

	/// <summary>
	/// Processes a single work item.
	/// This is the main work performed by the worker.
	/// </summary>
	/// <param name="item">The claimed item to process</param>
	/// <param name="leaseToken">The lease token for the item</param>
	/// <param name="ct">Cancellation token</param>
	/// <returns>Result of the processing operation</returns>
	Task<ProcessingResult> ProcessAsync(T item, Guid leaseToken, CancellationToken ct);

	/// <summary>
	/// Called after processing completes (success or failure).
	/// </summary>
	/// <param name="item">The processed item</param>
	/// <param name="leaseToken">The lease token for the item</param>
	/// <param name="result">The processing result</param>
	/// <param name="ct">Cancellation token</param>
	Task CompleteAsync(T item, Guid leaseToken, ProcessingResult result, CancellationToken ct);

	/// <summary>
	/// Interval in seconds for renewing the lease during processing.
	/// Typically set to 1/3 of the configured lease duration.
	/// </summary>
	int HeartbeatIntervalSeconds { get; }
}
