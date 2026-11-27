using System.Globalization;
using System.Text.Json;

namespace Beep.Api.Domain.Currency;

/// <summary>
/// Reads the rates the collector extracted from Vietcombank's exchange-rate page:
/// <code>
/// {"ContentSelector":{"Date":"2026-11-26T00:00:00","UpdatedDate":"2026-11-25T23:00:00+07:00",
///   "Data":[{"currencyCode":"USD","cash":"25610.00","transfer":"25640.00","sell":"26020.00"}, ...]}}
/// </code>
/// <c>cash</c> → BuyingRateCash, <c>transfer</c> → BuyingRateTransfer, <c>sell</c> → SellingRate.
/// The page gives its publication time (<c>UpdatedDate</c>), so the collection time is not used.
/// </summary>
public sealed class VietcombankRateParser : IBankRateParser
{
	/// <summary>The one instance, in <see cref="BankRateParsers.All"/>.</summary>
	public static readonly VietcombankRateParser Instance = new();

	private VietcombankRateParser()
	{
	}

	public string Name => "vietcombank";

	public string Bic => BankBic;

	public bool UsesCollectionTime => false;

	IReadOnlyList<CurrencyRateBank> IBankRateParser.Parse(string extractedJson, DateTimeOffset collectedAt, ICollection<string>? skipped) =>
		Parse(extractedJson);

	/// <summary>Vietcombank's BIC, as in <c>dbo.Bank.Bic</c>.</summary>
	public const string BankBic = "BFTVVNVX";

	/// <summary>Vietcombank's rates for one day: <c>RatesUrl + "2025-01-02"</c> (available from 2020-02-01).</summary>
	public const string RatesUrl = "https://www.vietcombank.com.vn/api/exchangerates?date=";

	/// <summary>Read a collection result: <c>{"ContentSelector": { rate list }}</c>.</summary>
	/// <exception cref="FormatException">The JSON is not in the expected shape.</exception>
	public static IReadOnlyList<CurrencyRateBank> Parse(string extractedJson) =>
		ParseJson(extractedJson, wrappedInContentSelector: true);

	/// <summary>
	/// Read a response of <see cref="RatesUrl"/>: the rate list itself. A day without rates
	/// (<c>"Count":0,"Data":[]</c>) gives an empty list.
	/// </summary>
	/// <exception cref="FormatException">The JSON is not in the expected shape.</exception>
	public static IReadOnlyList<CurrencyRateBank> ParseRateList(string json) =>
		ParseJson(json, wrappedInContentSelector: false);

	private static IReadOnlyList<CurrencyRateBank> ParseJson(string json, bool wrappedInContentSelector)
	{
		if (string.IsNullOrWhiteSpace(json))
			throw new FormatException("The data is empty");

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(json);
		}
		catch (JsonException ex)
		{
			throw new FormatException($"The data is not valid JSON: {ex.Message}", ex);
		}

		using (document)
		{
			var content = document.RootElement;
			if (wrappedInContentSelector
				&& (content.ValueKind != JsonValueKind.Object
					|| !content.TryGetProperty("ContentSelector", out content)))
				throw new FormatException("Expected an object with a ContentSelector object");

			if (content.ValueKind != JsonValueKind.Object)
				throw new FormatException("Expected the rate list to be an object");

			if (!content.TryGetProperty("Data", out var data) || data.ValueKind != JsonValueKind.Array)
				throw new FormatException("Expected a Data array");

			// "Date" is the day the rates apply to; "UpdatedDate" is when the bank published them.
			// Both are present even on a day without rates (0001-01-01).
			var date = DateOnly.FromDateTime(ParseDateTimeOffset(content, "Date").DateTime);
			var rateTime = ParseDateTimeOffset(content, "UpdatedDate");

			if (data.GetArrayLength() == 0)
				return [];

			var rates = new List<CurrencyRateBank>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var item in data.EnumerateArray())
			{
				var code = GetString(item, "currencyCode")?.Trim().ToUpperInvariant();
				if (code is not { Length: 3 } || !seen.Add(code))
					continue;

				rates.Add(new CurrencyRateBank(
					code,
					date,
					rateTime,
					BuyingRateCash: ParseRate(item, "cash", code),
					BuyingRateTransfer: ParseRate(item, "transfer", code),
					SellingRate: ParseRate(item, "sell", code)));
			}

			return rates;
		}
	}

	private static DateTimeOffset ParseDateTimeOffset(JsonElement content, string name)
	{
		var value = GetString(content, name);
		// A value without an offset (Date, "0001-01-01T00:00:00") is read as UTC so it keeps its clock date
		if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result))
			throw new FormatException($"{name} is missing or not a date: '{value}'");
		return result;
	}

	// "25,610.00" or 25610 -> 25610; empty or 0 -> null (0.00 is how the bank shows "not offered").
	private static decimal? ParseRate(JsonElement item, string name, string code)
	{
		if (!item.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
			return null;

		decimal rate;
		if (value.ValueKind == JsonValueKind.Number)
			rate = value.GetDecimal();
		else if (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))
			return null;
		else if (value.ValueKind != JsonValueKind.String
			|| !decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out rate))
			throw new FormatException($"{code} {name} is not a number: {value.GetRawText()}");

		return rate > 0 ? rate : null;
	}

	private static string? GetString(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
