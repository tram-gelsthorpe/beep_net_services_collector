using WorkerFramework.Configuration;

namespace WebsiteCollector.Worker.Infrastructure.Configuration;

/// <summary>
/// Configuration for the website collector worker.
/// Extends the base WorkerOptions with collector-specific settings.
/// </summary>
public sealed class WebsiteCollectorOptions : WorkerOptions
{
	public const string Name = "WebsiteCollector";

	public WebsiteCollectorOptions()
	{
		// Collectors can run longer than webhooks (complex data extraction)
		// Use defaults from WorkerOptions
		MaxConcurrentItems = 16;
		PollIntervalSeconds = 5;
		LeaseSeconds = 600;
		MaxResponseBytes = 2_000_000;
	}

	public override void Validate()
	{
		base.Validate();
	}
}
