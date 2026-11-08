using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Beep.ServiceBus;

public static class DependencyInjection
{
	public static async Task CreateDatabase(this IServiceProvider provider)
	{
		using var scope = provider.CreateScope();
		var database = scope.ServiceProvider.GetRequiredService<IServiceBusDatabase>();

		await database.Create();
	}
}
