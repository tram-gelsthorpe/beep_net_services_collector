using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Beep.ServiceBus;

namespace WebsiteCollector.Worker;

public sealed class WebsiteCollectorWorker : GenericWorkerBase<ClaimedWebsiteSelector>
{
	private readonly WebsiteCollectorOptions _collectorOptions;
	private readonly IWorkerIdProvider _workerIdProvider;

	public WebsiteCollectorWorker(
		IWebsiteSelectorsService collectionService,
		IHttpClientFactory httpClientFactory,
		IOptions<WebsiteCollectorOptions> options,
		IWorkerIdProvider workerIdProvider,
		ILogger<WebsiteCollectorWorker> logger)
		: base(
			new CollectorLeaseManager(collectionService), 
			new CollectorWorkProcessor(collectionService, httpClientFactory, options, logger),
			options.Value,
			logger)
	{
		_collectorOptions = options?.Value ?? throw new ArgumentNullException(nameof(options));
		_collectorOptions.Validate();
		_workerIdProvider = workerIdProvider ?? throw new ArgumentNullException(nameof(workerIdProvider));

		// Set the worker ID in the provider so it can be accessed globally
		_workerIdProvider.WorkerId = WorkerId;
	}

	protected override int PollIntervalSeconds => _collectorOptions.PollIntervalSeconds;
	protected override int MaxConcurrentItems => _collectorOptions.MaxConcurrentItems;
	protected override int LeaseSeconds => _collectorOptions.LeaseSeconds;
}
