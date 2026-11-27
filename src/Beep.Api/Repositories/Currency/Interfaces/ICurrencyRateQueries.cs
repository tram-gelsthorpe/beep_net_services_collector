namespace Beep.Api.Repositories.Currency;

/// <summary>Reads recorded rates from <c>dbo.Currency_Rate_Bank</c> (A_1_Beep_Global_Currency).</summary>
public interface ICurrencyRateQueries
{
	/// <summary>Banks with rates, optionally of one country and matching a BIC or part of a name.</summary>
	Task<IReadOnlyList<BankWithRates>> GetBanksWithRatesAsync(
		Guid? countryId, string? bank, CancellationToken cancellationToken);

	/// <summary>
	/// For each bank and currency, the latest rate on or before <paramref name="date"/> (or the latest of all
	/// when null): the highest Date, then the highest RateTime.
	/// </summary>
	/// <param name="bank">A BIC, or part of a bank name</param>
	Task<IReadOnlyList<LatestBankRate>> GetLatestRatesAsync(
		DateOnly? date, Guid? countryId, string? bank, CancellationToken cancellationToken);

	/// <summary>ISO codes of the given <c>dbo.Currency</c> ids (unknown ids are left out).</summary>
	Task<IReadOnlyDictionary<Guid, string>> GetCurrencyCodesAsync(
		IReadOnlyCollection<Guid> currencyIds, CancellationToken cancellationToken);
}
