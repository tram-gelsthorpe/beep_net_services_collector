using WorkerFramework.Configuration;

namespace WebsiteCollector.Worker.Infrastructure.Configuration;

/// <summary>
/// Configuration for the website collector worker.
/// Extends the base WorkerOptions with collector-specific settings.
/// </summary>
public sealed class WebsiteCollectorOptions : WorkerOptions
{
	/// <summary>
	/// HTTPS hosts that are allowed to be collected.
	/// URLs with other hosts are rejected. Empty array = no hosts allowed.
	/// </summary>
	public string[] AllowedHosts { get; set; } = [];

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

		if (AllowedHosts.Length == 0)
			throw new InvalidOperationException("AllowedHosts cannot be empty");
	}
}
