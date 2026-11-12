using Microsoft.AspNetCore.Mvc;
using WebsiteCollector.Api.Repositories;

namespace WebsiteCollector.Api.Controllers;

/// <summary>
/// Webhook delivery endpoints for receiving confirmations from WebhookBus worker.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CurrencyRateController : ControllerBase
{
	private readonly ICurrencyRateRepository _currencyRateRepository;
	private readonly ILogger<CurrencyRateController> _logger;

	public CurrencyRateController(ICurrencyRateRepository currencyRateRepository, ILogger<CurrencyRateController> logger)
	{
		_currencyRateRepository = currencyRateRepository;
		_logger = logger;
	}

	/// <summary>
	/// Record the result of a webhook delivery attempt.
	/// Called by WebhookBus worker after attempting delivery.
	/// </summary>
	[HttpPost("currency/vietcombank")]
	public async Task<IActionResult> RecordVietcombankCurrencyRate(
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

			await _currencyRateRepository.RecordVietcombankCurrencyRate(
				request,
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
}
