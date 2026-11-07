using Beep.ServiceBus.Leasing;
using WebsiteCollector.Worker.Features.WebhookDelivery.Models;
using WebsiteCollector.Worker.Features.WebhookDelivery.Repositories;

namespace WebsiteCollector.Worker.Features.WebhookDelivery.Adapters;

/// <summary>
/// Lease manager implementation for webhook deliveries using IWebhookDeliveryRepository
/// </summary>
internal sealed class DeliveryLeaseManager : ILeaseManager<PendingWebhookDelivery>
{
	private readonly IWebhookDeliveryRepository _repository;

	public DeliveryLeaseManager(IWebhookDeliveryRepository repository)
	{
		_repository = repository ?? throw new ArgumentNullException(nameof(repository));
	}

	public async Task<IReadOnlyList<IClaimedItem<PendingWebhookDelivery>>> ClaimAsync(
		Guid workerIdent,
		int maxItems,
		int leaseSeconds,
		CancellationToken ct)
	{
		var items = await _repository.GetPendingDeliveriesAsync(Guid.Empty, maxItems, ct);
		return items
			.Select(item => (IClaimedItem<PendingWebhookDelivery>)new ClaimedDeliveryItem(item, Guid.NewGuid()))
			.ToList();
	}

	public async Task<bool> RenewAsync(object item, Guid leaseToken, int leaseSeconds, CancellationToken ct)
	{
		// Webhook deliveries typically don't have heartbeat renewal in this implementation
		// They're meant to be completed quickly; return true to avoid lease loss
		await Task.CompletedTask;
		return true;
	}

	public async Task ReleaseAsync(object item, Guid leaseToken, CancellationToken ct)
	{
		// Release is handled via repository update in the processor
		await Task.CompletedTask;
	}
}
