using System;

namespace Beep.ServiceBus;

/// <summary>
/// Provides access to the current worker instance's ID across the application.
/// This allows services to access the worker ID without direct dependency on the concrete worker class.
/// </summary>
public interface IWorkerIdProvider
{
	/// <summary>
	/// Gets or sets the unique ID of the worker instance.
	/// </summary>
	Guid WorkerId { get; set; }

	/// <summary>
	/// Gets a value indicating whether the worker ID has been initialized.
	/// </summary>
	bool IsInitialized { get; }
}

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
