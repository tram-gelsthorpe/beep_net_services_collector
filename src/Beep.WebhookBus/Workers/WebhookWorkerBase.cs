using System;
using Beep.ServiceBus.Workers;
using Beep.WebhookBus.Domain;
using Beep.WebhookBus.Infrastructure.Configuration;
using Beep.WebhookBus.Workers.Adapters;
using Microsoft.Extensions.Logging;

namespace Beep.WebhookBus.Workers;

/// <summary>
/// Base class for webhook delivery worker implementations.
/// Provides the generic webhook delivery work loop that can be extended by domain-specific workers.
/// </summary>
public abstract class WebhookWorkerBase : GenericWorkerBase<PendingWebhookItem>
{
	private readonly WebhookOptions _options;

	protected WebhookWorkerBase(
		WebhookLeaseManager leaseManager,
		WebhookWorkProcessor workProcessor,
		WebhookOptions options,
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
