using System.Threading;
using System.Threading.Tasks;

namespace Beep.EventBus;

public interface IEventBusDatabase
{
	Task Create(CancellationToken cancellationToken = default);
}
