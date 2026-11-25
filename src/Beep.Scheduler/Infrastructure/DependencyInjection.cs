using Beep.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Beep.Scheduler;

/// <summary>
/// Extension methods for registering scheduler services
/// </summary>
public static class DependencyInjection
{
	/// <summary>
	/// Register the scheduler framework with dependency injection.
	/// The host must also register <see cref="IWorkItemRepository"/>: each due schedule is run as a
	/// ServiceBus work item, which gives one run per schedule across any number of instances, lease
	/// renewal while a handler runs, crash recovery and retries.
	/// </summary>
	/// <param name="services">Service collection</param>
	/// <param name="connectionString">Database connection string for scheduler tables</param>
	/// <param name="schedulerSchema">Database schema that holds the scheduler tables (default: dbo)</param>
	/// <param name="pollIntervalSeconds">How often to look for due schedules (default: 30)</param>
	/// <param name="workerOptions">How schedule work items are run (default: <see cref="SchedulerWorkerOptions"/>)</param>
	public static IServiceCollection RegisterScheduler(
		this IServiceCollection services,
		string connectionString,
		string schedulerSchema = "dbo",
		int pollIntervalSeconds = 30,
		SchedulerWorkerOptions? workerOptions = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
		ArgumentException.ThrowIfNullOrWhiteSpace(schedulerSchema);

		workerOptions ??= new SchedulerWorkerOptions();
		workerOptions.Validate();
		services.AddSingleton(workerOptions);

		// Stateless (connection per call), so safe to share with the hosted services.
		services.AddSingleton(provider => new SchedulerRepository(
			connectionString,
			schedulerSchema,
			provider.GetRequiredService<ILogger<SchedulerRepository>>()));

		// Finds due schedules and queues them; the worker runs them.
		services.AddHostedService(provider => new SchedulerService(
			provider.GetRequiredService<SchedulerRepository>(),
			provider.GetRequiredService<IWorkItemRepository>(),
			workerOptions,
			provider.GetRequiredService<ILogger<SchedulerService>>(),
			pollIntervalSeconds));

		services.AddSingleton<SchedulerWorkProcessor>();
		services.AddHostedService<SchedulerWorker>();

		services.AddScoped<IScheduleService, ScheduleManagementService>();

		return services;
	}

	/// <summary>
	/// Register a schedule handler for a specific worker type
	/// </summary>
	/// <typeparam name="THandler">Handler implementation type</typeparam>
	/// <param name="services">Service collection</param>
	public static IServiceCollection RegisterScheduleHandler<THandler>(
		this IServiceCollection services)
		where THandler : class, IScheduleHandler
	{
		services.AddScoped<IScheduleHandler, THandler>();
		return services;
	}
}
