using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Beep.Api.Domain.Currency;

/// <summary>
/// Reads the exchange rates of SNS Bank (de Volksbank, SNSBNL2A) in the Netherlands: the rates it applies to transfers
/// in a foreign currency, from its public page
/// (https://www.snsbank.nl/particulier/betalen/actuele-wisselkoers-mijn-sns.html). The collector extracts the cells of
/// the page's rates table, four to a row (code, name, "aankoopkoers", "verkoopkoers"), written with a decimal comma:
/// <code>{"ContentSelector": ["AUD", "Australische dollar", "1,63595", "1,65595", ..., "USD", "Amerikaanse dollar", "1,09335", "1,10235", ...]}</code>
/// <para>
/// SNS quotes each currency per euro, as the units of it that one euro buys or brings. It sends a transfer in a foreign
/// currency at the "aankoopkoers" (its example: 100 dollars at 1,10325 cost 100 / 1,10325 = € 90,64) and changes money
/// received in one into euros at the "verkoopkoers". The site writes rates as the bank country's currency per unit of
/// the foreign one, so both are turned round: SNS sells a dollar at 1 / aankoopkoers euro (its selling rate) and buys
/// one at 1 / verkoopkoers (its transfer buying rate). It has no cash rates.
/// </para>
/// <para>
/// The page does not say when the rates were set (they follow Reuters' middle rate through the day), so they are
/// recorded at the time they were collected (<see cref="UsesCollectionTime"/>), on the day in Amsterdam.
/// </para>
/// </summary>
public sealed partial class SnsRateParser : IBankRateParser
{
	/// <summary>The one instance, in <see cref="BankRateParsers.All"/>.</summary>
	public static readonly SnsRateParser Instance = new();

	/// <summary>SNS Bank's (de Volksbank's) BIC, as in <c>dbo.Bank.Bic</c>.</summary>
	public const string BankBic = "SNSBNL2A";

	/// <summary>Digits kept after the point when a rate is turned round.</summary>
	public const int Decimals = 10;

	private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

	// The page's numbers: "1,09335", "365,68905".
	private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

	private SnsRateParser()
	{
	}

	public string Name => "sns";

	public string Bic => BankBic;

	public bool UsesCollectionTime => true;

	public IReadOnlyList<CurrencyRateBank> Parse(string extractedJson, DateTimeOffset collectedAt, ICollection<string>? skipped = null)
	{
		var cells = ReadCells(extractedJson);

		// To the minute, in Amsterdam's time.
		var local = TimeZoneInfo.ConvertTime(collectedAt, Amsterdam);
		local = local.AddTicks(-(local.Ticks % TimeSpan.TicksPerMinute));
		var date = DateOnly.FromDateTime(local.DateTime);

		var rates = new List<CurrencyRateBank>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		for (var i = 0; i + 3 < cells.Count; i++)
		{
			var code = cells[i].Trim();
			if (!CurrencyCode().IsMatch(code))
				continue;
			// A code seen before starts another table (the page's table of fees): the rates have ended.
			if (!seen.Add(code))
				break;

			var aankoop = cells[i + 2].Trim();
			var verkoop = cells[i + 3].Trim();
			i += 3;
			if (code == "EUR")
				continue;

			if (!TryRate(aankoop, out var unitsWhenSending) || !TryRate(verkoop, out var unitsWhenReceiving))
			{
				skipped?.Add($"{BankBic} {code} is not a rate: '{aankoop}' / '{verkoop}'");
				continue;
			}

			rates.Add(new CurrencyRateBank(code, date, local,
				BuyingRateCash: null,
				BuyingRateTransfer: Math.Round(1m / unitsWhenReceiving, Decimals, MidpointRounding.AwayFromZero),
				SellingRate: Math.Round(1m / unitsWhenSending, Decimals, MidpointRounding.AwayFromZero)));
		}

		if (rates.Count == 0)
			throw new FormatException("No SNS rates found in the collected data");
		return rates;
	}

	private static bool TryRate(string text, out decimal rate) =>
		decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands, Dutch, out rate) && rate > 0;

	/// <summary>The ContentSelector values as text.</summary>
	private static List<string> ReadCells(string extractedJson)
	{
		if (string.IsNullOrWhiteSpace(extractedJson))
			throw new FormatException("The data is empty");

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(extractedJson);
		}
		catch (JsonException ex)
		{
			throw new FormatException($"The data is not valid JSON: {ex.Message}", ex);
		}

		using (document)
		{
			if (document.RootElement.ValueKind != JsonValueKind.Object
				|| !document.RootElement.TryGetProperty("ContentSelector", out var content)
				|| content.ValueKind != JsonValueKind.Array)
				throw new FormatException("Expected {\"ContentSelector\": [cells]} for SNS");

			return [.. content.EnumerateArray().Select(cell => cell.ValueKind switch
			{
				JsonValueKind.String => cell.GetString() ?? "",
				JsonValueKind.Number => cell.GetRawText(),
				_ => "",
			})];
		}
	}

	[GeneratedRegex("^[A-Z]{3}$")]
	private static partial Regex CurrencyCode();
}
