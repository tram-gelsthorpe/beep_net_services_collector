using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Beep.Platform.Api.Websites.Currency.Parsers;

/// <summary>
/// Reads the Bank of England's (BKENGB2L) daily spot exchange rates, the United Kingdom's reference rates, from its
/// public table (https://www.bankofengland.co.uk/boeapps/database/Rates.asp?into=GBP&amp;rateview=D). The collector
/// extracts the table's day, then its cells, four to a row (currency name, the day's spot rate, the 52-week high and
/// low; a currency without a rate that day has empty cells):
/// <code>{"ContentSelector": ["£1 26 Nov 2026", "Australian Dollar", 1.9776, 2.0282, 1.8620, "Bulgarian Lev", "", "", "", ...]}</code>
/// <para>
/// The Bank quotes each currency per pound (£1 = 1.3110 US dollars). The site writes rates as the bank country's
/// currency per unit of the foreign one, so each rate is turned round: 1 USD = 0.7627765065 GBP. A spot rate is a
/// middle rate, not a bank's buying and selling prices: it is recorded as both the transfer buying rate and the
/// selling rate (a spread of 0), with no cash rate. The 52-week high and low are not kept.
/// </para>
/// <para>
/// The Bank publishes the rates on London's working days, as observed at about 16:00 in London, so they are recorded
/// at 16:00 in London on the day the table gives. On other days the table still shows the last day's rates, which are
/// then already recorded.
/// </para>
/// </summary>
public sealed partial class BankOfEnglandRateParser : IBankRateParser
{
	/// <summary>The one instance, in <see cref="BankRateParsers.All"/>.</summary>
	public static readonly BankOfEnglandRateParser Instance = new();

	/// <summary>The Bank of England's BIC, as in <c>dbo.Bank.Bic</c>.</summary>
	public const string BankBic = "BKENGB2L";

	/// <summary>Digits kept after the point when a rate is turned round.</summary>
	public const int Decimals = 10;

	// When the Bank observes its rates, in London's time.
	private static readonly TimeOnly PublishedAt = new(16, 0);
	private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

	/// <summary>The table's currency names, with their ISO 4217 codes.</summary>
	public static readonly IReadOnlyDictionary<string, string> Currencies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["Australian Dollar"] = "AUD",
		["Bulgarian Lev"] = "BGN",
		["Canadian Dollar"] = "CAD",
		["Chinese Yuan"] = "CNY",
		["Czech Koruna"] = "CZK",
		["Danish Krone"] = "DKK",
		["Euro"] = "EUR",
		["Hong Kong Dollar"] = "HKD",
		["Hungarian Forint"] = "HUF",
		["Indian Rupee"] = "INR",
		["Israeli Shekel"] = "ILS",
		["Japanese Yen"] = "JPY",
		["Malaysian Ringgit"] = "MYR",
		["New Zealand Dollar"] = "NZD",
		["Norwegian Krone"] = "NOK",
		["Polish Zloty"] = "PLN",
		["Romanian Leu"] = "RON",
		["Russian Ruble"] = "RUB",
		["Saudi Riyal"] = "SAR",
		["Singapore Dollar"] = "SGD",
		["South African Rand"] = "ZAR",
		["South Korean Won"] = "KRW",
		["Swedish Krona"] = "SEK",
		["Swiss Franc"] = "CHF",
		["Taiwan Dollar"] = "TWD",
		["Thai Baht"] = "THB",
		["Turkish Lira"] = "TRY",
		["US Dollar"] = "USD",
	};

	private BankOfEnglandRateParser()
	{
	}

	public string Name => "boe";

	public string Bic => BankBic;

	public bool UsesCollectionTime => false;

	public bool IsReferenceRate => true;

	public IReadOnlyList<CurrencyRateBank> Parse(string extractedJson, DateTimeOffset collectedAt, ICollection<string>? skipped = null)
	{
		var cells = ReadCells(extractedJson);

		// The day comes first, in the rate column's heading ("£1 26 Nov 2026"), before the rows.
		DateOnly? day = null;
		foreach (var cell in cells)
		{
			var text = Spaces().Replace(cell, " ").Trim();
			if (Currencies.ContainsKey(text))
				break;
			var found = Day().Match(text);
			if (found.Success && DateOnly.TryParseExact(found.Value, "d MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
			{
				day = parsed;
				break;
			}
		}
		if (day is not { } date)
			throw new FormatException("The Bank of England's day (\"£1 26 Nov 2026\") was not found before its rates");

		var local = date.ToDateTime(PublishedAt);
		var rateTime = new DateTimeOffset(local, London.GetUtcOffset(local));

		var rates = new List<CurrencyRateBank>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		for (var i = 0; i + 1 < cells.Count; i++)
		{
			if (!Currencies.TryGetValue(Spaces().Replace(cells[i], " ").Trim(), out var code))
				continue;

			var text = cells[i + 1].Trim();
			i += 3;
			if (text.Length == 0)
				continue; // No rate for the currency that day (the Bulgarian lev since Bulgaria took the euro).
			if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var perPound) || perPound <= 0)
			{
				skipped?.Add($"{BankBic} {code} is not a rate: '{text}'");
				continue;
			}
			if (!seen.Add(code))
				continue;

			var rate = Math.Round(1m / perPound, Decimals, MidpointRounding.AwayFromZero);
			rates.Add(new CurrencyRateBank(code, date, rateTime, BuyingRateCash: null, BuyingRateTransfer: rate, SellingRate: rate));
		}

		if (rates.Count == 0)
			throw new FormatException("No Bank of England rates found in the collected data");
		return rates;
	}

	/// <summary>The ContentSelector values as text (a number keeps its written form: 1.3110 stays "1.3110").</summary>
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
				throw new FormatException("Expected {\"ContentSelector\": [cells]} for the Bank of England");

			return [.. content.EnumerateArray().Select(cell => cell.ValueKind switch
			{
				JsonValueKind.String => cell.GetString() ?? "",
				JsonValueKind.Number => cell.GetRawText(),
				_ => "",
			})];
		}
	}

	[GeneratedRegex(@"\d{1,2} [A-Za-z]{3} \d{4}")]
	private static partial Regex Day();

	[GeneratedRegex(@"\s+")]
	private static partial Regex Spaces();
}
