using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using WebsiteCollector.Infrastructure.Configuration;
using WebsiteCollector.Features.WebsiteCollection.Services;
using WebsiteCollector.Features.WebsiteCollection.Models;
using WebsiteCollector.Features.WebsiteCollection;
using WorkerFramework.Workers;

public sealed class CollectorWorker : GenericWorkerBase<ClaimedWebsiteSelector>
{
	private readonly CollectorOptions _collectorOptions;

	public CollectorWorker(
		IWebsiteCollectionService collectionService,
		IHttpClientFactory httpClientFactory,
		IOptions<CollectorOptions> options,
		ILogger<CollectorWorker> logger)
		: base(
			new SelectorLeaseManager(collectionService),
			new CollectorWorkProcessor(collectionService, httpClientFactory, options, logger),
			options.Value,
			logger)
	{
		_collectorOptions = options?.Value ?? throw new ArgumentNullException(nameof(options));
		_collectorOptions.Validate();
	}

	protected override int PollIntervalSeconds => _collectorOptions.PollIntervalSeconds;
	protected override int MaxConcurrentItems => _collectorOptions.MaxConcurrentItems;
	protected override int LeaseSeconds => _collectorOptions.LeaseSeconds;
}
