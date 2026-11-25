using Beep.ServiceBus;

namespace Beep.EventBus;

/// <summary>
/// Configuration options for the EventBus delivery worker.
/// Deliveries are typically fast, so defaults favour quick polling and short leases.
/// </summary>
public sealed class EventBusOptions : WorkerOptions
{
	public const string Name = "EventBus";

	public EventBusOptions()
	{
		WorkerType = Name;
		MaxConcurrentItems = 20;
		PollIntervalSeconds = 2;
		LeaseSeconds = 300;
		MaxResponseBytes = 0; // Delivery responses are small; disable the size check
	}
}
