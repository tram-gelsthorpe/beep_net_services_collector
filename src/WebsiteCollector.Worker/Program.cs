using Beep.Configuration;
using Beep.EventBus;
using Beep.Log.NLog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WebsiteCollector.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Settings: appsettings.json (no secrets), then user secrets (Development only), environment
// variables, command line, and Azure Key Vault when KeyVault:Uri is set.
builder.Configuration.AddKeyVaultIfConfigured();
builder.Configuration.RequireConnectionStrings(
	CreationDatabaseService.SchedulerDatabaseName,
	CreationDatabaseService.ServiceBusDatabaseName,
	CreationDatabaseService.EventBusDatabaseName,
	CreationDatabaseService.SelectorDatabaseName);
builder.Configuration.Require("BeepNLog:DatabaseConnection");

//Logs
builder.Services.RegisterBeepLog(builder.Configuration);

//Create databases
builder.Services.AddScoped<CreationDatabaseService>();

//ServiceBus Infrastructure
builder.Services.RegisterServiceBus(builder.Configuration);

// EventBus Infrastructure
builder.Services.RegisterEventBus(builder.Configuration);

// Scheduler Infrastructure
builder.Services.RegisterScheduler(builder.Configuration);

// Subscribers
builder.Services.AddSingleton<ISubscriber, WebsiteSelectorsSubscriber>();

// WebsiteCollector.Worker specific
builder.Services.AddCollectorHttpClient();

builder.Services.AddHostedService<WebsiteCollectorWorker>();

var host = builder.Build();

// Initialize database
using (var scope = host.Services.CreateScope())
{
	// Initialize logging
	var logger = scope.ServiceProvider
		.CreateNLogDatabase();

	// Initialize database
	var dbInitializer = scope.ServiceProvider
		.GetRequiredService<CreationDatabaseService>();

	await dbInitializer.InitializeAsync();

	// Initialize schedules
	var scheduleInitializer = scope.ServiceProvider
		.GetRequiredService<ScheduleInitializationService>();

	await scheduleInitializer.InitializeAsync();

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