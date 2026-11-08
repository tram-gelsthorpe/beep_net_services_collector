using System;
using Beep.ServiceBus;
using Microsoft.Extensions.Logging;

namespace Beep.EventBus;

/// <summary>
/// Base class for webhook delivery worker implementations.
/// Provides the generic webhook delivery work loop that can be extended by domain-specific workers.
/// </summary>
public abstract class EventBusWorkerBase : GenericWorkerBase<PendingEventBus>
{
	private readonly EventBusOptions _options;

	protected EventBusWorkerBase(
		EventBusLeaseManager leaseManager,
		EventBusWorkProcessor workProcessor,
		EventBusOptions options,
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
