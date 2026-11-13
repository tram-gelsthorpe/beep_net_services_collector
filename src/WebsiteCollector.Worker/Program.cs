using Beep.EventBus;
using Beep.ServiceBus;
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

// EventBus Infrastructure
builder.Services.AddSingleton<IEventAggregator, EventAggregator>();
builder.Services.AddScoped<IEventBusService, EventBusService>();

// WebsiteCollector.Worker specific
builder.Services.AddScoped<IWebsiteCollectionRepository, WebsiteCollectionRepository>();
builder.Services.AddScoped<IWebsiteCollectionService, WebsiteCollectionService>();
builder.Services.AddScoped<ISelectorManagementService, SelectorManagementService>();

// Subscribers
builder.Services.AddScoped<ISubscriber, WebsiteSelectorsSubscriber>();

builder.Services.AddCollectorHttpClient();

builder.Services.AddHostedService<WebsiteCollectorWorker>();
builder.Services.AddHostedService<EventBusWorker>();

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