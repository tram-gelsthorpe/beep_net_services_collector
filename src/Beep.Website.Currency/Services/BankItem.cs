namespace Beep.Website.Currency;

/// <param name="Name">The bank's full name</param>
/// <param name="ShortName">Its usual name ("Vietcombank"), if it has one</param>
public sealed record BankItem(string Bic, string Name, string? CountryCode, string? CountryName, DateOnly LatestDate, string? ShortName = null)
{
	/// <summary>The name to show first: the short name, else the full name.</summary>
	public string DisplayName => ShortName ?? Name;

	/// <summary>The full name to show under <see cref="DisplayName"/>: null when that is already the full name.</summary>
	public string? FullName => ShortName is null ? null : Name;

	/// <summary>The suggestion's text: the short name, then the full name.</summary>
	public string Label => FullName is { } full ? $"{DisplayName} · {full}" : Name;
}
