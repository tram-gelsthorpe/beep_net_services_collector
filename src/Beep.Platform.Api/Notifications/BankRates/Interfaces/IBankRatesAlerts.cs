namespace Beep.Platform.Api.Notifications;

/// <summary>Queues the rate alerts of a bank that just published new rates.</summary>
public interface IBankRatesAlerts
{
	/// <summary>Queues an alert for every confirmed subscriber who chose the bank; returns how many.</summary>
	Task<int> QueueAsync(string bankBic, CancellationToken cancellationToken);
}
