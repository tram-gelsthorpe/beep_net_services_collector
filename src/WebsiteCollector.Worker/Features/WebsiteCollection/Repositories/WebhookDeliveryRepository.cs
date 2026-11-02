using Dapper;
using System.Data;
using WebsiteCollector.Features.WebsiteCollection.Models;
using WebsiteCollector.Infrastructure.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;

namespace WebsiteCollector.Features.WebsiteCollection.Repositories;

public interface IWebhookDeliveryRepository
{
	Task QueueDeliveryAsync(Guid webhookId, Guid collectedDataId, CancellationToken ct);
	Task<IReadOnlyList<PendingWebhookDelivery>> ClaimPendingDeliveriesAsync(int maxResults, CancellationToken ct);
	Task CompleteDeliveryAsync(Guid deliveryId, bool success, int? httpStatusCode, string? errorMessage, CancellationToken ct);
}

public sealed class WebhookDeliveryRepository(IConfiguration config) : IWebhookDeliveryRepository
{
	private SqlConnection Open() => new(config.GetConnectionString("Default") ?? throw new InvalidOperationException("Missing Default connection string"));

	public async Task QueueDeliveryAsync(Guid webhookId, Guid collectedDataId, CancellationToken ct)
	{
		await using var connection = Open();
		await connection.ExecuteAsync(
			new CommandDefinition(
				"dbo.QueueWebhookDelivery",
				new { WebsiteSelectorsWebhooksId = webhookId, CollectedDataId = collectedDataId },
				commandType: CommandType.StoredProcedure,
				cancellationToken: ct));
	}

	public async Task<IReadOnlyList<PendingWebhookDelivery>> ClaimPendingDeliveriesAsync(int maxResults, CancellationToken ct)
	{
		await using var connection = Open();
		var deliveries = await connection.QueryAsync<PendingWebhookDelivery>(
			new CommandDefinition(
				"dbo.ClaimWebhookDelivery",
				new { MaxResults = maxResults },
				commandType: CommandType.StoredProcedure,
				cancellationToken: ct));

		return deliveries.ToList();
	}

	public async Task CompleteDeliveryAsync(Guid deliveryId, bool success, int? httpStatusCode, string? errorMessage, CancellationToken ct)
	{
		await using var connection = Open();
		await connection.ExecuteAsync(
			new CommandDefinition(
				"dbo.CompleteWebhookDelivery",
				new { DeliveryId = deliveryId, Success = success, HttpStatusCode = httpStatusCode, ErrorMessage = errorMessage },
				commandType: CommandType.StoredProcedure,
				cancellationToken: ct));
	}
}
