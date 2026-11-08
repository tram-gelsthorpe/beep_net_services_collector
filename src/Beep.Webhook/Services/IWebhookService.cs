using System.Threading;
using System.Threading.Tasks;
using Beep.Webhook.Domain;

namespace Beep.Webhook.Services
{
	public interface IWebhookService
	{
		Task DeliverAsync(PendingWebhookItem delivery, CancellationToken ct);
	}
}
