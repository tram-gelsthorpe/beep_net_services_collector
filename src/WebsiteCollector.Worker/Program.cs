using Beep.EventBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WebsiteCollector.Worker;

var builder = Host.CreateApplicationBuilder(args);

//Logs
//builder.Services.RegisterBeepLog(builder.Configuration);

//Create databases
builder.Services.AddScoped<CreationDatabaseService>();

//ServiceBus Infrastructure
builder.Services.RegisterServiceBus(builder.Configuration);

// EventBus Infrastructure
builder.Services.RegisterEventBus(builder.Configuration);

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