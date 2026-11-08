using System;
using Beep.ServiceBus.Leasing;
using Beep.Webhook.Domain;

namespace Beep.Webhook.Workers.Adapters;

/// <summary>
/// Represents a claimed webhook delivery work item with lease token.
/// </summary>
public sealed class ClaimedWebhookItem : IClaimedItem<PendingWebhookItem>
{
	public ClaimedWebhookItem(PendingWebhookItem item)
	{
		Item = item ?? throw new ArgumentNullException(nameof(item));
		LeaseToken = Guid.NewGuid(); // Each claim generates a unique lease token
	}

	public PendingWebhookItem Item { get; }
	public Guid LeaseToken { get; }
}
