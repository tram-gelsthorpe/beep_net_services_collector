using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Beep.WebhookBus.Domain;

namespace Beep.WebhookBus.Repository;

/// <summary>
/// Generic repository abstraction for webhook delivery operations.
/// Implementations handle storage and retrieval of webhook deliveries with dynamic payload support.
/// </summary>
public interface IWebhookRepository
{
	/// <summary>
	/// Retrieves the next batch of pending webhook deliveries for processing.
	/// </summary>
	Task<IList<PendingWebhookItem>> GetPendingDeliveriesAsync(int batchSize, CancellationToken ct);

	/// <summary>
	/// Records the result of a delivery attempt (success or failure).
	/// </summary>
	Task RecordDeliveryResultAsync(
		Guid deliveryId,
		bool isSuccessful,
		int? httpStatusCode,
		string? errorMessage,
		string? responseContent,
		CancellationToken ct);

	/// <summary>
	/// Retrieves a specific delivery for inspection or retries.
	/// </summary>
	Task<PendingWebhookItem?> GetDeliveryByIdAsync(Guid deliveryId, CancellationToken ct);

	/// <summary>
	/// Marks a delivery as failed/abandoned (no more retry attempts).
	/// </summary>
	Task AbandonDeliveryAsync(Guid deliveryId, string reason, CancellationToken ct);

	/// <summary>
	/// Gets the current attempt count for a delivery.
	/// </summary>
	Task<int> GetAttemptCountAsync(Guid deliveryId, CancellationToken ct);
}
