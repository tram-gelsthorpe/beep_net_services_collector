namespace Beep.Api.Infrastructure.Authentication;

/// <summary>
/// Middleware for API Key authentication.
/// Validates the X-API-Key header against configured API keys.
/// </summary>
public class ApiKeyMiddleware
{
	private readonly RequestDelegate _next;
	private readonly ILogger<ApiKeyMiddleware> _logger;
	private readonly ApiKeyValidator _validator;
	private const string ApiKeyHeader = "X-API-Key";

	public ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger, ApiKeyValidator validator)
	{
		_next = next;
		_logger = logger;
		_validator = validator;
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
			_logger.LogWarning("Request missing {Header} header. Path: {Path}", ApiKeyHeader, context.Request.Path);
			context.Response.StatusCode = StatusCodes.Status401Unauthorized;
			await context.Response.WriteAsJsonAsync(new { error = "API Key is required" });
			return;
		}

		// The keys are checked at startup (Program.cs), so there is always at least one.
		if (!_validator.IsValid(apiKeyValue.ToString()))
		{
			_logger.LogWarning("Invalid API key provided. Path: {Path}", context.Request.Path);
			context.Response.StatusCode = StatusCodes.Status401Unauthorized;
			await context.Response.WriteAsJsonAsync(new { error = "Invalid API Key" });
			return;
		}

		await _next(context);
	}
}
