using System.Threading;
using System.Threading.Tasks;

namespace Beep.EventBus;

/// <summary>Sends one delivery attempt over HTTP.</summary>
public interface IEventBusSender
{
	/// <summary>
	/// POST the delivery's JSON payload to its endpoint. Never throws for HTTP errors or
	/// timeouts; they are returned as an unsuccessful <see cref="DeliveryOutcome"/>.
	/// </summary>
	Task<DeliveryOutcome> SendAsync(EventBusDelivery delivery, CancellationToken ct);
}
