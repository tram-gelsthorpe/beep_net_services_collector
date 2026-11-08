using Beep.ServiceBus.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Beep.Webhook.Database;

namespace WebsiteCollector.Worker.Infrastructure.Database;

/// <summary>
/// Initializes all required databases and schemas on application startup.
/// Handles ServiceBus (WorkItems), WebhookBus (Webhooks), and WebsiteCollector schema creation.
/// </summary>
public sealed class DatabaseService(
	IConfiguration configuration,
	ILogger<DatabaseService> logger)
{
	/// <summary>
	/// Initialize all databases and schemas.
	/// Should be called during application startup before any services access the database.
	/// </summary>
	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			logger.LogInformation("Starting database initialization...");

			// Get connection string - uses ServiceBus connection for WorkItems storage
			var connectionString = configuration.GetConnectionString("ServiceBus")
				?? throw new InvalidOperationException("Missing ServiceBus connection string");

			// Initialize ServiceBus database (WorkItems table and stored procedures)
			logger.LogInformation("Initializing ServiceBus database...");
			var serviceBusDb = new SqlServiceBusDatabase(connectionString);
			await serviceBusDb.Create(cancellationToken);
			logger.LogInformation("ServiceBus database initialized successfully");

			// Initialize WebhookBus database (Webhooks and WebhookDeliveries tables)
			// If a separate Webhook database is configured, initialize it too
			var webhookConnectionString = configuration.GetConnectionString("Webhook")
				?? throw new InvalidOperationException("Missing Webhook connection string ");

			logger.LogInformation("Initializing WebhookBus database...");
			var webhookDb = new SqlWebhookDatabase(webhookConnectionString);
			await webhookDb.Create(cancellationToken);
			logger.LogInformation("WebhookBus database initialized successfully");


			// Initialize WebsiteCollector schema (WebsiteSelectors, WebsiteSelectionWebhooks, WebsiteSelectorsCollectedData)
			var collectorConnectionString = configuration.GetConnectionString("Collector") 
				?? throw new InvalidOperationException("Missing Collector connection string ");

			logger.LogInformation("Initializing WebsiteCollector database...");
			var collectorDb = new SqlWebhookDatabase(collectorConnectionString);
			await collectorDb.Create(cancellationToken);
			logger.LogInformation("WebsiteCollector database initialized successfully");

		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Database initialization failed");
			throw;
		}
	}
}
