using System.Threading;
using System.Threading.Tasks;

namespace Beep.ServiceBus.Database
{
	public interface IDatabase
	{
		Task Create(CancellationToken cancellationToken = default);
	}
}
