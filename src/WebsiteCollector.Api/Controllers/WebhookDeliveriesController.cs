using Microsoft.AspNetCore.Mvc;
using Beep.Webhook.Repository;

namespace WebsiteCollector.Api.Controllers;

/// <summary>
/// Webhook delivery endpoints for receiving confirmations from WebhookBus worker.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class WebhookDeliveriesController : ControllerBase
{
	private readonly IWebhookRepository _webhookRepository;
	private readonly ILogger<WebhookDeliveriesController> _logger;

	public WebhookDeliveriesController(IWebhookRepository webhookRepository, ILogger<WebhookDeliveriesController> logger)
	{
		_webhookRepository = webhookRepository;
		_logger = logger;
	}

	/// <summary>
	/// Record the result of a webhook delivery attempt.
	/// Called by WebhookBus worker after attempting delivery.
	/// </summary>
	[HttpPost("result")]
	public async Task<IActionResult> RecordDeliveryResult(
		[FromBody] RecordDeliveryResultRequest request,
		CancellationToken cancellationToken)
	{
		if (!ModelState.IsValid)
			return BadRequest(ModelState);

		try
		{
			_logger.LogInformation(
				"Recording webhook delivery result. DeliveryId: {DeliveryId}, Success: {Success}",
				request.DeliveryId, request.IsSuccessful);

			await _webhookRepository.RecordDeliveryResultAsync(
				request.DeliveryId,
				request.IsSuccessful,
				request.HttpStatusCode,
				request.ErrorMessage,
				request.ResponseContent,
				cancellationToken);

			return Ok(new { message = "Delivery result recorded successfully" });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error recording webhook delivery result. DeliveryId: {DeliveryId}", request.DeliveryId);
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to record delivery result" });
		}
	}

	/// <summary>
	/// Get the next batch of pending webhook deliveries.
	/// </summary>
	[HttpGet("pending")]
	public async Task<IActionResult> GetPendingDeliveries(
		[FromQuery] int batchSize = 10,
		CancellationToken cancellationToken = default)
	{
		if (batchSize <= 0 || batchSize > 100)
			return BadRequest(new { error = "batchSize must be between 1 and 100" });

		try
		{
			_logger.LogInformation("Retrieving pending deliveries. BatchSize: {BatchSize}", batchSize);
			var deliveries = await _webhookRepository.GetPendingDeliveriesAsync(batchSize, cancellationToken);

			return Ok(new
			{
				count = deliveries.Count,
				items = deliveries
			});
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving pending deliveries");
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to retrieve deliveries" });
		}
	}

	/// <summary>
	/// Get a specific delivery by ID.
	/// </summary>
	[HttpGet("{deliveryId:guid}")]
	public async Task<IActionResult> GetDeliveryById(
		Guid deliveryId,
		CancellationToken cancellationToken)
	{
		try
		{
			_logger.LogInformation("Retrieving delivery. DeliveryId: {DeliveryId}", deliveryId);
			var delivery = await _webhookRepository.GetDeliveryByIdAsync(deliveryId, cancellationToken);

			if (delivery == null)
				return NotFound(new { error = "Delivery not found" });

			return Ok(delivery);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving delivery. DeliveryId: {DeliveryId}", deliveryId);
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to retrieve delivery" });
		}
	}

	/// <summary>
	/// Abandon a delivery (mark as having no more retry attempts).
	/// </summary>
	[HttpPost("{deliveryId:guid}/abandon")]
	public async Task<IActionResult> AbandonDelivery(
		Guid deliveryId,
		[FromBody] AbandonDeliveryRequest request,
		CancellationToken cancellationToken)
	{
		if (!ModelState.IsValid)
			return BadRequest(ModelState);

		try
		{
			_logger.LogInformation("Abandoning delivery. DeliveryId: {DeliveryId}, Reason: {Reason}", deliveryId, request.Reason);
			await _webhookRepository.AbandonDeliveryAsync(deliveryId, request.Reason ?? "Manual abandonment", cancellationToken);

			return Ok(new { message = "Delivery abandoned successfully" });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error abandoning delivery. DeliveryId: {DeliveryId}", deliveryId);
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to abandon delivery" });
		}
	}

	/// <summary>
	/// Get the current attempt count for a delivery.
	/// </summary>
	[HttpGet("{deliveryId:guid}/attempt-count")]
	public async Task<IActionResult> GetAttemptCount(
		Guid deliveryId,
		CancellationToken cancellationToken)
	{
		try
		{
			_logger.LogInformation("Getting attempt count. DeliveryId: {DeliveryId}", deliveryId);
			var attemptCount = await _webhookRepository.GetDeliveryAttemptCountAsync(deliveryId, cancellationToken);

			return Ok(new { deliveryId, attemptCount });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error getting attempt count. DeliveryId: {DeliveryId}", deliveryId);
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to get attempt count" });
		}
	}
}

/// <summary>
/// Request model for recording delivery result.
/// </summary>
public class RecordDeliveryResultRequest
{
	public required Guid DeliveryId { get; set; }
	public required bool IsSuccessful { get; set; }
	public int? HttpStatusCode { get; set; }
	public string? ErrorMessage { get; set; }
	public string? ResponseContent { get; set; }
}

/// <summary>
/// Request model for abandoning a delivery.
/// </summary>
public class AbandonDeliveryRequest
{
	public string? Reason { get; set; }
}
