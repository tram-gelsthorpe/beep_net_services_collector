using Beep.ServiceBus.Configuration;

namespace WebsiteCollector.Worker.Infrastructure.Configuration;

/// <summary>
/// Configuration options for webhook delivery worker.
/// Webhook deliveries are typically fast, so defaults are optimized for quick processing.
/// </summary>
public sealed class WebhookDeliveryOptions : WorkerOptions
{
	public const string Name = "WebhookDelivery";

	public WebhookDeliveryOptions()
	{
		// Webhook deliveries are typically fast (seconds, not minutes)
		// so we use faster polling and shorter leases than general collectors
		MaxConcurrentItems = 20;
		PollIntervalSeconds = 2;
		LeaseSeconds = 300; // 5 minutes (much faster than collectors)
		MaxResponseBytes = 0; // Webhooks usually have small responses; disable check
	}

	public override void Validate()
	{
		base.Validate();

		// Webhook-specific validation can go here if needed
		// For now, just use parent validation
	}
}
