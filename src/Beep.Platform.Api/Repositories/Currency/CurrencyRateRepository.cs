using System.Data;
using Beep.Platform.Api.Domain.Currency;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Beep.Platform.Api.Repositories.Currency;

public sealed class CurrencyRateRepository : ICurrencyRateRepository
{
	// One currency's rate. UPDLOCK + HOLDLOCK keep the (bank, currency, day, time) range locked until
	// commit, so two deliveries of the same rate cannot both insert it.
	private const string SaveRateSql = """
		IF EXISTS (SELECT 1 FROM dbo.Currency_Rate_Bank WITH (UPDLOCK, HOLDLOCK)
		           WHERE Bank_Id = @BankId AND Currency_Id = @CurrencyId AND [Date] = @Date AND RateTime = @RateTime)
		BEGIN
			SELECT 0; -- repeated delivery
			RETURN;
		END

		IF @SkipUnchanged = 1 AND EXISTS (
			SELECT 1
			FROM (SELECT TOP (1) BuyingRateCash, BuyingRateTransfer, SellingRate
			      FROM dbo.Currency_Rate_Bank WITH (UPDLOCK, HOLDLOCK)
			      WHERE Bank_Id = @BankId AND Currency_Id = @CurrencyId AND [Date] = @Date AND RateTime < @RateTime
			      ORDER BY RateTime DESC) previous
			WHERE EXISTS (SELECT previous.BuyingRateCash, previous.BuyingRateTransfer, previous.SellingRate
			              INTERSECT SELECT @BuyingRateCash, @BuyingRateTransfer, @SellingRate))
		BEGIN
			SELECT 0; -- unchanged since the previous rate
			RETURN;
		END

		INSERT INTO dbo.Currency_Rate_Bank
			(Version, Created, CreatedBy, Currency_Id, Bank_Id, [Date], RateTime,
			 BuyingRateCash, BuyingRateTransfer, SellingRate)
		VALUES
			(1, SYSDATETIMEOFFSET(), @CreatedBy, @CurrencyId, @BankId, @Date, @RateTime,
			 @BuyingRateCash, @BuyingRateTransfer, @SellingRate);

		SELECT 1;
		""";

	private readonly string _connectionString;
	private readonly Guid _createdBy;

	/// <param name="connectionString">A_1_Beep_Global_Currency (<c>ConnectionStrings:Currency</c>)</param>
	/// <param name="createdBy">User recorded in CreatedBy (<c>Audit:CreatedBy</c>)</param>
	public CurrencyRateRepository(string connectionString, Guid createdBy)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
		if (createdBy == Guid.Empty)
			throw new ArgumentException("CreatedBy must be a user id, not an empty GUID", nameof(createdBy));

		_connectionString = connectionString;
		_createdBy = createdBy;
	}

	public async Task<IReadOnlySet<DateOnly>> GetRecordedDatesAsync(
		string bankBic, DateOnly from, DateOnly to, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(bankBic);

		var parameters = new DynamicParameters();
		parameters.Add("bic", new DbString { Value = bankBic, IsAnsi = true, Length = 20 });
		parameters.Add("from", from.ToDateTime(TimeOnly.MinValue), DbType.Date);
		parameters.Add("to", to.ToDateTime(TimeOnly.MinValue), DbType.Date);

		await using var connection = new SqlConnection(_connectionString);
		var dates = await connection.QueryAsync<DateTime>(new CommandDefinition("""
			SELECT DISTINCT r.[Date]
			FROM dbo.Currency_Rate_Bank r
			JOIN dbo.Bank b ON b.Id = r.Bank_Id
			WHERE b.Bic = @bic AND r.[Date] BETWEEN @from AND @to
			""", parameters, cancellationToken: cancellationToken));

		return dates.Select(DateOnly.FromDateTime).ToHashSet();
	}

	public async Task<CurrencyRateSaveResult> SaveBankRatesAsync(
		string bankBic, IReadOnlyList<CurrencyRateBank> rates, CancellationToken cancellationToken,
		bool skipUnchanged = false)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(bankBic);
		ArgumentNullException.ThrowIfNull(rates);

		await using var connection = new SqlConnection(_connectionString);
		await connection.OpenAsync(cancellationToken);
		await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

		var bankId = await GetBankIdAsync(connection, transaction, bankBic, cancellationToken);

		var codes = rates.Select(r => r.CurrencyCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		var currencyIds = codes.Count == 0
			? new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
			: (await connection.QueryAsync<(Guid Id, string CodeISO)>(new CommandDefinition(
					"SELECT Id, RTRIM(CodeISO) AS CodeISO FROM dbo.Currency WHERE CodeISO IN @codes",
					new { codes }, transaction, cancellationToken: cancellationToken)))
				.ToDictionary(c => c.CodeISO, c => c.Id, StringComparer.OrdinalIgnoreCase);

		int inserted = 0, duplicates = 0;
		var unknown = new List<string>();

		foreach (var rate in rates)
		{
			if (!currencyIds.TryGetValue(rate.CurrencyCode, out var currencyId))
			{
				unknown.Add(rate.CurrencyCode);
				continue;
			}

			var parameters = new DynamicParameters();
			parameters.Add("BankId", bankId);
			parameters.Add("CurrencyId", currencyId);
			parameters.Add("Date", rate.Date.ToDateTime(TimeOnly.MinValue), DbType.Date);
			parameters.Add("RateTime", rate.RateTime);
			parameters.Add("BuyingRateCash", rate.BuyingRateCash);
			parameters.Add("BuyingRateTransfer", rate.BuyingRateTransfer);
			parameters.Add("SellingRate", rate.SellingRate);
			parameters.Add("CreatedBy", _createdBy);
			parameters.Add("SkipUnchanged", skipUnchanged);

			var recorded = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
				SaveRateSql, parameters, transaction, cancellationToken: cancellationToken));

			if (recorded == 1)
				inserted++;
			else
				duplicates++;
		}

		await transaction.CommitAsync(cancellationToken);
		return new CurrencyRateSaveResult(inserted, duplicates, unknown);
	}

	private static async Task<Guid> GetBankIdAsync(
		SqlConnection connection, SqlTransaction transaction, string bankBic, CancellationToken cancellationToken)
	{
		var ids = (await connection.QueryAsync<Guid>(new CommandDefinition(
			"SELECT Id FROM dbo.Bank WHERE Bic = @bic",
			new { bic = new DbString { Value = bankBic, IsAnsi = true, Length = 20 } },
			transaction,
			cancellationToken: cancellationToken))).ToList();

		return ids.Count switch
		{
			1 => ids[0],
			0 => throw new InvalidOperationException($"No bank with BIC '{bankBic}' in dbo.Bank"),
			_ => throw new InvalidOperationException($"{ids.Count} banks have BIC '{bankBic}' in dbo.Bank"),
		};
	}
}
