using System;
using System.Linq;
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

	/// <summary>
	/// Optional API key sent with deliveries in <see cref="ApiKeyHeader"/>. A secret: set it in user
	/// secrets, an environment variable (<c>EventBus__ApiKey</c>) or Key Vault, not in appsettings.json.
	/// </summary>
	public string? ApiKey { get; set; }

	/// <summary>Header that carries <see cref="ApiKey"/>.</summary>
	public string ApiKeyHeader { get; set; } = "X-API-Key";

	/// <summary>
	/// Origins (scheme, host and port, e.g. "https://api.example.com") that receive
	/// <see cref="ApiKey"/>. Deliveries to any other URL are sent without it, so a selector's
	/// DispatchUrl cannot send the key to another site. Required when ApiKey is set.
	/// </summary>
	public string[] ApiKeyOrigins { get; set; } = [];

	public override void Validate()
	{
		base.Validate();

		if (string.IsNullOrWhiteSpace(ApiKey))
			return;

		if (string.IsNullOrWhiteSpace(ApiKeyHeader))
			throw new ArgumentException("ApiKeyHeader must be specified when ApiKey is set", nameof(ApiKeyHeader));

		if (ApiKeyOrigins.Length == 0)
			throw new ArgumentException(
				"ApiKeyOrigins must list the origins that may receive ApiKey (e.g. \"https://localhost:55281\")",
				nameof(ApiKeyOrigins));

		foreach (var origin in ApiKeyOrigins)
			if (NormalizeOrigin(origin) is null)
				throw new ArgumentException(
					$"ApiKeyOrigins entry '{origin}' is not an absolute http(s) URL", nameof(ApiKeyOrigins));
	}

	/// <summary>True when <paramref name="url"/> is on one of <see cref="ApiKeyOrigins"/>.</summary>
	public bool SendsApiKeyTo(string url) =>
		!string.IsNullOrWhiteSpace(ApiKey)
		&& NormalizeOrigin(url) is { } origin
		&& ApiKeyOrigins.Any(o => string.Equals(NormalizeOrigin(o), origin, StringComparison.OrdinalIgnoreCase));

	// "https://Host:443/path" -> "https://host" (default ports dropped), so equal origins compare equal.
	private static string? NormalizeOrigin(string? url) =>
		Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
			? uri.GetLeftPart(UriPartial.Authority)
			: null;
}
