namespace Beep.WebhookBus.Domain;

/// <summary>
/// Generic interface for webhook payloads.
/// Implementations can define the specific payload structure for different event types.
/// </summary>
public interface IWebhookItemPayload
{
	string Serialize();
}
