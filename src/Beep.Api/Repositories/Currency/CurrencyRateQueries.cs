using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Beep.Api.Repositories.Currency;

public sealed class CurrencyRateQueries : ICurrencyRateQueries
{
	// A bank matches @Bank when its BIC equals it or its name contains it.
	private const string BankFilter = """
		(@CountryId IS NULL OR b.Country_Id = @CountryId)
		AND (@Bank IS NULL OR b.Bic = @Bank OR b.Name LIKE @BankPattern ESCAPE '\')
		""";

	private readonly string _connectionString;

	/// <param name="connectionString">A_1_Beep_Global_Currency (<c>ConnectionStrings:Currency</c>)</param>
	public CurrencyRateQueries(string connectionString)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
		_connectionString = connectionString;
	}

	public async Task<IReadOnlyList<BankWithRates>> GetBanksWithRatesAsync(
		Guid? countryId, string? bank, CancellationToken cancellationToken)
	{
		await using var connection = new SqlConnection(_connectionString);
		var rows = await connection.QueryAsync<BankWithRates>(new CommandDefinition($"""
			SELECT b.Country_Id AS CountryId, b.Bic, b.Name, r.LatestDate
			FROM dbo.Bank b
			CROSS APPLY (SELECT MAX(r.[Date]) AS LatestDate FROM dbo.Currency_Rate_Bank r WHERE r.Bank_Id = b.Id) r
			WHERE r.LatestDate IS NOT NULL AND b.Bic IS NOT NULL AND {BankFilter}
			ORDER BY b.Name
			""", Parameters(null, countryId, bank), cancellationToken: cancellationToken));
		return rows.ToList();
	}

	public async Task<IReadOnlyList<LatestBankRate>> GetLatestRatesAsync(
		DateOnly? date, Guid? countryId, string? bank, CancellationToken cancellationToken)
	{
		await using var connection = new SqlConnection(_connectionString);
		var rows = await connection.QueryAsync<LatestBankRate>(new CommandDefinition($"""
			SELECT b.Country_Id AS CountryId, b.Bic, b.Name AS BankName,
			       RTRIM(c.CodeISO) AS CurrencyCode, c.Name AS CurrencyName, NULLIF(LTRIM(RTRIM(c.Symbol)), N'') AS CurrencySymbol,
			       r.[Date], r.RateTime, r.BuyingRateCash, r.BuyingRateTransfer, r.SellingRate
			FROM dbo.Bank b
			CROSS JOIN dbo.Currency c
			CROSS APPLY (
				SELECT TOP (1) r.[Date], r.RateTime, r.BuyingRateCash, r.BuyingRateTransfer, r.SellingRate
				FROM dbo.Currency_Rate_Bank r
				WHERE r.Bank_Id = b.Id AND r.Currency_Id = c.Id AND (@Date IS NULL OR r.[Date] <= @Date)
				ORDER BY r.[Date] DESC, r.RateTime DESC) r
			WHERE b.Bic IS NOT NULL AND {BankFilter}
			  AND EXISTS (SELECT 1 FROM dbo.Currency_Rate_Bank x WHERE x.Bank_Id = b.Id)
			ORDER BY b.Name, c.CodeISO
			""", Parameters(date, countryId, bank), cancellationToken: cancellationToken));
		return rows.ToList();
	}

	public async Task<IReadOnlyDictionary<Guid, string>> GetCurrencyCodesAsync(
		IReadOnlyCollection<Guid> currencyIds, CancellationToken cancellationToken)
	{
		if (currencyIds.Count == 0)
			return new Dictionary<Guid, string>();

		await using var connection = new SqlConnection(_connectionString);
		var rows = await connection.QueryAsync<(Guid Id, string Code)>(new CommandDefinition(
			"SELECT Id, RTRIM(CodeISO) FROM dbo.Currency WHERE Id IN @ids",
			new { ids = currencyIds.Distinct().ToArray() }, cancellationToken: cancellationToken));
		return rows.ToDictionary(r => r.Id, r => r.Code);
	}

	private static DynamicParameters Parameters(DateOnly? date, Guid? countryId, string? bank)
	{
		bank = string.IsNullOrWhiteSpace(bank) ? null : bank.Trim();

		var parameters = new DynamicParameters();
		parameters.Add("Date", date?.ToDateTime(TimeOnly.MinValue), DbType.Date);
		parameters.Add("CountryId", countryId, DbType.Guid);
		parameters.Add("Bank", bank, DbType.String, size: 254);
		parameters.Add("BankPattern", bank is null ? null : "%" + EscapeLike(bank) + "%", DbType.String, size: 520);
		return parameters;
	}

	// The search text is matched literally: %, _ and [ have no special meaning.
	private static string EscapeLike(string value) =>
		value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_").Replace("[", @"\[");
}
