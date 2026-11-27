namespace Beep.Api.Domain.Currency;

/// <summary>
/// Reads one bank's rates from what the collector extracted from its rate page (a collection result's
/// <c>ExtractedJson</c>: <c>{"ContentSelector": ...}</c>).
/// </summary>
public interface IBankRateParser
{
	/// <summary>The bank's name in its endpoint's path: <c>POST api/CurrencyRate/currency/{name}</c>.</summary>
	string Name { get; }

	/// <summary>The bank's BIC, as in <c>dbo.Bank.Bic</c>.</summary>
	string Bic { get; }

	/// <summary>
	/// True when the page does not say when the rates were published, so they are recorded at the time they
	/// were collected; a rate that has not changed since the bank's previous one that day is then not recorded again.
	/// </summary>
	bool UsesCollectionTime { get; }

	/// <param name="extractedJson">The collection result's ExtractedJson</param>
	/// <param name="collectedAt">When the page was collected</param>
	/// <exception cref="FormatException">The data is not this bank's rate table, or has no rates.</exception>
	IReadOnlyList<CurrencyRateBank> Parse(string extractedJson, DateTimeOffset collectedAt);
}

/// <summary>The banks whose collected rate pages the API can read, by BIC.</summary>
public static class BankRateParsers
{
	/// <summary>Vietnam's time: the rate pages' days and times are local to it.</summary>
	public static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

	public static IReadOnlyList<IBankRateParser> All { get; } =
	[
		VietcombankRateParser.Instance,
		BankRateTable.Hsbc,
		BankRateTable.Woori,
		BankRateTable.Ocb,
		BankRateTable.Agribank,
		BankRateTable.BvBank,
		BankRateTable.VietABank,
		BankRateTable.VietinBank,
		BankRateTable.NamABank,
		BankRateTable.Ncb,
		new SeABankRateParser(),
	];

	private static readonly Dictionary<string, IBankRateParser> ByName =
		All.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

	/// <summary>The parser of the bank with that endpoint name, or null when the API cannot read its page.</summary>
	public static IBankRateParser? ForName(string name) => ByName.GetValueOrDefault(name.Trim());
}
