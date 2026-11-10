using System.Security.Cryptography;
using Beep.EventBus;
using Beep.Log.NLog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WebsiteCollector.Worker;

var builder = Host.CreateApplicationBuilder(args);

//Logs
//builder.Services.RegisterBeepLog(builder.Configuration);

// Options
builder.Services.Configure<WebsiteCollectorOptions>(
	builder.Configuration.GetSection(WebsiteCollectorOptions.Name));

builder.Services.Configure<EventBusOptions>(
	builder.Configuration.GetSection(EventBusOptions.Name));

//Services
builder.Services.AddScoped<CreationDatabaseService>();

//builder.Services.AddSingleton<Database>();
//builder.Services.AddSingleton<IEventAggregator, EventAggregator>();


//// Beep.EventBus - Generic event bus infrastructure
//// WebhookDeliveryRepository implements both IWebhookDeliveryRepository (project-specific) and IEventBusRepository (generic)
//builder.Services.AddScoped<IEventBusDeliveryRepository, EventBusDeliveryRepository>();
//builder.Services.AddScoped<IEventBusRepository>(sp => (IEventBusRepository)sp.GetRequiredService<IEventBusDeliveryRepository>());
//builder.Services.AddScoped<IEventBusService, EventBusService>();

//// WebsiteCollector.Worker specific
//builder.Services.AddScoped<IWebsiteCollectionRepository, WebsiteCollectionRepository>();
//builder.Services.AddScoped<IWebsiteSelectionWebhookRepository, WebsiteSelectionWebhookRepository>();
//builder.Services.AddScoped<IWebsiteCollectionService, WebsiteCollectionService>();
//builder.Services.AddScoped<ISelectorManagementService, SelectorManagementService>();
builder.Services.AddScoped<ISubscriber, WebsiteSelectorsSubscriber>();

//builder.Services.AddCollectorHttpClient();

//builder.Services.AddHostedService<WebsiteCollectorWorker>();
//builder.Services.AddHostedService<EventBusWorker>();

var host = builder.Build();

// Initialize database
using (var scope = host.Services.CreateScope())
{
	// Initialize logging
	//var logger = scope.ServiceProvider
	//	.CreateDatabase();

	// Initialize database
	var dbInitializer = scope.ServiceProvider
		.GetRequiredService<CreationDatabaseService>();

	await dbInitializer.InitializeAsync();

	// Register subscribers
	var subscribers = scope.ServiceProvider
		.GetServices<ISubscriber>();

	foreach (var subscriber in subscribers)
	{
		await subscriber.RegisterAsync(
			host.Services
				.GetRequiredService<IHostApplicationLifetime>()
				.ApplicationStopping);
	}
}

// Run host once
await host.RunAsync();