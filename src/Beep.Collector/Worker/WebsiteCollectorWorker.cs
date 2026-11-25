using Beep.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Beep.Collector;

public sealed class WebsiteCollectorWorker : GenericWorkerBase<ClaimedWebsiteSelector>
{
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
		options.Value.Validate();

		// Publish this instance's ID so schedule handlers can attribute work to it.
		ArgumentNullException.ThrowIfNull(workerIdProvider);
		workerIdProvider.WorkerId = WorkerId;
	}
}
