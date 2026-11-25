using Beep.Api.Domain.Currency;
using System.Globalization;
using Beep.Api.Repositories.Collector;
using Beep.Api.Repositories.Country;
using Beep.Api.Repositories.Currency;
using Microsoft.AspNetCore.Mvc;

namespace Beep.Api.Controllers.Currency;

/// <summary>
/// Bank currency rates in <c>dbo.Currency_Rate_Bank</c>. The POST endpoints receive collection results from
/// the Beep.Collector EventBus and record them (each bank has its own endpoint, which fixes the bank by BIC
/// and how its collected JSON is read). The GET endpoints read the recorded rates.
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

	/// <summary>Countries that have at least one bank with rates.</summary>
	[HttpGet("countries")]
	public async Task<ActionResult<IReadOnlyList<CountryResponse>>> GetCountries(
		[FromServices] ICurrencyRateQueries queries,
		[FromServices] ICountryRepository countries,
		CancellationToken cancellationToken)
	{
		var banks = await queries.GetBanksWithRatesAsync(null, null, cancellationToken);
		var all = await countries.GetCountriesAsync(cancellationToken);

		return banks.Select(b => b.CountryId).Distinct()
			.Select(id => all.TryGetValue(id, out var c) ? new CountryResponse(c.Code, c.Name) : null)
			.OfType<CountryResponse>()
			.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
			.ToList();
	}

	/// <summary>Banks with rates, optionally of a country and matching a BIC or part of a name.</summary>
	/// <param name="country">Country code, e.g. VN</param>
	/// <param name="bank">A BIC (BFTVVNVX) or part of a bank name (vietcombank)</param>
	/// <param name="queries">Rate queries</param>
	/// <param name="countries">Country names</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <response code="400">Unknown country code</response>
	[HttpGet("banks")]
	public async Task<ActionResult<IReadOnlyList<BankResponse>>> GetBanks(
		[FromQuery] string? country,
		[FromQuery] string? bank,
		[FromServices] ICurrencyRateQueries queries,
		[FromServices] ICountryRepository countries,
		CancellationToken cancellationToken)
	{
		var all = await countries.GetCountriesAsync(cancellationToken);
		if (!TryFindCountry(all, country, out var countryId))
			return BadRequest(new { error = $"Unknown country '{country}'" });

		var banks = await queries.GetBanksWithRatesAsync(countryId, bank, cancellationToken);
		return banks.Select(b =>
		{
			var c = all.GetValueOrDefault(b.CountryId);
			return new BankResponse(b.Bic, b.Name, c?.Code, c?.Name, DateOnly.FromDateTime(b.LatestDate));
		}).ToList();
	}

	/// <summary>
	/// The latest rate of each bank and currency on or before <paramref name="date"/> (default: the latest
	/// recorded), optionally of a country and of banks matching a BIC or part of a name.
	/// </summary>
	/// <param name="date">yyyy-MM-dd</param>
	/// <param name="country">Country code, e.g. VN</param>
	/// <param name="bank">A BIC (BFTVVNVX) or part of a bank name (vietcombank)</param>
	/// <param name="queries">Rate queries</param>
	/// <param name="countries">Country names</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <response code="400">Unknown country code, or a date that is not yyyy-MM-dd</response>
	[HttpGet("latest")]
	public async Task<ActionResult<IReadOnlyList<LatestRateResponse>>> GetLatestRates(
		[FromQuery] string? date,
		[FromQuery] string? country,
		[FromQuery] string? bank,
		[FromServices] ICurrencyRateQueries queries,
		[FromServices] ICountryRepository countries,
		CancellationToken cancellationToken)
	{
		DateOnly? onOrBefore = null;
		if (!string.IsNullOrWhiteSpace(date))
		{
			if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
				return BadRequest(new { error = $"'{date}' is not a date (yyyy-MM-dd)" });
			onOrBefore = parsed;
		}

		var all = await countries.GetCountriesAsync(cancellationToken);
		if (!TryFindCountry(all, country, out var countryId))
			return BadRequest(new { error = $"Unknown country '{country}'" });

		var rates = await queries.GetLatestRatesAsync(onOrBefore, countryId, bank, cancellationToken);
		return rates.Select(r =>
		{
			var c = all.GetValueOrDefault(r.CountryId);
			return new LatestRateResponse(
				r.Bic, r.BankName, c?.Code, c?.Name, r.CurrencyCode, r.CurrencyName,
				DateOnly.FromDateTime(r.Date), r.RateTime, r.BuyingRateCash, r.BuyingRateTransfer, r.SellingRate);
		}).ToList();
	}

	// No code: no country filter (true, null). An unknown code: false.
	private static bool TryFindCountry(IReadOnlyDictionary<Guid, CountryInfo> countries, string? code, out Guid? countryId)
	{
		countryId = null;
		if (string.IsNullOrWhiteSpace(code))
			return true;

		var match = countries.Values.FirstOrDefault(c => string.Equals(c.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
		countryId = match?.Id;
		return match is not null;
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

	public sealed record CountryResponse(string Code, string Name);

	public sealed record BankResponse(string Bic, string Name, string? CountryCode, string? CountryName, DateOnly LatestDate);

	/// <param name="Bic">Bank BIC</param>
	/// <param name="BankName">Bank name</param>
	/// <param name="CountryCode">Bank's country code</param>
	/// <param name="CountryName">Bank's country name</param>
	/// <param name="CurrencyCode">ISO currency code</param>
	/// <param name="CurrencyName">Currency name</param>
	/// <param name="Date">Day the rate applies to</param>
	/// <param name="RateTime">When the bank published it, in the bank's local time</param>
	/// <param name="BuyingRateCash">Bank buys cash (null: not offered)</param>
	/// <param name="BuyingRateTransfer">Bank buys by transfer</param>
	/// <param name="SellingRate">Bank sells</param>
	public sealed record LatestRateResponse(
		string Bic,
		string BankName,
		string? CountryCode,
		string? CountryName,
		string CurrencyCode,
		string CurrencyName,
		DateOnly Date,
		DateTimeOffset? RateTime,
		decimal? BuyingRateCash,
		decimal? BuyingRateTransfer,
		decimal? SellingRate);

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
