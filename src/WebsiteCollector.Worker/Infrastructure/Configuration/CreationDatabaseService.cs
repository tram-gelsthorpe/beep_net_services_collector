using Beep.EventBus;
using Beep.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WebsiteCollector.Worker.Infrastructure.Configuration;

/// <summary>
/// Initializes all required databases and schemas on application startup.
/// Handles ServiceBus (WorkItems), WebhookBus (Webhooks), and WebsiteCollector schema creation.
/// </summary>
public sealed class CreationDatabaseService(
	IConfiguration configuration,
	ILogger<CreationDatabaseService> logger)
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

			// Initialize EventBus database (EventBus and EventBusDeliveries tables)
			// If a separate Webhook database is configured, initialize it too
			var webhookConnectionString = configuration.GetConnectionString("Webhook")
				?? throw new InvalidOperationException("Missing Webhook connection string ");

			logger.LogInformation("Initializing EventBus database...");
			var webhookDb = new SqlEventBusDatabase(webhookConnectionString);
			await webhookDb.Create(cancellationToken);
			logger.LogInformation("EventBus database initialized successfully");


			// Initialize WebsiteCollector schema (WebsiteSelectors, WebsiteSelectionWebhooks, WebsiteSelectorsCollectedData)
			var collectorConnectionString = configuration.GetConnectionString("Collector") 
				?? throw new InvalidOperationException("Missing Collector connection string ");

			logger.LogInformation("Initializing WebsiteCollector database...");
			var collectorDb = new SqlEventBusDatabase(collectorConnectionString);
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
