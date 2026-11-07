using System;
using Beep.WebhookDelivery.Domain;
using WorkerFramework.Leasing;

namespace Beep.WebhookDelivery.Workers.Adapters;

/// <summary>
/// Represents a claimed webhook delivery work item with lease token.
/// </summary>
public sealed class ClaimedDeliveryItem : IClaimedItem<PendingWebhookDelivery>
{
	public ClaimedDeliveryItem(PendingWebhookDelivery item)
	{
		Item = item ?? throw new ArgumentNullException(nameof(item));
		LeaseToken = Guid.NewGuid(); // Each claim generates a unique lease token
	}

	public PendingWebhookDelivery Item { get; }
	public Guid LeaseToken { get; }
}
