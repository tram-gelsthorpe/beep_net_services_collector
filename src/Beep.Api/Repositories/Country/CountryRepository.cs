using Dapper;
using Microsoft.Data.SqlClient;

namespace Beep.Api.Repositories.Country;

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
