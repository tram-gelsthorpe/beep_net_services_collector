using System.Threading;
using System.Threading.Tasks;
using Beep.WebhookBus.Domain;

namespace Beep.WebhookBus.Services
{
	public interface IWebhookService
	{
		Task DeliverAsync(PendingWebhookItem delivery, CancellationToken ct);
	}
}
