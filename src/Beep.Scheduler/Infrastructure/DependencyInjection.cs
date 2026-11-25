using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Beep.Scheduler;

/// <summary>
/// Extension methods for registering scheduler services
/// </summary>
public static class DependencyInjection
{
	/// <summary>
	/// Register the scheduler framework with dependency injection
	/// </summary>
	/// <param name="services">Service collection</param>
	/// <param name="connectionString">Database connection string for scheduler tables</param>
	/// <param name="schedulerSchema">Database schema that holds the scheduler tables (default: dbo)</param>
	/// <param name="pollIntervalSeconds">How often to poll for due schedules (default: 30)</param>
	/// <param name="claimLeaseSeconds">
	/// How long a due schedule stays claimed by the instance running it (default: 1 hour).
	/// Must be longer than the slowest handler run.
	/// </param>
	public static IServiceCollection RegisterScheduler(
		this IServiceCollection services,
		string connectionString,
		string schedulerSchema = "dbo",
		int pollIntervalSeconds = 30,
		int claimLeaseSeconds = 3600)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
		ArgumentException.ThrowIfNullOrWhiteSpace(schedulerSchema);

		// Stateless (connection per call), so safe to share with the hosted SchedulerService.
		services.AddSingleton(provider => new SchedulerRepository(
			connectionString,
			schedulerSchema,
			provider.GetRequiredService<ILogger<SchedulerRepository>>()));

		services.AddHostedService(provider => new SchedulerService(
			provider.GetRequiredService<SchedulerRepository>(),
			provider.GetRequiredService<IServiceScopeFactory>(),
			provider.GetRequiredService<ILogger<SchedulerService>>(),
			pollIntervalSeconds,
			claimLeaseSeconds));

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
