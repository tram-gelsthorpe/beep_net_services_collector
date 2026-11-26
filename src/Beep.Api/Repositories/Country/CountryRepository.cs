using Dapper;
using Microsoft.Data.SqlClient;

namespace Beep.Api.Repositories.Country;

/// <summary>A country of <c>dbo.Country</c> (A_1_Beep_Global_Country).</summary>
/// <param name="CurrencyId">The country's currency in A_1_Beep_Global_Currency's <c>dbo.Currency</c>, if known</param>
public sealed record CountryInfo(Guid Id, string Code, string Name, Guid? CurrencyId);

/// <summary>Reads countries from the Country database (<c>ConnectionStrings:Country</c>).</summary>
public interface ICountryRepository
{
	/// <summary>All countries, by Id. Cached: countries rarely change.</summary>
	Task<IReadOnlyDictionary<Guid, CountryInfo>> GetCountriesAsync(CancellationToken cancellationToken);
}

public sealed class CountryRepository : ICountryRepository
{
	private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

	private readonly string _connectionString;
	private readonly SemaphoreSlim _refresh = new(1, 1);
	private IReadOnlyDictionary<Guid, CountryInfo>? _countries;
	private DateTimeOffset _loadedAt;

	public CountryRepository(string connectionString)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
		_connectionString = connectionString;
	}

	public async Task<IReadOnlyDictionary<Guid, CountryInfo>> GetCountriesAsync(CancellationToken cancellationToken)
	{
		if (_countries is { } cached && DateTimeOffset.UtcNow - _loadedAt < CacheDuration)
			return cached;

		await _refresh.WaitAsync(cancellationToken);
		try
		{
			if (_countries is { } loaded && DateTimeOffset.UtcNow - _loadedAt < CacheDuration)
				return loaded;

			await using var connection = new SqlConnection(_connectionString);
			var rows = await connection.QueryAsync<CountryInfo>(new CommandDefinition(
				"SELECT Id, RTRIM(Code) AS Code, ISNULL(Name, RTRIM(Code)) AS Name, Currency_Id AS CurrencyId FROM dbo.Country",
				cancellationToken: cancellationToken));

			_countries = rows.ToDictionary(c => c.Id);
			_loadedAt = DateTimeOffset.UtcNow;
			return _countries;
		}
		finally
		{
			_refresh.Release();
		}
	}
}
