using Beep.Scheduler.Handlers;
using Beep.Scheduler.Infrastructure.Database;
using Beep.Scheduler.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Beep.Scheduler.Infrastructure;

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
	/// <param name="pollIntervalSeconds">How often to poll for due schedules (default: 30)</param>
	public static IServiceCollection RegisterScheduler(
		this IServiceCollection services,
		string connectionString,
		int pollIntervalSeconds = 30)
	{
		if (string.IsNullOrWhiteSpace(connectionString))
			throw new ArgumentException("Connection string cannot be empty", nameof(connectionString));

		// Register database utility
		services.AddScoped(provider =>
		{
			var logger = provider.GetRequiredService<ILogger<SchedulerDatabase>>();
			return new SchedulerDatabase(connectionString, logger);
		});

		// Register scheduler service as hosted service
		services.AddHostedService(provider =>
		{
			var database = provider.GetRequiredService<SchedulerDatabase>();
			var logger = provider.GetRequiredService<ILogger<SchedulerService>>();
			return new SchedulerService(database, provider, logger, pollIntervalSeconds);
		});

		// Handler collection (initialize empty)
		services.AddSingleton<IHandlerRegistry>(new HandlerRegistry());

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
		services.AddScoped<THandler>();

		// Register so it can be discovered by type
		services.AddScoped<IScheduleHandler>(provider =>
			provider.GetRequiredService<THandler>());

		return services;
	}
}

/// <summary>
/// Registry for tracking handlers
/// </summary>
public interface IHandlerRegistry
{
	void Register(string workerType, Type handlerType);
	Type? Get(string workerType);
}

/// <summary>
/// Default handler registry implementation
/// </summary>
internal class HandlerRegistry : IHandlerRegistry
{
	private readonly Dictionary<string, Type> _handlers = new(StringComparer.OrdinalIgnoreCase);

	public void Register(string workerType, Type handlerType)
	{
		if (!typeof(IScheduleHandler).IsAssignableFrom(handlerType))
			throw new ArgumentException($"Handler type must implement IScheduleHandler", nameof(handlerType));

		_handlers[workerType] = handlerType;
	}

	public Type? Get(string workerType)
	{
		_handlers.TryGetValue(workerType, out var type);
		return type;
	}
}
