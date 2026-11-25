using Beep.EventBus;
using Beep.Scheduler;
using Beep.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WebsiteCollector.Worker;

/// <summary>
/// Service registrations for the WebsiteCollector worker.
/// Repositories and services are stateless (they open a connection per call), so they are
/// registered as singletons and can be consumed safely by the hosted background workers.
/// </summary>
public static class DependencyInjection
{
	public static IServiceCollection RegisterServiceBus(this IServiceCollection services, IConfiguration configuration)
	{
		var serviceBusConnectionString = GetConnectionString(configuration, CreationDatabaseService.ServiceBusDatabaseName);
		var collectorConnectionString = GetConnectionString(configuration, CreationDatabaseService.SelectorDatabaseName);

		services.Configure<WebsiteCollectorOptions>(configuration.GetSection(WebsiteCollectorOptions.Name));
		services.AddSingleton<IWorkerIdProvider, WorkerIdProvider>();

		services.AddSingleton(p => new SelectorManagementRepository(
			collectorConnectionString,
			p.GetRequiredService<ILogger<SelectorManagementRepository>>()));

		services.AddSingleton<IWorkItemRepository>(p => new WorkItemRepository(
			serviceBusConnectionString,
			CreationDatabaseService.ServiceBusSchema,
			p.GetRequiredService<ILogger<WorkItemRepository>>()));

		services.AddSingleton<IWebsiteSelectorsRepository, WebsiteSelectorsRepository>();
		services.AddSingleton<IWebsiteSelectorsService, WebsiteSelectorsService>();

		return services;
	}

	public static IServiceCollection RegisterEventBus(this IServiceCollection services, IConfiguration configuration)
	{
		var eventBusConnectionString = GetConnectionString(configuration, CreationDatabaseService.EventBusDatabaseName);

		var eventBusOptions = new EventBusOptions();
		configuration.GetSection(EventBusOptions.Name).Bind(eventBusOptions);
		services.AddSingleton(eventBusOptions);

		services.AddSingleton<IEventBusRepository>(new EventBusRepository(eventBusConnectionString, CreationDatabaseService.EventBusSchema));
		services.AddSingleton<IEventAggregator, EventAggregator>();
		services.AddSingleton<IEventBusService, EventBusService>();
		services.AddSingleton<EventBusLeaseManager>();
		services.AddSingleton<EventBusWorkProcessor>();
		services.AddHostedService<EventBusWorker>();

		return services;
	}

	public static IServiceCollection RegisterScheduler(this IServiceCollection services, IConfiguration configuration)
	{
		var schedulerConnectionString = GetConnectionString(configuration, CreationDatabaseService.SchedulerDatabaseName);

		var schedulerOption = new SchedulerOption();
		configuration.GetSection(SchedulerOption.Name).Bind(schedulerOption);

		services.RegisterScheduler(
			schedulerConnectionString,
			schedulerSchema: CreationDatabaseService.SchedulerSchema,
			pollIntervalSeconds: schedulerOption.PollIntervalSeconds);

		services.AddScoped<IScheduleHandler>(p => new WebsiteCollectorScheduleHandler(
			p.GetRequiredService<IWorkItemRepository>(),
			p.GetRequiredService<SelectorManagementRepository>(),
			p.GetRequiredService<IOptions<WebsiteCollectorOptions>>().Value.WorkerType,
			p.GetRequiredService<IWorkerIdProvider>().WorkerId,
			p.GetRequiredService<ILogger<WebsiteCollectorScheduleHandler>>()));

		services.AddScoped(p => new ScheduleInitializationService(
			p.GetRequiredService<IScheduleService>(),
			schedulerOption,
			p.GetRequiredService<ILogger<ScheduleInitializationService>>()));

		return services;
	}

	private static string GetConnectionString(IConfiguration configuration, string name) =>
		configuration.GetConnectionString(name)
			?? throw new InvalidOperationException($"Missing '{name}' connection string");
}
