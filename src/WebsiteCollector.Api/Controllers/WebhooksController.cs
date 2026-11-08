using Microsoft.AspNetCore.Mvc;
using WebsiteCollector.Worker.Features.WebsiteCollector.Repositories;

namespace WebsiteCollector.Api.Controllers;

/// <summary>
/// Endpoints for managing website selection webhooks.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class WebhooksController : ControllerBase
{
	private readonly IWebsiteSelectionWebhookRepository _webhookRepository;
	private readonly ILogger<WebhooksController> _logger;

	public WebhooksController(IWebsiteSelectionWebhookRepository webhookRepository, ILogger<WebhooksController> logger)
	{
		_webhookRepository = webhookRepository;
		_logger = logger;
	}

	/// <summary>
	/// Get all webhooks for a website selector.
	/// </summary>
	[HttpGet("selector/{websiteSelectorsId:guid}")]
	public async Task<IActionResult> GetWebhooksBySelector(Guid websiteSelectorsId, CancellationToken cancellationToken)
	{
		try
		{
			_logger.LogInformation("Retrieving webhooks for selector. WebsiteSelectorsId: {WebsiteSelectorsId}", websiteSelectorsId);
			var webhooks = await _webhookRepository.GetByWebsiteSelectorsIdAsync(websiteSelectorsId, cancellationToken);
			return Ok(new { count = webhooks.Count, items = webhooks });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving webhooks for selector. WebsiteSelectorsId: {WebsiteSelectorsId}", websiteSelectorsId);
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to retrieve webhooks" });
		}
	}

	/// <summary>
	/// Register a new webhook for a website selector.
	/// </summary>
	[HttpPost]
	public async Task<IActionResult> RegisterWebhook(
		[FromBody] RegisterWebhookRequest request,
		CancellationToken cancellationToken)
	{
		if (!ModelState.IsValid)
			return BadRequest(ModelState);

		try
		{
			_logger.LogInformation("Registering webhook for selector. WebsiteSelectorsId: {WebsiteSelectorsId}", request.WebsiteSelectorsId);
			var webhookId = await _webhookRepository.RegisterWebhookAsync(
				request.WebsiteSelectorsId,
				request.WebhookUrl,
				cancellationToken);

			if (webhookId == Guid.Empty)
				return BadRequest(new { error = "Failed to register webhook or webhook already exists" });

			return CreatedAtAction(nameof(GetWebhooksBySelector), new { websiteSelectorsId = request.WebsiteSelectorsId }, 
				new { id = webhookId, message = "Webhook registered successfully" });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error registering webhook");
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to register webhook" });
		}
	}

	/// <summary>
	/// Update webhook status after a delivery attempt.
	/// </summary>
	[HttpPut("{webhookId:guid}/status")]
	public async Task<IActionResult> UpdateWebhookStatus(
		Guid webhookId,
		[FromBody] UpdateWebhookStatusRequest request,
		CancellationToken cancellationToken)
	{
		if (!ModelState.IsValid)
			return BadRequest(ModelState);

		try
		{
			_logger.LogInformation("Updating webhook status. WebhookId: {WebhookId}, HttpStatus: {HttpStatus}", webhookId, request.HttpStatusCode);
			await _webhookRepository.UpdateWebhookStatusAsync(webhookId, request.HttpStatusCode, request.ErrorMessage, cancellationToken);
			return Ok(new { message = "Webhook status updated successfully" });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error updating webhook status. WebhookId: {WebhookId}", webhookId);
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to update webhook status" });
		}
	}

	/// <summary>
	/// Disable a webhook.
	/// </summary>
	[HttpPost("{webhookId:guid}/disable")]
	public async Task<IActionResult> DisableWebhook(Guid webhookId, CancellationToken cancellationToken)
	{
		try
		{
			_logger.LogInformation("Disabling webhook. WebhookId: {WebhookId}", webhookId);
			await _webhookRepository.DisableWebhookAsync(webhookId, cancellationToken);
			return Ok(new { message = "Webhook disabled successfully" });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error disabling webhook. WebhookId: {WebhookId}", webhookId);
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to disable webhook" });
		}
	}
}

/// <summary>
/// Request model for registering a webhook.
/// </summary>
public class RegisterWebhookRequest
{
	public required Guid WebsiteSelectorsId { get; set; }
	public required string WebhookUrl { get; set; }
}

/// <summary>
/// Request model for updating webhook status.
/// </summary>
public class UpdateWebhookStatusRequest
{
	public int? HttpStatusCode { get; set; }
	public string? ErrorMessage { get; set; }
}
