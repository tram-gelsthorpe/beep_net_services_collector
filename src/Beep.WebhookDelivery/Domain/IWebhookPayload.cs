namespace Beep.WebhookDelivery.Domain;

/// <summary>
/// Generic interface for webhook payloads.
/// Implementations can define the specific payload structure for different event types.
/// </summary>
public interface IWebhookPayload
{
	string Serialize();
}
