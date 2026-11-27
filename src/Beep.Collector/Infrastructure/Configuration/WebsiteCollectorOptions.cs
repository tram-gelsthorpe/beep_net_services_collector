using System.Net.Http.Headers;
using Beep.ServiceBus;

namespace Beep.Collector;

/// <summary>
/// Configuration for the website collector worker.
/// </summary>
public sealed class WebsiteCollectorOptions : WorkerOptions
{
	public const string Name = "WebsiteCollector";

	/// <summary>The User-Agent sent when none is configured.</summary>
	public const string DefaultUserAgent = "Beep.Collector/1.0";

	/// <summary>
	/// The User-Agent header sent with every page request (some sites refuse a request without one, or
	/// with one that is not a browser's).
	/// </summary>
	public string UserAgent { get; set; } = DefaultUserAgent;

	public WebsiteCollectorOptions()
	{
		WorkerType = Name;
		MaxConcurrentItems = 16;
		PollIntervalSeconds = 5;
		LeaseSeconds = 600;
		MaxResponseBytes = 2_000_000;
	}

	public override void Validate()
	{
		base.Validate();

		if (string.IsNullOrWhiteSpace(UserAgent) || !new HttpRequestMessage().Headers.UserAgent.TryParseAdd(UserAgent))
			throw new ArgumentException(
				"UserAgent must be a User-Agent header value, e.g. \"Beep.Collector/1.0\"", nameof(UserAgent));
	}
}
