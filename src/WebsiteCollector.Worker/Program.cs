using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Options;
using WebsiteCollector.Infrastructure.Configuration;
using WebsiteCollector.Infrastructure.Database;
using WebsiteCollector.Infrastructure.Events;
using WebsiteCollector.Infrastructure.Http;
using WebsiteCollector.Features.WebsiteCollection.Services;
using WebsiteCollector.Features.WebsiteCollection.Repositories;
using WebsiteCollector.Features.WebsiteCollection.Models;
using WebsiteCollector.Features.WebsiteCollection.Subscribers;
using WebsiteCollector.Features.WebsiteCollection;
using WebsiteCollector.Features.SelectorManagement.Services;
using WebsiteCollector.Features.SelectorManagement.Models;
using WebsiteCollector.Features.LeaseManagement.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<CollectorOptions>(builder.Configuration.GetSection("Collector"));
builder.Services.Configure<WebhookDeliveryOptions>(builder.Configuration.GetSection("WebhookDelivery"));
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<IEventAggregator, EventAggregator>();
builder.Services.AddScoped<IWebsiteCollectionRepository, WebsiteCollectionRepository>();
builder.Services.AddScoped<IWebsiteSelectionWebhookRepository, WebsiteSelectionWebhookRepository>();
builder.Services.AddScoped<IWebhookDeliveryRepository, WebhookDeliveryRepository>();
builder.Services.AddScoped<IWebsiteCollectionService, WebsiteCollectionService>();
builder.Services.AddScoped<IWebhookDeliveryService, WebhookDeliveryService>();
builder.Services.AddScoped<ISelectorManagementService, SelectorManagementService>();
builder.Services.AddScoped<ILeaseManagementService, LeaseManagementService>();
builder.Services.AddScoped<ISubscriber, WebsiteSelectorsSubscriber>();
builder.Services.AddCollectorHttpClient();
builder.Services.AddHostedService<CollectorWorker>();
builder.Services.AddHostedService<WebhookDeliveryWorker>();

var host = builder.Build();

// Initialize event subscribers on startup
var subscribers = host.Services.GetRequiredService<IEnumerable<ISubscriber>>();
foreach (var subscriber in subscribers)
{
    await subscriber.RegisterAsync(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
}

await host.RunAsync();
