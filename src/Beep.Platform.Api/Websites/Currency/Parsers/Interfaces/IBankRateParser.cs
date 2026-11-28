namespace Beep.Platform.Api.Websites.Currency.Parsers;

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

	/// <summary>
	/// True when the rates are a central bank's reference rates (the ECB's): one middle rate published for information,
	/// not a bank's buying and selling prices. The pages mark them so.
	/// </summary>
	bool IsReferenceRate => false;

	/// <param name="extractedJson">The collection result's ExtractedJson</param>
	/// <param name="collectedAt">When the page was collected</param>
	/// <param name="skipped">
	/// Gets a note for each currency left out because the page wrote one of its rates wrongly (a typo such as
	/// "15162.."); the bank's other currencies are still read. Only table parsers leave currencies out.
	/// </param>
	/// <exception cref="FormatException">The data is not this bank's rate table, or has no rates.</exception>
	IReadOnlyList<CurrencyRateBank> Parse(string extractedJson, DateTimeOffset collectedAt, ICollection<string>? skipped = null);
}
