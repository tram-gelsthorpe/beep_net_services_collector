using WebsiteCollector.Worker.Features.WebsiteCollector.Models;
using WebsiteCollector.Worker.Features.WebsiteCollector.Repositories;
using WebsiteCollector.Worker.Features.WebsiteCollector.Events;
using Beep.EventBus;

namespace WebsiteCollector.Worker.Features.WebsiteCollector.Services;

public sealed class WebsiteCollectionService(
	IWebsiteCollectionRepository repository,
	IEventAggregator eventAggregator) : IWebsiteCollectionService
{
	public async Task<IReadOnlyList<ClaimedWebsiteSelector>> ClaimSelectorsAsync(Guid worker, int count, int lease, CancellationToken ct) =>
		await repository.ClaimAsync(worker, count, lease, ct);

	public async Task StartExecutionAsync(Guid websiteSelectorsId, Guid leaseToken, Guid worker, CancellationToken ct) =>
		await repository.StartAsync(websiteSelectorsId, leaseToken, worker, ct);

	public async Task<bool> RenewLeaseAsync(Guid websiteSelectorsId, Guid leaseToken, int leaseSeconds, CancellationToken ct) =>
		await repository.RenewAsync(websiteSelectorsId, leaseToken, leaseSeconds, ct);

	public async Task<int> CompleteExecutionAsync(
		Guid websiteSelectorsId,
		Guid leaseToken,
		bool success,
		int? httpStatus,
		string? error,
		byte[]? hash,
		string? raw,
		string? extracted,
		CancellationToken ct,
		Guid? websiteId = null,
		DateTimeOffset? startedAt = null,
		int? attemptNumber = null)
	{
		var result = await repository.FinishAsync(websiteSelectorsId, leaseToken, success, httpStatus, error, hash, raw, extracted, ct);

		// Publish event for subscribers
		var @event = new CollectionCompletedEvent
		{
			WebsiteSelectorsId = websiteSelectorsId,
			WebsiteId = websiteId ?? Guid.Empty,
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
