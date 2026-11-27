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
