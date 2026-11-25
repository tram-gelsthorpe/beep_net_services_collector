using System;
using Microsoft.Extensions.DependencyInjection;

namespace Beep.EventBus;

public static class DependencyInjection
{
	/// <summary>
	/// Register the EventBus publisher and its delivery worker.
	/// The host must also register <see cref="Beep.ServiceBus.IWorkItemRepository"/>: deliveries are
	/// queued, claimed and retried through the ServiceBus.
	/// </summary>
	/// <param name="services">Service collection</param>
	/// <param name="connectionString">EventBus database (endpoints and deliveries)</param>
	/// <param name="schema">Schema holding the EventBus tables</param>
	/// <param name="options">Worker settings; WorkerType is the ServiceBus worker type of deliveries</param>
	public static IServiceCollection AddEventBus(
		this IServiceCollection services,
		string connectionString,
		string schema,
		EventBusOptions options)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
		ArgumentException.ThrowIfNullOrWhiteSpace(schema);
		ArgumentNullException.ThrowIfNull(options);
		options.Validate();

		services.AddSingleton(options);
		services.AddSingleton<IEventBusRepository>(new EventBusRepository(connectionString, schema));
		services.AddSingleton<IEventBusPublisher, EventBusPublisher>();

		services.AddHttpClient(EventBusSender.HttpClientName);
		services.AddSingleton<IEventBusSender, EventBusSender>();
		services.AddSingleton<EventBusWorkProcessor>();
		services.AddHostedService<EventBusWorker>();

		return services;
	}
}
