using WebsiteCollector.Worker.Features.WebhookDelivery.Models;

namespace WebsiteCollector.Worker.Features.WebhookDelivery.Repositories
{
	public interface IWebhookDeliveryRepository
	{
		/// <summary>
		/// Queue a webhook delivery for processing by creating a work item in the generic framework.
		/// </summary>
		Task QueueDeliveryAsync(Guid webhookId, Guid collectedDataId, CancellationToken ct);

		/// <summary>
		/// Get pending webhook deliveries ready for processing from the database.
		/// Note: Actual claiming is handled by the generic WorkerFramework.
		/// </summary>
		Task<IReadOnlyList<PendingWebhookDelivery>> GetPendingDeliveriesAsync(Guid webhookId, int maxResults, CancellationToken ct);

		/// <summary>
		/// Record a webhook delivery result in the database.
		/// </summary>
		Task RecordDeliveryResultAsync(Guid deliveryId, bool success, int? httpStatusCode, string? errorMessage, CancellationToken ct);
	}
}
