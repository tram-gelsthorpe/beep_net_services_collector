
namespace WebsiteCollector.Worker
{
	/// <summary>
	/// Repository for managing website selection webhooks.
	/// </summary>
	public interface IWebsiteSelectionWebhookRepository
	{
		/// <summary>
		/// Get all enabled webhooks for a specific website selector.
		/// </summary>
		Task<IReadOnlyList<WebsiteSelectionWebhook>> GetByWebsiteSelectorsIdAsync(Guid websiteSelectorsId, CancellationToken ct);

		/// <summary>
		/// Register a new webhook.
		/// </summary>
		Task<Guid> RegisterWebhookAsync(Guid websiteSelectorsId, string webhookUrl, CancellationToken ct);

		/// <summary>
		/// Update webhook after calling it.
		/// </summary>
		Task UpdateWebhookStatusAsync(Guid webhookId, int? httpStatus, string? errorMessage, CancellationToken ct);

		/// <summary>
		/// Disable a webhook.
		/// </summary>
		Task DisableWebhookAsync(Guid webhookId, CancellationToken ct);
	}
}
