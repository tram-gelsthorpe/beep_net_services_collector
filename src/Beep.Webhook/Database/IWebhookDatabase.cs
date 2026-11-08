using System.Threading;
using System.Threading.Tasks;

namespace Beep.Webhook.Database
{
	public interface IWebhookDatabase
	{
		Task Create(CancellationToken cancellationToken = default);
	}
}
