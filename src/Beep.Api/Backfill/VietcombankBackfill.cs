using Beep.Api.Domain.Currency;
using Beep.Api.Repositories.Currency;

namespace Beep.Api.Backfill;

/// <summary>
/// Fills <c>dbo.Currency_Rate_Bank</c> with Vietcombank's past rates: for each day in a range that has no
/// Vietcombank rate yet, fetches <see cref="VietcombankRateParser.RatesUrl"/> for that day and records it
/// with <see cref="ICurrencyRateRepository"/> (the same rules as deliveries, so repeats are skipped).
/// </summary>
/// <remarks>
/// Each past day has one rate: the bank's end-of-day rate (UpdatedDate = that day 23:00 +07:00).
/// Requests are spaced out to be polite to Vietcombank. Stopping and running again is safe: only days
/// that are still missing are fetched.
/// </remarks>
public sealed class VietcombankBackfill
{
	/// <summary>First day Vietcombank's API has rates for.</summary>
	public static readonly DateOnly FirstAvailableDate = new(2020, 2, 1);

	private static readonly TimeSpan[] DefaultRetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10)];

	private readonly HttpClient _http;
	private readonly ICurrencyRateRepository _repository;
	private readonly ILogger<VietcombankBackfill> _logger;
	private readonly IReadOnlyList<TimeSpan> _retryDelays;

	/// <param name="retryDelays">Pauses before each retry of a failed request (default: 2s, then 10s)</param>
	public VietcombankBackfill(
		HttpClient http,
		ICurrencyRateRepository repository,
		ILogger<VietcombankBackfill> logger,
		IReadOnlyList<TimeSpan>? retryDelays = null)
	{
		_retryDelays = retryDelays ?? DefaultRetryDelays;
		_http = http ?? throw new ArgumentNullException(nameof(http));
		_repository = repository ?? throw new ArgumentNullException(nameof(repository));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	/// <summary>Yesterday in Vietnam: the last day whose rates are final.</summary>
	public static DateOnly LastCompleteDay(DateTimeOffset now) =>
		DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(7)).DateTime).AddDays(-1);

	/// <param name="from">First day to fill (not before <see cref="FirstAvailableDate"/>)</param>
	/// <param name="to">Last day to fill</param>
	/// <param name="delayBetweenRequests">Pause between two requests to Vietcombank</param>
	public async Task<BackfillResult> RunAsync(
		DateOnly from, DateOnly to, TimeSpan delayBetweenRequests, CancellationToken cancellationToken)
	{
		if (from < FirstAvailableDate)
			from = FirstAvailableDate;
		if (to < from)
			throw new ArgumentException($"The range {from:yyyy-MM-dd} to {to:yyyy-MM-dd} is empty", nameof(to));

		var recorded = await _repository.GetRecordedDatesAsync(
			VietcombankRateParser.BankBic, from, to, cancellationToken);
		var missing = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
			.Select(from.AddDays)
			.Where(day => !recorded.Contains(day))
			.ToList();

		_logger.LogInformation(
			"Vietcombank backfill {From:yyyy-MM-dd} to {To:yyyy-MM-dd}: {Missing} of {Days} days missing",
			from, to, missing.Count, to.DayNumber - from.DayNumber + 1);

		var result = new BackfillResult { DaysMissing = missing.Count };
		for (var i = 0; i < missing.Count; i++)
		{
			var day = missing[i];
			if (i > 0)
				await Task.Delay(delayBetweenRequests, cancellationToken);

			var json = await FetchAsync(day, cancellationToken);
			if (json is null)
			{
				result.FailedDays.Add(day);
				continue;
			}

			IReadOnlyList<CurrencyRateBank> rates;
			try
			{
				rates = VietcombankRateParser.ParseRateList(json);
			}
			catch (FormatException ex)
			{
				_logger.LogWarning("{Day:yyyy-MM-dd}: Vietcombank's response is not a rate list: {Error}", day, ex.Message);
				result.FailedDays.Add(day);
				continue;
			}

			if (rates.Count == 0)
			{
				_logger.LogInformation("{Day:yyyy-MM-dd}: Vietcombank has no rates for this day", day);
				result.DaysWithoutRates++;
				continue;
			}

			if (rates[0].Date != day)
				_logger.LogWarning(
					"{Day:yyyy-MM-dd}: Vietcombank returned the rates of {Date:yyyy-MM-dd}; recorded under that date",
					day, rates[0].Date);

			var saved = await _repository.SaveBankRatesAsync(VietcombankRateParser.BankBic, rates, cancellationToken);
			result.DaysRecorded++;
			result.RatesInserted += saved.Inserted;
			result.UnknownCurrencies.UnionWith(saved.UnknownCurrencies);

			if (result.DaysRecorded % 30 == 0 || i == missing.Count - 1)
				_logger.LogInformation(
					"Vietcombank backfill: {Done} of {Missing} days done, up to {Day:yyyy-MM-dd} ({Inserted} rates inserted)",
					i + 1, missing.Count, day, result.RatesInserted);
		}

		return result;
	}

	// The day's response, or null after the retries failed.
	private async Task<string?> FetchAsync(DateOnly day, CancellationToken cancellationToken)
	{
		var url = VietcombankRateParser.RatesUrl + day.ToString("yyyy-MM-dd");
		for (var attempt = 0; ; attempt++)
		{
			string error;
			try
			{
				using var response = await _http.GetAsync(url, cancellationToken);
				if (response.IsSuccessStatusCode)
					return await response.Content.ReadAsStringAsync(cancellationToken);
				error = $"HTTP {(int)response.StatusCode}";
			}
			catch (HttpRequestException ex)
			{
				error = ex.Message;
			}
			catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				error = "timed out";
			}

			if (attempt >= _retryDelays.Count)
			{
				_logger.LogWarning("{Day:yyyy-MM-dd}: giving up after {Attempts} attempts: {Error}", day, attempt + 1, error);
				return null;
			}

			_logger.LogWarning("{Day:yyyy-MM-dd}: {Error}; retrying in {Delay}s", day, error, _retryDelays[attempt].TotalSeconds);
			await Task.Delay(_retryDelays[attempt], cancellationToken);
		}
	}
}

public sealed class BackfillResult
{
	public int DaysMissing { get; set; }
	public int DaysRecorded { get; set; }
	public int DaysWithoutRates { get; set; }
	public int RatesInserted { get; set; }
	public List<DateOnly> FailedDays { get; } = [];
	public SortedSet<string> UnknownCurrencies { get; } = new(StringComparer.OrdinalIgnoreCase);
}
