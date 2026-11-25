using Beep.EventBus;
using Beep.Scheduler;
using Microsoft.Extensions.Logging;

namespace WebsiteCollector.Worker;

/// <summary>
/// Subscriber that queues webhook deliveries when collection notifications occur.
/// Uses the generic Beep.EventBus delivery system with async, queued processing.
/// Webhook actual HTTP delivery is handled by the WebhookDeliveryWorker via the generic framework.
/// </summary>
public sealed class WebsiteSelectorsSubscriber : ISubscriber
{
	private readonly IEventAggregator _eventAggregator;
	private readonly IEventBusRepository _eventBusRepository;
	private readonly SelectorManagementRepository _selectorManagementRepository;
	private readonly ILogger<WebsiteSelectorsSubscriber> _logger;

	public string Name => "WebsiteSelectorsSubscriber";

	public WebsiteSelectorsSubscriber(
		IEventAggregator eventAggregator,
		IEventBusRepository eventBusRepository,
		SelectorManagementRepository selectorManagementRepository,
		ILogger<WebsiteSelectorsSubscriber> logger)
	{
		_eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
		_eventBusRepository = eventBusRepository ?? throw new ArgumentNullException(nameof(eventBusRepository));
		_selectorManagementRepository = selectorManagementRepository ?? throw new ArgumentNullException(nameof(selectorManagementRepository));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public async Task RegisterAsync(CancellationToken ct = default)
	{
		_eventAggregator.Subscribe<CollectionCompletedEvent>(OnCollectionCompletedAsync);
		_logger.LogInformation("Registered WebsiteSelectorsSubscriber to collection completed events");

		await Task.CompletedTask;
	}

	/// <summary>
	/// Register work items for all active website selectors
	/// </summary>
	public async Task<EventBusRegister> RegisterItemsAsync(
		WorkerSchedule schedule,
		CancellationToken cancellationToken)
	{
		var result = new EventBusRegister();

		try
		{
			_logger.LogInformation("WebsiteSelectorsSubscriber: Fetching active selectors for registration");

			// Get all active selectors
			var activeSelectors = await _selectorManagementRepository.GetDispatchWebsiteSelectorAsync(cancellationToken);

			if (activeSelectors.Count == 0)
			{
				_logger.LogWarning("WebsiteSelectorsSubscriber: No active selectors found");
				result.ItemsRegistered = 0;
				result.Success = true;
				result.Details = "No active selectors to register";
				return result;
			}

			_logger.LogInformation(
				"WebsiteSelectorsSubscriber: Found {SelectorCount} active selectors. Registering work items...",
				activeSelectors.Count);

			int registeredCount = 0;
			int failedCount = 0;
			var failedSelectors = new List<Guid>();

			// Register a work item for each active selector
			foreach (var selector in activeSelectors)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					_logger.LogWarning("WebsiteSelectorsSubscriber: Registration cancelled by request");
					break;
				}

				try
				{
					await _eventBusRepository.RegisterEventBusAsync(
						eventBusOwnerId: selector.Id,
						eventBusOwnerDeliveryUrl: selector.DispatchUrl,
						timeoutSeconds: selector.TimeoutSeconds,
						maxAttempts: selector.MaxAttempts,
						ct: cancellationToken);

					_logger.LogDebug(
						"WebsiteCollectorScheduleHandler: Registered domainId {Id} with dispatch url {DispatchUrl}",
						selector.Id, selector.DispatchUrl);

					registeredCount++;
				}
				catch (Exception ex)
				{
					_logger.LogError(
						ex,
						"WebsiteSelectorsSubscriber: Failed to register item for selector {SelectorId}",
						selector.Id);

					failedCount++;
					failedSelectors.Add(selector.Id);
				}
			}

			result.ItemsRegistered = registeredCount;
			result.Success = failedCount == 0;
			result.Details = $"Registered: {registeredCount}, Failed: {failedCount}, Total: {activeSelectors.Count}";

			if (failedCount > 0)
			{
				result.Error = $"Failed to register {failedCount} of {activeSelectors.Count} selectors";
			}

			_logger.LogInformation(
				"WebsiteSelectorsSubscriber: Registration complete. " +
				"Registered: {RegisteredCount}, Failed: {FailedCount}, Total: {TotalCount}",
				registeredCount, failedCount, activeSelectors.Count);

			return result;
		}
		catch (Exception ex)
		{
			_logger.LogError(
				ex,
				"WebsiteSelectorsSubscriber: Unexpected error during schedule registration");

			result.Success = false;
			result.Error = ex.Message;
			result.Details = ex.ToString();

			return result;
		}
	}

	private async Task OnCollectionCompletedAsync(CollectionCompletedEvent @event, CancellationToken ct)
	{
		try
		{
			_logger.LogInformation(
				"Collection completed for selector {SelectorId}, queueing webhook deliveries",
				@event.WebsiteSelectorsId);

			// Get all registered EventBus for this selector
			var eventBusCollection = await _eventBusRepository.GetByOwnerIdAsync(@event.WebsiteSelectorsId, ct);

			if (!eventBusCollection.Any())
			{
				_logger.LogDebug(
					"No EventBus registered for selector {SelectorId}",
					@event.WebsiteSelectorsId);
				return;
			}

			// Queue a delivery for each EventBus registered for this selector
			// The generic EventBus delivery system (Beep.EventBus) will handle actual HTTP delivery
			foreach (var eventBus in eventBusCollection)
			{
				try
				{
					_logger.LogDebug(
						"Queueing EventBus delivery for {EventBusOwnerUrl} (EventBus {EventBusId})",
						eventBus.EventBusOwnerUrl,
						eventBus.Id);

					// Queue the delivery - actual HTTP POST will be handled by EventBusDeliveryWorker
					await _eventBusRepository.QueueDeliveryAsync(eventBus.Id, @event.CollectedDataId, ct);

					_logger.LogInformation(
						"Queued EventBus delivery for selector {SelectorId} to EventBus {EventBusOwnerUrl}",
						@event.WebsiteSelectorsId,
						eventBus.EventBusOwnerUrl);
				}
				catch (Exception ex)
				{
					_logger.LogError(
						ex,
						"Failed to queue EventBus delivery for selector {SelectorId} and EventBus {EventBusId}",
						@event.WebsiteSelectorsId,
						eventBus.Id);
				}
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(
				ex,
				"Error processing collection completed event for selector {SelectorId}",
				@event.WebsiteSelectorsId);
		}
	}
}
