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
/// </summary>
public static class VietcombankRateParser
{
	/// <summary>Vietcombank's BIC, as in <c>dbo.Bank.Bic</c>.</summary>
	public const string BankBic = "BFTVVNVX";

	/// <exception cref="FormatException">The JSON is not in the expected shape.</exception>
	public static IReadOnlyList<CurrencyRateBank> Parse(string extractedJson)
	{
		if (string.IsNullOrWhiteSpace(extractedJson))
			throw new FormatException("The extracted data is empty");

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(extractedJson);
		}
		catch (JsonException ex)
		{
			throw new FormatException($"The extracted data is not valid JSON: {ex.Message}", ex);
		}

		using (document)
		{
			if (document.RootElement.ValueKind != JsonValueKind.Object
				|| !document.RootElement.TryGetProperty("ContentSelector", out var content)
				|| content.ValueKind != JsonValueKind.Object)
				throw new FormatException("Expected an object with a ContentSelector object");

			// "Date" is the day the rates apply to; "UpdatedDate" is when the bank published them.
			var date = DateOnly.FromDateTime(ParseDateTimeOffset(content, "Date").DateTime);
			var rateTime = ParseDateTimeOffset(content, "UpdatedDate");

			if (!content.TryGetProperty("Data", out var data) || data.ValueKind != JsonValueKind.Array)
				throw new FormatException("Expected a ContentSelector.Data array");

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
		if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
			throw new FormatException($"ContentSelector.{name} is missing or not a date: '{value}'");
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
