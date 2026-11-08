using Beep.EventBus;

namespace WebsiteCollector.Worker.Features.WebhookDelivery.Repositories
{
	/// <summary>
	/// Project-specific webhook delivery repository.
	/// Provides a thin wrapper around generic Beep.Webhook IWebhookRepository
	/// with project-specific queuing logic for WebsiteSelectors context.
	/// </summary>
	public interface IEventBusDeliveryRepository
	{
		/// <summary>
		/// Queue a webhook delivery for processing.
		/// Creates a delivery work item in the generic framework.
		/// </summary>
		Task QueueDeliveryAsync(Guid webhookId, Guid collectedDataId, CancellationToken ct);

		/// <summary>
		/// Record the result of a webhook delivery attempt.
		/// </summary>
		Task RecordDeliveryResultAsync(Guid deliveryId, bool success, int? httpStatusCode, string? errorMessage, CancellationToken ct);

		/// <summary>
		/// Get pending deliveries from the generic framework.
		/// </summary>
		Task<IList<PendingEventBus>> GetPendingDeliveriesAsync(int batchSize, CancellationToken ct);
	}
}
