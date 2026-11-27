using Beep.Api.Domain.Currency;
using System.Globalization;
using Beep.Api.Notifications;
using Beep.Notification;
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
	private readonly INotificationQueue _notifications;
	private readonly ILogger<CurrencyRateController> _logger;

	public CurrencyRateController(
		ICollectedDataRepository collectedData,
		ICurrencyRateRepository currencyRates,
		INotificationQueue notifications,
		ILogger<CurrencyRateController> logger)
	{
		_collectedData = collectedData;
		_currencyRates = currencyRates;
		_notifications = notifications;
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

		// Rates are quoted in the currency of the bank's country.
		var quoteCurrencies = await queries.GetCurrencyCodesAsync(
			rates.Select(r => all.GetValueOrDefault(r.CountryId)?.CurrencyId).OfType<Guid>().Distinct().ToList(),
			cancellationToken);

		return rates.Select(r =>
		{
			var c = all.GetValueOrDefault(r.CountryId);
			var quote = c?.CurrencyId is { } currencyId ? quoteCurrencies.GetValueOrDefault(currencyId) : null;
			return new LatestRateResponse(
				r.Bic, r.BankName, c?.Code, c?.Name, r.CurrencyCode, r.CurrencyName,
				DateOnly.FromDateTime(r.Date), r.RateTime, r.BuyingRateCash, r.BuyingRateTransfer, r.SellingRate,
				r.CurrencySymbol, quote);
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
	/// Records a bank's rates from a collection result of its rate page: <paramref name="bank"/> is its name in
	/// <see cref="BankRateParsers.All"/> (vietcombank, hsbc, woori, ocb, seabank, agribank, bvbank, vietabank,
	/// vietinbank, namabank, ncb). Pages that give no publication time are recorded at <c>CollectedAt</c>.
	/// </summary>
	/// <response code="200">Rates recorded (or already recorded)</response>
	/// <response code="400">No CollectedDataId; or no CollectedAt for a bank recorded at its collection time</response>
	/// <response code="404">No such bank, or the collection result does not exist or has no extracted data</response>
	/// <response code="422">The extracted data is not the bank's rate list</response>
	[HttpPost("currency/{bank}")]
	public Task<IActionResult> RecordBankCurrencyRate(
		string bank,
		[FromBody] CollectionDeliveryRequest request,
		CancellationToken cancellationToken)
	{
		var parser = BankRateParsers.ForName(bank);
		if (parser is null)
			return Task.FromResult<IActionResult>(NotFound(new { error = $"No rate page reader for bank '{bank}'" }));
		if (parser.UsesCollectionTime && request.CollectedAt == default)
			return Task.FromResult<IActionResult>(BadRequest(new { error = "CollectedAt is required" }));

		return RecordAsync(parser.Bic, json => parser.Parse(json, request.CollectedAt), parser.UsesCollectionTime,
			request, cancellationToken);
	}

	private async Task<IActionResult> RecordAsync(
		string bankBic,
		Func<string, IReadOnlyList<CurrencyRateBank>> parse,
		bool skipUnchanged,
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

			var result = await _currencyRates.SaveBankRatesAsync(bankBic, rates, cancellationToken, skipUnchanged);

			_logger.LogInformation(
				"Recorded {BankBic} rates from collected data {CollectedDataId}: {Inserted} inserted, " +
				"{Duplicates} already recorded, unknown currencies: {UnknownCurrencies}",
				bankBic, request.CollectedDataId, result.Inserted, result.Duplicates,
				result.UnknownCurrencies.Count == 0 ? "none" : string.Join(", ", result.UnknownCurrencies));

			// New rates: email the bank's subscribers after answering (redeliveries insert nothing).
			if (result.Inserted > 0)
				_notifications.Enqueue($"{bankBic} rate alerts", (services, ct) =>
					services.GetRequiredService<BankRatesAlerts>().SendAsync(bankBic, ct));

			return Ok(result);
		}
		catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			_logger.LogError(ex, "Error recording {BankBic} rates from collected data {CollectedDataId}",
				bankBic, request.CollectedDataId);
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to record currency rates" });
		}
	}
}
