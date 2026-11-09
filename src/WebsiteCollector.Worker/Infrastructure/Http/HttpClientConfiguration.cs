using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace WebsiteCollector.Worker;

public static class HttpClientConfiguration
{
	public static IHttpClientBuilder AddCollectorHttpClient(this IServiceCollection services)
	{
		return services.AddHttpClient("collector", c => c.Timeout = Timeout.InfiniteTimeSpan)
			.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
			{
				AllowAutoRedirect = false,
				AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
				PooledConnectionLifetime = TimeSpan.FromMinutes(5)
			});
	}
}
