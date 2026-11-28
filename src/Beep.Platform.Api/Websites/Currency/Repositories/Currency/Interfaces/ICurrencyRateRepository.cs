using Beep.Platform.Api.Websites.Currency.Parsers;

namespace Beep.Platform.Api.Websites.Currency.Repositories.Currency;

/// <summary>
/// Records bank rates in <c>dbo.Currency_Rate_Bank</c> (A_1_Beep_Global_Currency).
/// </summary>
/// <remarks>
/// Each rate is a row for its bank, currency, <c>Date</c> and <c>RateTime</c>; earlier rows are never
/// changed. The latest rate of a day is the row with the highest <c>RateTime</c>. A rate whose time is
/// already recorded for that bank, currency and day is a repeated delivery and is skipped.
/// </remarks>
public interface ICurrencyRateRepository
{
	/// <summary>Days from <paramref name="from"/> to <paramref name="to"/> that have at least one rate of the bank.</summary>
	Task<IReadOnlySet<DateOnly>> GetRecordedDatesAsync(
		string bankBic, DateOnly from, DateOnly to, CancellationToken cancellationToken);

	/// <summary>Record the rates of the bank with BIC <paramref name="bankBic"/> (<c>dbo.Bank.Bic</c>).</summary>
	/// <param name="skipUnchanged">
	/// Also skip a rate equal to the bank's previous one of that currency and day (for rates timed by when
	/// they were collected, so collecting an unchanged page again records nothing)
	/// </param>
	/// <exception cref="InvalidOperationException">No bank, or more than one, has that BIC.</exception>
	Task<CurrencyRateSaveResult> SaveBankRatesAsync(
		string bankBic, IReadOnlyList<CurrencyRateBank> rates, CancellationToken cancellationToken,
		bool skipUnchanged = false);
}
