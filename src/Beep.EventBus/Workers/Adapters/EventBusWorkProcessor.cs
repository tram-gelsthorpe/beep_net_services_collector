using System;
using System.Threading;
using System.Threading.Tasks;
using Beep.ServiceBus;
using Microsoft.Extensions.Logging;

namespace Beep.EventBus;

/// <summary>
/// Processes ServiceBus work items of type <see cref="EventBusPublisher.DeliveryDomainType"/>:
/// loads the delivery, POSTs it, records the result and completes the work item. Retries,
/// back-off and abandoning after MaxAttempts are done by the ServiceBus.
/// </summary>
public sealed class EventBusWorkProcessor : IWorkProcessor<IClaimedWorkItem>
{
	private readonly IEventBusRepository _repository;
	private readonly IEventBusSender _sender;
	private readonly IWorkItemRepository _workItems;
	private readonly ILogger<EventBusWorkProcessor> _logger;

	public int HeartbeatIntervalSeconds { get; }

	public EventBusWorkProcessor(
		IEventBusRepository repository,
		IEventBusSender sender,
		IWorkItemRepository workItems,
		EventBusOptions options,
		ILogger<EventBusWorkProcessor> logger)
	{
		_repository = repository ?? throw new ArgumentNullException(nameof(repository));
		_sender = sender ?? throw new ArgumentNullException(nameof(sender));
		_workItems = workItems ?? throw new ArgumentNullException(nameof(workItems));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		ArgumentNullException.ThrowIfNull(options);
		HeartbeatIntervalSeconds = Math.Max(10, options.LeaseSeconds / 3);
	}

	public Task InitializeAsync(IClaimedWorkItem item, Guid leaseToken, Guid workerId, CancellationToken ct) =>
		Task.CompletedTask;

	public async Task<ProcessingResult> ProcessAsync(IClaimedWorkItem item, Guid leaseToken, CancellationToken ct)
	{
		if (item.DomainType != EventBusPublisher.DeliveryDomainType)
			return Failed($"Unexpected domain type '{item.DomainType}'");

		var delivery = await _repository.GetDeliveryAsync(item.DomainId, ct);

		// Nothing (left) to send: finish the work item instead of retrying it.
		if (delivery is null)
			return Skipped(item, "delivery no longer exists");
		if (delivery.DeliveredAt is not null)
			return Skipped(item, "already delivered");
		if (!delivery.IsEnabled)
			return Skipped(item, "endpoint is disabled");

		_logger.LogDebug(
			"Delivering {DeliveryId} (attempt {Attempt} of {MaxAttempts})",
			delivery.DeliveryId, item.AttemptNumber, item.MaxAttempts);

		var outcome = await _sender.SendAsync(delivery, ct);
		await _repository.RecordAttemptAsync(delivery.DeliveryId, outcome, ct);

		return outcome.Delivered
			? new ProcessingResult { Success = true }
			: Failed(outcome.Error ?? "Delivery failed");
	}

	public async Task CompleteAsync(IClaimedWorkItem item, Guid leaseToken, ProcessingResult result, CancellationToken ct)
	{
		var completed = await _workItems.CompleteAsync(
			item.WorkItemId, leaseToken, result.Success, result.ErrorMessage, cancellationToken: ct);

		if (!completed)
			_logger.LogWarning("Lease on delivery work item {WorkItemId} was lost before it could be completed", item.WorkItemId);
	}

	private ProcessingResult Skipped(IClaimedWorkItem item, string reason)
	{
		_logger.LogInformation("Skipping delivery {DeliveryId}: {Reason}", item.DomainId, reason);
		return new ProcessingResult { Success = true };
	}

	private static ProcessingResult Failed(string error) => new() { Success = false, ErrorMessage = error };
}
