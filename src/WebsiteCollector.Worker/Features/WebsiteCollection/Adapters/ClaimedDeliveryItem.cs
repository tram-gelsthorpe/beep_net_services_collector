using System;
using WebsiteCollector.Features.WebsiteCollection.Models;
using WorkerFramework.Leasing;

namespace WebsiteCollector.Features.WebsiteCollection;

/// <summary>
/// Adapter for IClaimedItem to wrap PendingWebhookDelivery
/// </summary>
internal sealed class ClaimedDeliveryItem : IClaimedItem<PendingWebhookDelivery>
{
	public PendingWebhookDelivery Item { get; }
	public Guid LeaseToken { get; }

	public ClaimedDeliveryItem(PendingWebhookDelivery delivery, Guid leaseToken)
	{
		Item = delivery;
		LeaseToken = leaseToken;
	}
}
