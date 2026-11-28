namespace Beep.Platform.Api.Repositories.Country;

/// <summary>Reads countries from the Country database (<c>ConnectionStrings:Country</c>).</summary>
public interface ICountryRepository
{
	/// <summary>All countries, by Id. Cached: countries rarely change.</summary>
	Task<IReadOnlyDictionary<Guid, CountryInfo>> GetCountriesAsync(CancellationToken cancellationToken);
}
