using System.Threading;
using System.Threading.Tasks;

namespace Beep.ServiceBus;

public interface IServiceBusDatabase
{
	Task Create(string schema = "dbo", CancellationToken cancellationToken = default);
}
