using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Beep.Collector;

public static class HttpClientConfiguration
{
	public static IHttpClientBuilder AddCollectorHttpClient(this IServiceCollection services)
	{
		// Each request's timeout is the selector's TimeoutSeconds; every request sends WebsiteCollector:UserAgent.
		return services.AddHttpClient("collector", (provider, client) =>
			{
				client.Timeout = Timeout.InfiniteTimeSpan;
				client.DefaultRequestHeaders.UserAgent.ParseAdd(
					provider.GetRequiredService<IOptions<WebsiteCollectorOptions>>().Value.UserAgent);
			})
			.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
			{
				AllowAutoRedirect = false,
				AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
				PooledConnectionLifetime = TimeSpan.FromMinutes(5)
			});
	}
}
