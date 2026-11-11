using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Beep.ServiceBus;

namespace WebsiteCollector.Worker;

public sealed class WebsiteCollectorWorker : GenericWorkerBase<ClaimedWebsiteSelector>
{
	private readonly WebsiteCollectorOptions _collectorOptions;

	public WebsiteCollectorWorker(
		IWebsiteCollectionService collectionService,
		IHttpClientFactory httpClientFactory,
		IOptions<WebsiteCollectorOptions> options,
		ILogger<WebsiteCollectorWorker> logger)
		: base(
			new CollectorLeaseManager(collectionService), 
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
