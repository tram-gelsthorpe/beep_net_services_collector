namespace Beep.Webhook.Domain;

/// <summary>
/// Status values for webhook delivery work items.
/// </summary>
public static class WebhookItemStatus
{
	public const string Pending = "Pending";
	public const string Delivering = "Delivering";
	public const string Delivered = "Delivered";
	public const string Failed = "Failed";
	public const string Abandoned = "Abandoned";
}
