using System;

namespace Beep.ServiceBus;

/// <summary>
/// Default implementation that holds and provides the worker ID.
/// Must be initialized by the worker with its actual WorkerId from GenericWorkerBase.
/// </summary>
public class WorkerIdProvider : IWorkerIdProvider
{
	/// <summary>
	/// Gets or sets the unique ID of the worker instance.
	/// </summary>
	public Guid WorkerId { get; set; } = Guid.Empty;

	/// <summary>
	/// Gets a value indicating whether the worker ID has been initialized (not Guid.Empty).
	/// </summary>
	public bool IsInitialized => WorkerId != Guid.Empty;
}
