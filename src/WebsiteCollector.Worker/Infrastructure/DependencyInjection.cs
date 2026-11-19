using Beep.EventBus;
using Beep.Scheduler;
using Beep.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WebsiteCollector.Worker
{
	public static class DependencyInjection
	{
		private const string SchedulerDatabaseName = "Scheduler";
		private const string ServiceBusDatabaseName = "ServiceBus";
		private const string EventBusDatabaseName = "EventBus";
		private const string SelectorDatabaseName = "Collector";

		public static IServiceCollection AddWebsiteCollector(this IServiceCollection services)
		{
			services.AddSingleton<ISelectorManagementService, SelectorManagementService>();
			services.AddSingleton<SelectorManagementRepository>();
			return services;
		}

		public static IServiceCollection RegisterServiceBus(this IServiceCollection services, IConfiguration configuration)
		{
			var websiteCollectorOption = configuration.GetSection(WebsiteCollectorOptions.Name);
			services.Configure<WebsiteCollectorOptions>(websiteCollectorOption);
			services.AddScoped(provider => websiteCollectorOption);

			// WebsiteCollector.Worker specific
			services.AddScoped<IWebsiteSelectorsRepository, WebsiteSelectorsRepository>
				(p => new WebsiteSelectorsRepository(configuration, 
					ServiceBusDatabaseName, 
					CreationDatabaseService.ServiceBusSchema,
					SelectorDatabaseName, 
					p.GetRequiredService<ILoggerFactory>()));
			
			services.AddScoped<IWebsiteSelectorsService, WebsiteSelectorsService>();
			//builder.Services.AddScoped<ISelectorManagementService, SelectorManagementService>();

			return services;
		}

		public static IServiceCollection RegisterEventBus(this IServiceCollection services, 
			IConfiguration configuration)
		{
			var eventBusOptions = new EventBusOptions();
			configuration.GetSection(EventBusOptions.Name).Bind(eventBusOptions);
			services.AddScoped(provider => eventBusOptions);

			services.AddScoped<IEventBusRepository, EventBusRepository>
				(p => new EventBusRepository(configuration.GetConnectionString(EventBusDatabaseName) ?? throw new InvalidOperationException("Missing EventBus connection string"), 
				CreationDatabaseService.EventBusSchema));

			services.AddSingleton<IEventAggregator, EventAggregator>();
			services.AddScoped<IEventBusService, EventBusService>();
			services.AddScoped<ILeaseManager<PendingEventBus>, EventBusLeaseManager>();
			services.AddScoped<IWorkProcessor<PendingEventBus>, EventBusWorkProcessor>();

			services.AddScoped<EventBusLeaseManager>();
			services.AddScoped<EventBusWorkProcessor>();

			// Register EventBusWorker with factory to provide ILogger
			services.AddHostedService(provider =>
				new EventBusWorker(
					provider.GetRequiredService<EventBusLeaseManager>(),
					provider.GetRequiredService<EventBusWorkProcessor>(),
					provider.GetRequiredService<EventBusOptions>(),
					provider.GetRequiredService<ILoggerFactory>().CreateLogger<EventBusWorker>()));

			return services;
		}

		public static IServiceCollection RegisterScheduler(this IServiceCollection services, IConfiguration configuration)
		{
			var schedulerOptionSection = new SchedulerOption();
			configuration.GetSection(SchedulerOption.Name).Bind(schedulerOptionSection);
			services.AddScoped(provider => schedulerOptionSection);

			var schedulerConnectionString = configuration.GetConnectionString(SchedulerDatabaseName)
				?? throw new InvalidOperationException("Missing Scheduler connection string for scheduler");

			var selectorConnectionString = configuration.GetConnectionString(SelectorDatabaseName)
				?? throw new InvalidOperationException("Missing Collector connection string for schedule handler");

			// Register Beep.Scheduler framework
			services.RegisterScheduler(schedulerConnectionString, 
				schedulerSchema: CreationDatabaseService.SchedulerSchema,
				pollIntervalSeconds: schedulerOptionSection.PollIntervalSeconds);

			// Register WebsiteCollector schedule handler
			services.AddScoped(provider =>
				new WebsiteCollectorScheduleHandler(
					provider.GetRequiredService<IWorkItemRepository>(),
					selectorConnectionString,
					provider.GetRequiredService<ILogger<WebsiteCollectorScheduleHandler>>()));

			services.AddScoped<IScheduleHandler>(provider =>
				provider.GetRequiredService<WebsiteCollectorScheduleHandler>());

			// Register schedule initialization service
			services.AddScoped(provider => new ScheduleInitializationService(
				provider.GetRequiredService<IScheduleService>(),
				schedulerOptionSection,
				provider.GetRequiredService<ILogger<ScheduleInitializationService>>()));

			return services;
		}
	}
}
