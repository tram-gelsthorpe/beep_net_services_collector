using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Beep.Platform.Api.Domain.Currency;

/// <summary>
/// Reads the exchange rates of Banque Raiffeisen (CCRALULL) in Luxembourg, from its public page
/// (https://raiffeisen.lu/fr/particuliers/gerer-au-quotidien/cours-de-change). The collector extracts the table's day
/// and time, then its cells, five to a row (code, transfer "Achat" and "Vente", banknote "Achat" and "Vente"; the
/// banknote cells are empty for a currency it has no notes of), written with a decimal comma:
/// <code>{"ContentSelector": ["27/11/2026", "07:15", "AUD", "1,654400", "1,637300", "", "", ..., "USD", "1,103500", "1,092000", "1,124500", "1,069600", ...]}</code>
/// <para>
/// Raiffeisen quotes each currency per euro: it buys a currency at the "Achat" rate (more units for a euro) and sells
/// it at the "Vente" rate. The site writes rates as the bank country's currency per unit of the foreign one, so they
/// are turned round: Raiffeisen buys a dollar at 1 / Achat euro, by transfer or in notes, and sells one at
/// 1 / Vente. The site keeps one selling rate per currency: the transfer one, as for the other banks; the banknote
/// selling rate is not kept.
/// </para>
/// <para>The rates are recorded at the day and time the table gives, in Luxembourg's time.</para>
/// </summary>
public sealed partial class RaiffeisenLuRateParser : IBankRateParser
{
	/// <summary>The one instance, in <see cref="BankRateParsers.All"/>.</summary>
	public static readonly RaiffeisenLuRateParser Instance = new();

	/// <summary>Banque Raiffeisen's BIC, as in <c>dbo.Bank.Bic</c>.</summary>
	public const string BankBic = "CCRALULL";

	/// <summary>Digits kept after the point when a rate is turned round.</summary>
	public const int Decimals = 10;

	private static readonly TimeZoneInfo Luxembourg = TimeZoneInfo.FindSystemTimeZoneById("Europe/Luxembourg");

	// The page's numbers: "1,103500", "175,854700".
	private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-LU");

	private RaiffeisenLuRateParser()
	{
	}

	public string Name => "raiffeisenlu";

	public string Bic => BankBic;

	public bool UsesCollectionTime => false;

	public IReadOnlyList<CurrencyRateBank> Parse(string extractedJson, DateTimeOffset collectedAt, ICollection<string>? skipped = null)
	{
		var cells = ReadCells(extractedJson);

		// The day and time come first ("27/11/2026", "07:15"), before the table's rows.
		var first = cells.FindIndex(c => CurrencyCode().IsMatch(c.Trim()));
		var head = first < 0 ? cells : cells.Take(first).ToList();
		DateOnly? day = null;
		TimeOnly? time = null;
		foreach (var cell in head.Select(c => c.Trim()))
		{
			if (DateOnly.TryParseExact(cell, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
				day = d;
			else if (TimeOnly.TryParseExact(cell, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
				time = t;
		}
		if (day is not { } date || time is not { } at)
			throw new FormatException("Raiffeisen's table day and time (\"27/11/2026\", \"07:15\") were not found before its rates");

		var local = date.ToDateTime(at);
		var rateTime = new DateTimeOffset(local, Luxembourg.GetUtcOffset(local));

		var rates = new List<CurrencyRateBank>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		for (var i = Math.Max(first, 0); i + 4 < cells.Count; i++)
		{
			var code = cells[i].Trim();
			if (!CurrencyCode().IsMatch(code))
				continue;

			var buyTransfer = cells[i + 1].Trim();
			var sellTransfer = cells[i + 2].Trim();
			var buyNotes = cells[i + 3].Trim();
			i += 4;
			if (code == "EUR" || !seen.Add(code))
				continue;

			if (!TryRate(buyTransfer, out var achat) || !TryRate(sellTransfer, out var vente))
			{
				skipped?.Add($"{BankBic} {code} is not a rate: '{buyTransfer}' / '{sellTransfer}'");
				continue;
			}

			// No banknotes of the currency: an empty cell.
			decimal? buyCash = null;
			if (buyNotes.Length > 0)
			{
				if (!TryRate(buyNotes, out var notes))
				{
					skipped?.Add($"{BankBic} {code} banknote rate is not a rate: '{buyNotes}'");
					continue;
				}
				buyCash = Turned(notes);
			}

			rates.Add(new CurrencyRateBank(code, date, rateTime, buyCash, Turned(achat), Turned(vente)));
		}

		if (rates.Count == 0)
			throw new FormatException("No Raiffeisen rates found in the collected data");
		return rates;
	}

	private static decimal Turned(decimal unitsPerEuro) => Math.Round(1m / unitsPerEuro, Decimals, MidpointRounding.AwayFromZero);

	private static bool TryRate(string text, out decimal rate) =>
		decimal.TryParse(text, NumberStyles.AllowDecimalPoint, French, out rate) && rate > 0;

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
				throw new FormatException("Expected {\"ContentSelector\": [cells]} for Raiffeisen");

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
