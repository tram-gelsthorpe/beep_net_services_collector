using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WebsiteCollector.Features.WebsiteCollection.Models;

namespace WebsiteCollector.Features.WebsiteCollection.Repositories;

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

public sealed class WebsiteSelectionWebhookRepository(IConfiguration config) : IWebsiteSelectionWebhookRepository
{
	private SqlConnection Open() => new(config.GetConnectionString("Default") ?? throw new InvalidOperationException("Missing Default connection string"));

	public async Task<IReadOnlyList<WebsiteSelectionWebhook>> GetByWebsiteSelectorsIdAsync(Guid websiteSelectorsId, CancellationToken ct)
	{
		await using var conn = Open();
		const string sql = """
			SELECT Id, WebsiteSelectorsId, WebhookUrl, IsEnabled, RetryAttempts, MaxRetryAttempts,
				   LastCalledAt, LastStatusCode, LastErrorMessage
			FROM dbo.WebsiteSelectionWebhooks
			WHERE WebsiteSelectorsId = @WebsiteSelectorsId AND IsEnabled = 1
			""";

		var rows = await conn.QueryAsync<WebsiteSelectionWebhook>(
			new CommandDefinition(sql, new { WebsiteSelectorsId = websiteSelectorsId }, cancellationToken: ct));

		return rows.AsList();
	}

	public async Task<Guid> RegisterWebhookAsync(Guid websiteSelectorsId, string webhookUrl, CancellationToken ct)
	{
		await using var conn = Open();
		const string sql = """
			IF NOT EXISTS (SELECT 1 FROM dbo.WebsiteSelectionWebhooks 
						  WHERE WebsiteSelectorsId = @WebsiteSelectorsId AND WebhookUrl = @WebhookUrl)
			BEGIN
				INSERT INTO dbo.WebsiteSelectionWebhooks (WebsiteSelectorsId, WebhookUrl, IsEnabled)
				VALUES (@WebsiteSelectorsId, @WebhookUrl, 1);
				SELECT CAST(SCOPE_IDENTITY() as uniqueidentifier);
			END
			""";

		var result = await conn.QueryAsync<Guid>(
			new CommandDefinition(sql, 
				new { WebsiteSelectorsId = websiteSelectorsId, WebhookUrl = webhookUrl }, 
				cancellationToken: ct));

		return result.FirstOrDefault();
	}

	public async Task UpdateWebhookStatusAsync(Guid webhookId, int? httpStatus, string? errorMessage, CancellationToken ct)
	{
		await using var conn = Open();
		const string sql = """
			UPDATE dbo.WebsiteSelectionWebhooks
			SET LastCalledAt = SYSUTCDATETIME(),
				LastStatusCode = @HttpStatus,
				LastErrorMessage = @ErrorMessage,
				UpdatedAt = SYSUTCDATETIME()
			WHERE Id = @WebhookId
			""";

		await conn.ExecuteAsync(
			new CommandDefinition(sql, 
				new { WebhookId = webhookId, HttpStatus = httpStatus, ErrorMessage = errorMessage }, 
				cancellationToken: ct));
	}

	public async Task DisableWebhookAsync(Guid webhookId, CancellationToken ct)
	{
		await using var conn = Open();
		const string sql = "UPDATE dbo.WebsiteSelectionWebhooks SET IsEnabled = 0, UpdatedAt = SYSUTCDATETIME() WHERE Id = @WebhookId";

		await conn.ExecuteAsync(
			new CommandDefinition(sql, new { WebhookId = webhookId }, cancellationToken: ct));
	}
}
