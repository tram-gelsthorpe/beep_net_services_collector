using Beep.EventBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WebsiteCollector.Worker;
using WebsiteCollector.Worker.Features.SelectorManagement.Services;
using WebsiteCollector.Worker.Features.WebhookDelivery.Repositories;
using WebsiteCollector.Worker.Features.WebsiteCollector.Repositories;
using WebsiteCollector.Worker.Features.WebsiteCollector.Services;
using WebsiteCollector.Worker.Features.WebsiteCollector.Subscribers;
using WebsiteCollector.Worker.Infrastructure.Configuration;
using WebsiteCollector.Worker.Infrastructure.Database;
using WebsiteCollector.Worker.Infrastructure.Http;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<WebsiteCollectorOptions>(builder.Configuration.GetSection(WebsiteCollectorOptions.Name));
builder.Services.Configure<EventBusOptions>(builder.Configuration.GetSection(EventBusOptions.Name));

builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<IEventAggregator, EventAggregator>();
builder.Services.AddScoped<DatabaseInitializationService>();

// Beep.EventBus - Generic event bus infrastructure
// WebhookDeliveryRepository implements both IWebhookDeliveryRepository (project-specific) and IEventBusRepository (generic)
builder.Services.AddScoped<IEventBusDeliveryRepository, EventBusDeliveryRepository>();
builder.Services.AddScoped<IEventBusRepository>(sp => (IEventBusRepository)sp.GetRequiredService<IEventBusDeliveryRepository>());
builder.Services.AddScoped<IEventBusService, EventBusService>();

// WebsiteCollector.Worker specific
builder.Services.AddScoped<IWebsiteCollectionRepository, WebsiteCollectionRepository>();
builder.Services.AddScoped<IWebsiteSelectionWebhookRepository, WebsiteSelectionWebhookRepository>();
builder.Services.AddScoped<IWebsiteCollectionService, WebsiteCollectionService>();
builder.Services.AddScoped<ISelectorManagementService, SelectorManagementService>();
builder.Services.AddScoped<ISubscriber, WebsiteSelectorsSubscriber>();

builder.Services.AddCollectorHttpClient();

builder.Services.AddHostedService<WebsiteCollectorWorker>();
builder.Services.AddHostedService<WebhookDeliveryWorker>();

var host = builder.Build();

// Initialize databases on startup before any services access them
var dbInitializer = host.Services.GetRequiredService<DatabaseInitializationService>();
await dbInitializer.InitializeAsync();

// Initialize event subscribers on startup
var subscribers = host.Services.GetRequiredService<IEnumerable<ISubscriber>>();
foreach (var subscriber in subscribers)
{
    await subscriber.RegisterAsync(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
}

await host.RunAsync();
await host.RunAsync();


