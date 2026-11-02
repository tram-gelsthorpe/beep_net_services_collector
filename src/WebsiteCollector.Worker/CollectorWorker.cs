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
	private readonly CollectorOptions _settings;

	public CollectorWorker(
		IWebsiteCollectionService collectionService,
		IHttpClientFactory httpClientFactory,
		IOptions<CollectorOptions> settings,
		ILogger<CollectorWorker> logger)
		: base(
			new SelectorLeaseManager(collectionService),
			new CollectorWorkProcessor(collectionService, httpClientFactory, settings, logger),
			logger)
	{
		_settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
		ValidateSettings();
	}

	protected override int PollIntervalSeconds => _settings.PollSeconds;
	protected override int MaxConcurrentItems => _settings.Concurrency;
	protected override int LeaseSeconds => _settings.LeaseSeconds;

	private void ValidateSettings()
	{
		if (_settings.Concurrency is < 1 or > 256 ||
			_settings.LeaseSeconds is < 60 or > 3600 ||
			_settings.PollSeconds < 1 ||
			_settings.MaxResponseBytes < 1 ||
			_settings.AllowedHosts.Length == 0)
		{
			throw new InvalidOperationException("Invalid Collector settings or empty AllowedHosts");
		}
	}
}
