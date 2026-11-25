using Beep.ServiceBus;

namespace Beep.Collector;

/// <summary>
/// Configuration for the website collector worker.
/// </summary>
public sealed class WebsiteCollectorOptions : WorkerOptions
{
	public const string Name = "WebsiteCollector";

	public WebsiteCollectorOptions()
	{
		WorkerType = Name;
		MaxConcurrentItems = 16;
		PollIntervalSeconds = 5;
		LeaseSeconds = 600;
		MaxResponseBytes = 2_000_000;
	}
}
