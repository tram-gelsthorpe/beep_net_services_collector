using Beep.EventBus;
using Beep.Scheduler;
using Beep.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WebsiteCollector.Worker
{
	public static class DependencyInjection
	{
	
		public static IServiceCollection AddWebsiteCollector(this IServiceCollection services)
		{
			services.AddSingleton<IWorkerIdProvider, WorkerIdProvider>();
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
			services.AddScoped<IWebsiteSelectorsRepository>
				(p => new WebsiteSelectorsRepository(configuration,
					p.GetRequiredService<IOptions<WebsiteCollectorOptions>>(),
					CreationDatabaseService.ServiceBusDatabaseName, 
					CreationDatabaseService.ServiceBusSchema,
					CreationDatabaseService.SelectorDatabaseName,
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
				(p => new EventBusRepository(configuration.GetConnectionString(CreationDatabaseService.EventBusDatabaseName) ?? throw new InvalidOperationException("Missing EventBus connection string"), 
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

			var serviceBusConnectionString = configuration.GetConnectionString(CreationDatabaseService.ServiceBusDatabaseName)
				?? throw new InvalidOperationException("Missing ServiceBus connection string for scheduler");

			var schedulerConnectionString = configuration.GetConnectionString(CreationDatabaseService.SchedulerDatabaseName)
				?? throw new InvalidOperationException("Missing Scheduler connection string for scheduler");

			var selectorConnectionString = configuration.GetConnectionString(CreationDatabaseService.SelectorDatabaseName)
				?? throw new InvalidOperationException("Missing Collector connection string for schedule handler");

			// Register IWorkItemRepository for schedule handlers
			services.AddScoped<IWorkItemRepository>(provider =>
				new WorkItemRepository(
					serviceBusConnectionString,
					CreationDatabaseService.ServiceBusSchema,
					provider.GetRequiredService<ILogger<WorkItemRepository>>()));

			// Register Beep.Scheduler framework
			services.RegisterScheduler(schedulerConnectionString, 
				schedulerSchema: CreationDatabaseService.SchedulerSchema,
				pollIntervalSeconds: schedulerOptionSection.PollIntervalSeconds);

			// Register WebsiteCollector schedule handler
			services.AddScoped<IScheduleHandler>(provider =>
				new WebsiteCollectorScheduleHandler(
					provider.GetRequiredService<IWorkItemRepository>(),
					selectorConnectionString,
					schedulerOptionSection.WorkerType,
					provider.GetRequiredService<IWorkerIdProvider>().WorkerId,
					provider.GetRequiredService<ILogger<WebsiteCollectorScheduleHandler>>()));

			// Register schedule initialization service
			services.AddScoped(provider => new ScheduleInitializationService(
				provider.GetRequiredService<IScheduleService>(),
				schedulerOptionSection,
				provider.GetRequiredService<ILogger<ScheduleInitializationService>>()));

			return services;
		}
	}
}
