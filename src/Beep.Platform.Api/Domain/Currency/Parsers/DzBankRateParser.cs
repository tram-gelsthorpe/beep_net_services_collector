using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Beep.Platform.Api.Domain.Currency;

/// <summary>
/// Reads the foreign-exchange fixing of DZ BANK (GENODEFF), the central bank of Germany's Volksbanken and
/// Raiffeisenbanken, from its public price list D810 "Devisenhandel - Hauptwährungen"
/// (https://bvapps.vr-bankenportal.de/gla/devisenkurse/D810.html). The collector extracts the cells of the list's rows,
/// six to a row (pair, name, "Geld", "Mitte", "Brief", time), written with a decimal comma:
/// <code>{"ContentSelector": ["EURUSD", "US Dollar", "1,09300", "1,09650", "1,10000", "27.11.2026 13:07:15", ...]}</code>
/// <para>
/// Each pair is quoted per euro: the bank buys a euro for the "Geld" rate in the other currency and sells one for the
/// "Brief" rate. The site writes rates as the bank country's currency per unit of the foreign one, so both are turned
/// round: DZ BANK sells a dollar at 1 / Geld euro (its selling rate) and buys one at 1 / Brief (its transfer buying
/// rate). The middle rate is not kept, and there are no cash rates.
/// </para>
/// <para>Each row gives its own time, in Frankfurt's time: the rates are recorded at it.</para>
/// </summary>
public sealed partial class DzBankRateParser : IBankRateParser
{
	/// <summary>The one instance, in <see cref="BankRateParsers.All"/>.</summary>
	public static readonly DzBankRateParser Instance = new();

	/// <summary>DZ BANK's BIC, as in <c>dbo.Bank.Bic</c>.</summary>
	public const string BankBic = "GENODEFF";

	/// <summary>Digits kept after the point when a rate is turned round.</summary>
	public const int Decimals = 10;

	private static readonly TimeZoneInfo Frankfurt = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

	// The list's numbers: "1,09300", "174,49500".
	private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

	private DzBankRateParser()
	{
	}

	public string Name => "dzbank";

	public string Bic => BankBic;

	public bool UsesCollectionTime => false;

	public IReadOnlyList<CurrencyRateBank> Parse(string extractedJson, DateTimeOffset collectedAt, ICollection<string>? skipped = null)
	{
		var cells = ReadCells(extractedJson);

		var rates = new List<CurrencyRateBank>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		for (var i = 0; i + 5 < cells.Count; i++)
		{
			var pair = EuroPair().Match(cells[i].Trim());
			if (!pair.Success)
				continue;

			var code = pair.Groups[1].Value;
			var geld = cells[i + 2].Trim();
			var brief = cells[i + 4].Trim();
			var time = cells[i + 5].Trim();
			i += 5;
			if (!seen.Add(code))
				continue;

			if (!DateTime.TryParseExact(time, "dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
			{
				skipped?.Add($"{BankBic} {code} has no time: '{time}'");
				continue;
			}
			if (!TryRate(geld, out var unitsPerEuroBought) || !TryRate(brief, out var unitsPerEuroSold))
			{
				skipped?.Add($"{BankBic} {code} is not a rate: '{geld}' / '{brief}'");
				continue;
			}

			var rateTime = new DateTimeOffset(local, Frankfurt.GetUtcOffset(local));
			rates.Add(new CurrencyRateBank(code, DateOnly.FromDateTime(local), rateTime,
				BuyingRateCash: null,
				BuyingRateTransfer: Math.Round(1m / unitsPerEuroSold, Decimals, MidpointRounding.AwayFromZero),
				SellingRate: Math.Round(1m / unitsPerEuroBought, Decimals, MidpointRounding.AwayFromZero)));
		}

		if (rates.Count == 0)
			throw new FormatException("No DZ BANK rates found in the collected data");
		return rates;
	}

	private static bool TryRate(string text, out decimal rate) =>
		decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands, German, out rate) && rate > 0;

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
				throw new FormatException("Expected {\"ContentSelector\": [cells]} for DZ BANK");

			return [.. content.EnumerateArray().Select(cell => cell.ValueKind switch
			{
				JsonValueKind.String => cell.GetString() ?? "",
				JsonValueKind.Number => cell.GetRawText(),
				_ => "",
			})];
		}
	}

	// "EURUSD": the euro against the other currency.
	[GeneratedRegex("^EUR([A-Z]{3})$")]
	private static partial Regex EuroPair();
}
