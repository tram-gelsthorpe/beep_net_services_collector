using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace WebsiteCollector.Api.Infrastructure.Authentication;

/// <summary>
/// Middleware for API Key authentication.
/// Validates the X-API-Key header against configured API keys.
/// </summary>
public class ApiKeyMiddleware
{
	private readonly RequestDelegate _next;
	private readonly ILogger<ApiKeyMiddleware> _logger;
	private readonly IConfiguration _configuration;
	private const string ApiKeyHeader = "X-API-Key";

	public ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger, IConfiguration configuration)
	{
		_next = next;
		_logger = logger;
		_configuration = configuration;
	}

	public async Task InvokeAsync(HttpContext context)
	{
		// Skip authentication for Swagger endpoints
		if (context.Request.Path.StartsWithSegments("/swagger") || 
			context.Request.Path.StartsWithSegments("/api-docs") ||
			context.Request.Path.StartsWithSegments("/health"))
		{
			await _next(context);
			return;
		}

		if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var apiKeyValue))
		{
			_logger.LogWarning($"Request missing {ApiKeyHeader} header. Path: {context.Request.Path}");
			context.Response.StatusCode = StatusCodes.Status401Unauthorized;
			await context.Response.WriteAsJsonAsync(new { error = "API Key is required" });
			return;
		}

		var validApiKeys = _configuration.GetSection("Authentication:ApiKeys").Get<string[]>();
		if (validApiKeys == null || validApiKeys.Length == 0)
		{
			_logger.LogError("No valid API keys configured");
			context.Response.StatusCode = StatusCodes.Status500InternalServerError;
			await context.Response.WriteAsJsonAsync(new { error = "Server configuration error" });
			return;
		}

		if (!validApiKeys.Contains(apiKeyValue.ToString()))
		{
			_logger.LogWarning($"Invalid API key provided. Path: {context.Request.Path}");
			context.Response.StatusCode = StatusCodes.Status401Unauthorized;
			await context.Response.WriteAsJsonAsync(new { error = "Invalid API Key" });
			return;
		}

		await _next(context);
	}
}
