using System.Threading;
using System.Threading.Tasks;

namespace Beep.ServiceBus.Database
{
	public interface IServiceBusDatabase
	{
		Task Create(CancellationToken cancellationToken = default);
	}
}
