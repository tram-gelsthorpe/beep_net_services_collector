using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using WebsiteCollector.Worker.Features.SelectorManagement.Services;
using WebsiteCollector.Worker.Infrastructure.Events;
using WebsiteCollector.Worker.Infrastructure.Configuration;
using WebsiteCollector.Worker;
using WebsiteCollector.Worker.Infrastructure.Database;
using WebsiteCollector.Worker.Infrastructure.Http;
using WebsiteCollector.Worker.Features.WebsiteCollector.Repositories;
using WebsiteCollector.Worker.Features.WebsiteCollector.Subscribers;
using WebsiteCollector.Worker.Features.WebsiteCollector.Services;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<WebsiteCollectorOptions>(builder.Configuration.GetSection("Collector"));
builder.Services.Configure<WebhookDeliveryOptions>(builder.Configuration.GetSection("WebhookDelivery"));
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<IEventAggregator, EventAggregator>();
builder.Services.AddScoped<IWebsiteCollectionRepository, WebsiteCollectionRepository>();
builder.Services.AddScoped<IWebsiteSelectionWebhookRepository, WebsiteSelectionWebhookRepository>();
builder.Services.AddScoped<IWebhookDeliveryRepository, WebhookDeliveryRepository>();
builder.Services.AddScoped<IWebsiteCollectionService, WebsiteCollectionService>();
builder.Services.AddScoped<IWebhookDeliveryService, WebhookDeliveryService>();
builder.Services.AddScoped<ISelectorManagementService, SelectorManagementService>();
builder.Services.AddScoped<ISubscriber, WebsiteSelectorsSubscriber>();
builder.Services.AddCollectorHttpClient();
builder.Services.AddHostedService<WebsiteCollectorWorker>();
builder.Services.AddHostedService<WebhookDeliveryWorker>();

var host = builder.Build();

// Initialize event subscribers on startup
var subscribers = host.Services.GetRequiredService<IEnumerable<ISubscriber>>();
foreach (var subscriber in subscribers)
{
    await subscriber.RegisterAsync(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
}

await host.RunAsync();
