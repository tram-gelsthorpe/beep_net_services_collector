using Beep.EventBus;

namespace WebsiteCollector.Worker;

public sealed class WebsiteSelectorsService(
	IWebsiteSelectorsRepository repository, 
	IEventAggregator eventAggregator) : IWebsiteSelectorsService
{
	public async Task<IReadOnlyList<ClaimedWebsiteSelector>> ClaimSelectorsAsync(Guid workerId, int count, int lease, CancellationToken ct) =>
		await repository.ClaimAsync(workerId, count, lease, ct);

	public async Task StartExecutionAsync(Guid workItemId, Guid leaseToken, int leaseSeconds, Guid workerId, Guid domainId, int timeoutSeconds, int maxAttempts, CancellationToken ct) =>
		await repository.StartAsync(workItemId, leaseToken, leaseSeconds, workerId, domainId, timeoutSeconds, maxAttempts, ct);

	public async Task<bool> RenewLeaseAsync(Guid workItemId, Guid leaseToken, int leaseSeconds, CancellationToken ct) =>
		await repository.RenewAsync(workItemId, leaseToken, leaseSeconds, ct);

	public async Task<int> CompleteExecutionAsync(
		Guid workItemId,
		Guid domainId,
		Guid leaseToken,
		bool success,
		int? httpStatus,
		string? error,
		byte[]? hash,
		string? raw,
		string? extracted,
		CancellationToken ct,
		Guid? websiteId = null,
		Guid? collectedDataId = null,
		DateTimeOffset? startedAt = null,
		int? attemptNumber = null)
	{
		var result = await repository.FinishAsync(workItemId, leaseToken, success, httpStatus, error, hash, raw, extracted, domainId, ct);

		// Publish event for subscribers
		var @event = new CollectionCompletedEvent
		{
			WebsiteSelectorsId = domainId,
			WebsiteId = websiteId ?? Guid.Empty,
			CollectedDataId = collectedDataId ?? Guid.Empty,
			IsSuccess = success,
			HttpStatusCode = httpStatus,
			Error = error,
			ResponseContent = raw,
			ExtractedJson = extracted,
			ContentHash = hash,
			CollectionStartedAt = startedAt ?? DateTimeOffset.UtcNow,
			CollectionFinishedAt = DateTimeOffset.UtcNow,
			AttemptNumber = attemptNumber ?? 1
		};

		await eventAggregator.PublishAsync(@event, ct);

		return result;
	}
}
