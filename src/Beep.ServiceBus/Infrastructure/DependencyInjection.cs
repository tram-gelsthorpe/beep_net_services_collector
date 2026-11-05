using System;
using System.Threading.Tasks;
using Beep.ServiceBus.Database;
using Microsoft.Extensions.DependencyInjection;

namespace Beep.ServiceBus.Infrastructure
{
	public static class DependencyInjection
	{
		public static async Task CreateDatabase(this IServiceProvider provider)
		{
			using var scope = provider.CreateScope();
			var database = scope.ServiceProvider.GetRequiredService<IDatabase>();

			await database.Create();
		}
	}
}
