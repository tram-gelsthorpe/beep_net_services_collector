using Beep.Platform.Api.Websites.Currency.Parsers;
using Beep.Platform.Api.Websites.Currency.Repositories.Currency;

namespace Beep.Platform.Api.Websites.Currency.Backfill;

/// <summary>
/// Fills <c>dbo.Currency_Rate_Bank</c> with a bank's past rates: for each day in a range that has no rate of the
/// bank yet, fetches that day from the bank (<see cref="IBankHistory"/>) and records it with
/// <see cref="ICurrencyRateRepository"/> (the same rules as deliveries, so repeats are skipped).
/// </summary>
/// <remarks>
/// Each past day has one rate: the bank's end-of-day rate. Requests are spaced out to be polite to the bank.
/// Stopping and running again is safe: only days that are still missing are fetched.
/// </remarks>
public sealed class BankBackfill
{
	private static readonly TimeSpan[] DefaultRetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10)];

	private readonly HttpClient _http;
	private readonly ICurrencyRateRepository _repository;
	private readonly IBankHistory _bank;
	private readonly ILogger<BankBackfill> _logger;
	private readonly IReadOnlyList<TimeSpan> _retryDelays;

	/// <param name="retryDelays">Pauses before each retry of a failed request (default: 2s, then 10s)</param>
	public BankBackfill(
		HttpClient http,
		ICurrencyRateRepository repository,
		IBankHistory bank,
		ILogger<BankBackfill> logger,
		IReadOnlyList<TimeSpan>? retryDelays = null)
	{
		_retryDelays = retryDelays ?? DefaultRetryDelays;
		_http = http ?? throw new ArgumentNullException(nameof(http));
		_repository = repository ?? throw new ArgumentNullException(nameof(repository));
		_bank = bank ?? throw new ArgumentNullException(nameof(bank));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	/// <summary>Yesterday in Vietnam: the last day whose rates are final.</summary>
	public static DateOnly LastCompleteDay(DateTimeOffset now) =>
		DateOnly.FromDateTime(now.ToOffset(BankRateParsers.VietnamOffset).DateTime).AddDays(-1);

	/// <param name="from">First day to fill (not before the bank's <see cref="IBankHistory.FirstAvailableDate"/>)</param>
	/// <param name="to">Last day to fill</param>
	/// <param name="delayBetweenRequests">Pause between two requests to the bank</param>
	public async Task<BackfillResult> RunAsync(
		DateOnly from, DateOnly to, TimeSpan delayBetweenRequests, CancellationToken cancellationToken)
	{
		if (from < _bank.FirstAvailableDate)
			from = _bank.FirstAvailableDate;
		if (to < from)
			throw new ArgumentException($"The range {from:yyyy-MM-dd} to {to:yyyy-MM-dd} is empty", nameof(to));

		var recorded = await _repository.GetRecordedDatesAsync(_bank.Bic, from, to, cancellationToken);
		var missing = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
			.Select(from.AddDays)
			.Where(day => !recorded.Contains(day))
			.ToList();

		_logger.LogInformation(
			"{Bank} backfill {From:yyyy-MM-dd} to {To:yyyy-MM-dd}: {Missing} of {Days} days missing",
			_bank.Name, from, to, missing.Count, to.DayNumber - from.DayNumber + 1);

		var result = new BackfillResult { DaysMissing = missing.Count };
		var requests = 0;
		// The rates fetched for the day before, to tell a repeat of them from new rates (RepeatsPreviousRates).
		(DateOnly Day, IReadOnlyList<CurrencyRateBank> Rates)? previous = null;

		for (var i = 0; i < missing.Count; i++)
		{
			var day = missing[i];

			// The day before was not fetched in this run (it is recorded): fetch it to compare.
			if (_bank.RepeatsPreviousRates && previous?.Day != day.AddDays(-1) && day > _bank.FirstAvailableDate)
			{
				var before = await FetchRatesAsync(day.AddDays(-1), requests++ > 0, delayBetweenRequests, cancellationToken);
				previous = before is null ? null : (day.AddDays(-1), before);
			}

			var rates = await FetchRatesAsync(day, requests++ > 0, delayBetweenRequests, cancellationToken);
			if (rates is null)
			{
				result.FailedDays.Add(day);
				previous = null;
				continue;
			}

			var repeated = _bank.RepeatsPreviousRates && previous?.Day == day.AddDays(-1) && SameRates(previous.Value.Rates, rates);
			previous = (day, rates);

			if (rates.Count == 0 || repeated)
			{
				_logger.LogInformation(
					"{Day:yyyy-MM-dd}: {Bank} has no rates for this day{Repeated}", day, _bank.Name,
					repeated ? " (the day before's rates again)" : "");
				result.DaysWithoutRates++;
				continue;
			}

			if (rates[0].Date != day)
				_logger.LogWarning(
					"{Day:yyyy-MM-dd}: {Bank} returned the rates of {Date:yyyy-MM-dd}; recorded under that date",
					day, _bank.Name, rates[0].Date);

			var saved = await _repository.SaveBankRatesAsync(_bank.Bic, rates, cancellationToken);
			result.DaysRecorded++;
			result.RatesInserted += saved.Inserted;
			result.UnknownCurrencies.UnionWith(saved.UnknownCurrencies);

			if (result.DaysRecorded % 30 == 0 || i == missing.Count - 1)
				_logger.LogInformation(
					"{Bank} backfill: {Done} of {Missing} days done, up to {Day:yyyy-MM-dd} ({Inserted} rates inserted)",
					_bank.Name, i + 1, missing.Count, day, result.RatesInserted);
		}

		return result;
	}

	// The day's rates (empty: none that day), or null when the bank's answer could not be had or read.
	private async Task<IReadOnlyList<CurrencyRateBank>?> FetchRatesAsync(
		DateOnly day, bool pauseFirst, TimeSpan delay, CancellationToken cancellationToken)
	{
		if (pauseFirst)
			await Task.Delay(delay, cancellationToken);

		var (answered, body) = await FetchAsync(day, cancellationToken);
		if (!answered)
			return null;
		if (body is null)
			return [];

		try
		{
			var skipped = new List<string>();
			var rates = _bank.Parse(body, day, skipped);
			foreach (var note in skipped)
				_logger.LogWarning("{Day:yyyy-MM-dd}: {Bank}: a currency is left out: {Skipped}", day, _bank.Name, note);
			return rates;
		}
		catch (FormatException ex)
		{
			_logger.LogWarning("{Day:yyyy-MM-dd}: {Bank}'s answer is not a rate list: {Error}", day, _bank.Name, ex.Message);
			return null;
		}
	}

	// Answered: the day's response body, or a null body for a status that means no rates that day.
	// Not answered: the retries failed.
	private async Task<(bool Answered, string? Body)> FetchAsync(DateOnly day, CancellationToken cancellationToken)
	{
		for (var attempt = 0; ; attempt++)
		{
			string error;
			try
			{
				using var request = _bank.CreateRequest(day);
				using var response = await _http.SendAsync(request, cancellationToken);
				if (response.IsSuccessStatusCode)
					return (true, await response.Content.ReadAsStringAsync(cancellationToken));
				if (_bank.MeansNoRates(response.StatusCode))
					return (true, null);
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
				_logger.LogWarning("{Day:yyyy-MM-dd}: giving up on {Bank} after {Attempts} attempts: {Error}",
					day, _bank.Name, attempt + 1, error);
				return (false, null);
			}

			_logger.LogWarning("{Day:yyyy-MM-dd}: {Bank}: {Error}; retrying in {Delay}s",
				day, _bank.Name, error, _retryDelays[attempt].TotalSeconds);
			await Task.Delay(_retryDelays[attempt], cancellationToken);
		}
	}

	private static bool SameRates(IReadOnlyList<CurrencyRateBank> a, IReadOnlyList<CurrencyRateBank> b) =>
		a.Count > 0 && a.Count == b.Count
		&& a.Select(Key).OrderBy(k => k.Item1, StringComparer.Ordinal)
			.SequenceEqual(b.Select(Key).OrderBy(k => k.Item1, StringComparer.Ordinal));

	private static (string, decimal?, decimal?, decimal?) Key(CurrencyRateBank r) =>
		(r.CurrencyCode.ToUpperInvariant(), r.BuyingRateCash, r.BuyingRateTransfer, r.SellingRate);
}
