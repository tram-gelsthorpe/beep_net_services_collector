using System.Threading;
using System.Threading.Tasks;

namespace Beep.EventBus;

public interface IEventBusService
{
	Task DeliverAsync(PendingEventBus delivery, CancellationToken ct);
}
