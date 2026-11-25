using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Beep.Api.Repositories.Currency;

/// <summary>A bank that has at least one rate in <c>dbo.Currency_Rate_Bank</c>.</summary>
public sealed record BankWithRates(Guid CountryId, string Bic, string Name, DateTime LatestDate);

/// <summary>The latest rate of one bank for one currency.</summary>
public sealed record LatestBankRate(
	Guid CountryId,
	string Bic,
	string BankName,
	string CurrencyCode,
	string CurrencyName,
	DateTime Date,
	DateTimeOffset? RateTime,
	decimal? BuyingRateCash,
	decimal? BuyingRateTransfer,
	decimal? SellingRate);

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
}

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
			       RTRIM(c.CodeISO) AS CurrencyCode, c.Name AS CurrencyName,
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
