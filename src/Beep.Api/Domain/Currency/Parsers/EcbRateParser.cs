using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Beep.Api.Domain.Currency;

/// <summary>
/// Reads the euro reference rates of the European Central Bank, recorded as Belgium's rates under the National Bank
/// of Belgium (NBBEBEBB), which publishes the same rates. The collector extracts the day and the table of the ECB's
/// rates page (https://www.ecb.europa.eu/stats/policy_and_exchange_rates/euro_reference_exchange_rates/html/index.en.html):
/// <code>{"ContentSelector": ["27 November 2026", "USD", 1.0957, "JPY", 174.81, ...]}</code>
/// <para>
/// The ECB quotes each currency per euro (1 EUR = 1.0957 USD). The site writes rates as the bank country's currency per
/// unit of the foreign one (1 USD = 25,640 VND), so each rate is turned round: 1 USD = 0.9126585744 EUR.
/// A reference rate is one middle rate, not a bank's buying and selling prices: it is recorded as both the transfer
/// buying rate and the selling rate (a spread of 0), with no cash rate.
/// </para>
/// <para>
/// The ECB publishes the rates on its working days at about 16:00 Central European Time, so they are recorded at 16:00
/// in Brussels on the day the page gives. On other days the page still shows the last day's rates, which are then
/// already recorded.
/// </para>
/// </summary>
public sealed partial class EcbRateParser : IBankRateParser
{
	/// <summary>The one instance, in <see cref="BankRateParsers.All"/>.</summary>
	public static readonly EcbRateParser Instance = new();

	/// <summary>The National Bank of Belgium's BIC, as in <c>dbo.Bank.Bic</c>.</summary>
	public const string BankBic = "NBBEBEBB";

	/// <summary>Digits kept after the point when a rate is turned round.</summary>
	public const int Decimals = 10;

	// When the ECB publishes its rates, in Brussels' time.
	private static readonly TimeOnly PublishedAt = new(16, 0);
	private static readonly TimeZoneInfo Brussels = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

	private EcbRateParser()
	{
	}

	public string Name => "ecb";

	public string Bic => BankBic;

	public bool UsesCollectionTime => false;

	public bool IsReferenceRate => true;

	public IReadOnlyList<CurrencyRateBank> Parse(string extractedJson, DateTimeOffset collectedAt, ICollection<string>? skipped = null)
	{
		var cells = ReadCells(extractedJson);

		// The day comes first ("27 November 2026"), before the table.
		DateOnly? day = null;
		foreach (var cell in cells)
		{
			if (DateOnly.TryParseExact(cell.Trim(), "d MMMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
			{
				day = parsed;
				break;
			}
			if (CurrencyCode().IsMatch(cell.Trim()))
				break;
		}
		if (day is not { } date)
			throw new FormatException("The ECB page's day (\"27 November 2026\") was not found before its rates");

		var local = date.ToDateTime(PublishedAt);
		var rateTime = new DateTimeOffset(local, Brussels.GetUtcOffset(local));

		var rates = new List<CurrencyRateBank>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		for (var i = 0; i + 1 < cells.Count; i++)
		{
			var code = cells[i].Trim();
			if (!CurrencyCode().IsMatch(code) || code == "EUR")
				continue;

			var text = cells[i + 1].Trim();
			i++;
			if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var perEuro) || perEuro <= 0)
			{
				skipped?.Add($"{BankBic} {code} is not a rate: '{text}'");
				continue;
			}
			if (!seen.Add(code))
				continue;

			var rate = Math.Round(1m / perEuro, Decimals, MidpointRounding.AwayFromZero);
			rates.Add(new CurrencyRateBank(code, date, rateTime, BuyingRateCash: null, BuyingRateTransfer: rate, SellingRate: rate));
		}

		if (rates.Count == 0)
			throw new FormatException("No ECB reference rates found in the collected data");
		return rates;
	}

	/// <summary>The ContentSelector values as text (a number keeps its written form: 11.2080 stays "11.2080").</summary>
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
				throw new FormatException("Expected {\"ContentSelector\": [cells]} for the ECB");

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
