using System.Threading;
using System.Threading.Tasks;

namespace Beep.ServiceBus.Database
{
	public interface IWebhookDatabase
	{
		Task Create(CancellationToken cancellationToken = default);
	}
}
