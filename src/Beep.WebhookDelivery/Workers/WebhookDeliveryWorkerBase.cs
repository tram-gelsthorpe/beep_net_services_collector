using System;
using Beep.WebhookDelivery.Domain;
using Beep.WebhookDelivery.Infrastructure.Configuration;
using Beep.WebhookDelivery.Workers.Adapters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerFramework.Workers;

namespace Beep.WebhookDelivery.Workers;

/// <summary>
/// Base class for webhook delivery worker implementations.
/// Provides the generic webhook delivery work loop that can be extended by domain-specific workers.
/// </summary>
public abstract class WebhookDeliveryWorkerBase : GenericWorkerBase<PendingWebhookDelivery>
{
	private readonly WebhookDeliveryOptions _options;

	protected WebhookDeliveryWorkerBase(
		DeliveryLeaseManager leaseManager,
		DeliveryWorkProcessor workProcessor,
		WebhookDeliveryOptions options,
		ILogger logger)
		: base(leaseManager, workProcessor, options, logger)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_options.Validate();
	}

	protected override int PollIntervalSeconds => _options.PollIntervalSeconds;
	protected override int MaxConcurrentItems => _options.MaxConcurrentItems;
	protected override int LeaseSeconds => _options.LeaseSeconds;
}
