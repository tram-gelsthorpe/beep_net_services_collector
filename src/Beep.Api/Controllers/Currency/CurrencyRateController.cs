using Beep.Api.Domain.Currency;
using Beep.Api.Repositories.Collector;
using Beep.Api.Repositories.Currency;
using Microsoft.AspNetCore.Mvc;

namespace Beep.Api.Controllers.Currency;

/// <summary>
/// Receives collection results from the Beep.Collector EventBus and records the bank rates in
/// <c>dbo.Currency_Rate_Bank</c>. Each bank has its own endpoint, which fixes the bank (by BIC) and how
/// its collected JSON is read.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CurrencyRateController : ControllerBase
{
	private readonly ICollectedDataRepository _collectedData;
	private readonly ICurrencyRateRepository _currencyRates;
	private readonly ILogger<CurrencyRateController> _logger;

	public CurrencyRateController(
		ICollectedDataRepository collectedData,
		ICurrencyRateRepository currencyRates,
		ILogger<CurrencyRateController> logger)
	{
		_collectedData = collectedData;
		_currencyRates = currencyRates;
		_logger = logger;
	}

	/// <summary>
	/// Record Vietcombank's rates from a collection result (bank BIC BFTVVNVX).
	/// </summary>
	/// <response code="200">Rates recorded (or already recorded)</response>
	/// <response code="404">The collection result does not exist or has no extracted data</response>
	/// <response code="422">The extracted data is not Vietcombank's rate list</response>
	[HttpPost("currency/vietcombank")]
	public Task<IActionResult> RecordVietcombankCurrencyRate(
		[FromBody] CollectionDeliveryRequest request,
		CancellationToken cancellationToken) =>
		RecordAsync(VietcombankRateParser.BankBic, VietcombankRateParser.Parse, request, cancellationToken);

	private async Task<IActionResult> RecordAsync(
		string bankBic,
		Func<string, IReadOnlyList<CurrencyRateBank>> parse,
		CollectionDeliveryRequest request,
		CancellationToken cancellationToken)
	{
		if (request.CollectedDataId == Guid.Empty)
			return BadRequest(new { error = "CollectedDataId is required" });

		try
		{
			var extractedJson = await _collectedData.GetExtractedJsonAsync(request.CollectedDataId, cancellationToken);
			if (extractedJson is null)
			{
				_logger.LogWarning(
					"Collected data {CollectedDataId} of selector {SelectorId} not found or has no extracted data",
					request.CollectedDataId, request.WebsiteSelectorsId);
				return NotFound(new { error = $"No extracted data for collected data {request.CollectedDataId}" });
			}

			IReadOnlyList<CurrencyRateBank> rates;
			try
			{
				rates = parse(extractedJson);
			}
			catch (FormatException ex)
			{
				_logger.LogWarning(
					"Collected data {CollectedDataId} is not a {BankBic} rate list: {Error}",
					request.CollectedDataId, bankBic, ex.Message);
				return UnprocessableEntity(new { error = ex.Message });
			}

			var result = await _currencyRates.SaveBankRatesAsync(bankBic, rates, cancellationToken);

			_logger.LogInformation(
				"Recorded {BankBic} rates from collected data {CollectedDataId}: {Inserted} inserted, " +
				"{Duplicates} already recorded, unknown currencies: {UnknownCurrencies}",
				bankBic, request.CollectedDataId, result.Inserted, result.Duplicates,
				result.UnknownCurrencies.Count == 0 ? "none" : string.Join(", ", result.UnknownCurrencies));

			return Ok(result);
		}
		catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			_logger.LogError(ex, "Error recording {BankBic} rates from collected data {CollectedDataId}",
				bankBic, request.CollectedDataId);
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to record currency rates" });
		}
	}

	/// <summary>
	/// Body POSTed by the Beep.Collector EventBus when a selector's collection is saved.
	/// </summary>
	public sealed class CollectionDeliveryRequest
	{
		public required Guid WebsiteSelectorsId { get; init; }
		public required Guid CollectedDataId { get; init; }
		public DateTimeOffset CollectedAt { get; init; }
	}
}
