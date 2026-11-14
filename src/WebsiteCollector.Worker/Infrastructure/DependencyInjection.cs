using Beep.EventBus;
using Beep.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WebsiteCollector.Worker
{
	public static class DependencyInjection
	{
		private const string ServiceBusDatabaseName = "ServiceBus";
		private const string EventBusDatabaseName = "EventBus";
		private const string SelectorDatabaseName = "Selector";

		public static IServiceCollection AddWebsiteCollector(this IServiceCollection services)
		{
			services.AddSingleton<ISelectorManagementService, SelectorManagementService>();
			services.AddSingleton<SelectorManagementRepository>();
			return services;
		}

		public static IServiceCollection RegisterServiceBus(this IServiceCollection services, IConfiguration configuration)
		{
			// WebsiteCollector.Worker specific
			services.AddScoped<IWebsiteSelectorsRepository, WebsiteSelectorsRepository>
				(p => new WebsiteSelectorsRepository(configuration, ServiceBusDatabaseName, SelectorDatabaseName, p.GetRequiredService<ILoggerFactory>()));
			
			services.AddScoped<IWebsiteSelectorsService, WebsiteSelectorsService>();
			//builder.Services.AddScoped<ISelectorManagementService, SelectorManagementService>();

			return services;
		}

		public static IServiceCollection RegisterEventBus(this IServiceCollection services, 
			IConfiguration configuration)
		{
			var eventBusOptions = configuration.GetSection(EventBusOptions.Name);
			services.Configure<EventBusOptions>(eventBusOptions);
			services.AddScoped(provider => eventBusOptions);

			services.AddScoped<IEventBusRepository, EventBusRepository>
				(p => new EventBusRepository(configuration.GetConnectionString(EventBusDatabaseName) ?? throw new InvalidOperationException("Missing EventBus connection string")));

			services.AddSingleton<IEventAggregator, EventAggregator>();
			services.AddScoped<IEventBusService, EventBusService>();
			services.AddScoped<ILeaseManager<PendingEventBus>, EventBusLeaseManager>();
			services.AddScoped<IWorkProcessor<PendingEventBus>, EventBusWorkProcessor>();

			services.AddScoped<EventBusLeaseManager>();
			services.AddScoped<EventBusWorkProcessor>();

			return services;
		}
	}
}
