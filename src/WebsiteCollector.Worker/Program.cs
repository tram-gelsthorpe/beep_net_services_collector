using Beep.EventBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WebsiteCollector.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<WebsiteCollectorOptions>(builder.Configuration.GetSection(WebsiteCollectorOptions.Name));
builder.Services.Configure<EventBusOptions>(builder.Configuration.GetSection(EventBusOptions.Name));

builder.Services.AddScoped<CreationDatabaseService>();

builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<IEventAggregator, EventAggregator>();


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
builder.Services.AddHostedService<EventBusWorker>();

var host = builder.Build();

// Initialize databases on startup before any services access them
var dbInitializer = host.Services.GetRequiredService<CreationDatabaseService>();
await dbInitializer.InitializeAsync();

// Initialize event subscribers on startup
var subscribers = host.Services.GetRequiredService<IEnumerable<ISubscriber>>();
foreach (var subscriber in subscribers)
{
    await subscriber.RegisterAsync(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
}

await host.RunAsync();
await host.RunAsync();


