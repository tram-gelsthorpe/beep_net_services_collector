using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Beep.Api.Domain.Currency;

/// <summary>
/// Reads SeABank's rates from its rate page's script (the WebsiteSelector picks
/// <c>script:contains("listExchangeRate")</c>). The script pushes a string holding the page's data:
/// <code>self.__next_f.push([1,"14:[...{\"listExchangeRate\":{\"details\":[{\"currency\":\"USD\",\"buy\":\"25670\",
/// \"sell\":\"26050\",\"transferBuy\":\"25670\",\"transferSell\":\"26050\",\"updateTime\":\"11/26/2026 8:51:00 AM\"},...]}}]"])</code>
/// The list holds each of the day's updates, each rate with the time it was published (Vietnam time).
/// Note-size variants ("USD_1", "USD_5_20") and gold ("XAU_1") are left out.
/// </summary>
public sealed class SeABankRateParser : IBankRateParser
{
	public const string BankBic = "SEAVVNVX";

	private const string ListProperty = "\"listExchangeRate\"";

	public string Name => "seabank";

	public string Bic => BankBic;

	public bool UsesCollectionTime => false;

	public IReadOnlyList<CurrencyRateBank> Parse(string extractedJson, DateTimeOffset collectedAt, ICollection<string>? skipped = null)
	{
		foreach (var script in ReadScripts(extractedJson))
		{
			var data = PushedData(script);
			var start = data?.IndexOf(ListProperty, StringComparison.Ordinal) ?? -1;
			if (start < 0)
				continue;

			var colon = data!.IndexOf(':', start + ListProperty.Length);
			var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(data[(colon + 1)..]));
			JsonElement list;
			try
			{
				list = JsonElement.ParseValue(ref reader);
			}
			catch (JsonException ex)
			{
				throw new FormatException($"SeABank's listExchangeRate is not valid JSON: {ex.Message}", ex);
			}

			if (list.ValueKind != JsonValueKind.Object
				|| !list.TryGetProperty("details", out var details)
				|| details.ValueKind != JsonValueKind.Array)
				throw new FormatException("SeABank's listExchangeRate has no details list");

			var rates = details.EnumerateArray().Select(ReadRate).OfType<CurrencyRateBank>().ToList();
			if (rates.Count == 0)
				throw new FormatException("SeABank's listExchangeRate has no rates");
			return rates;
		}

		throw new FormatException("No SeABank listExchangeRate found in the collected data");
	}

	private static CurrencyRateBank? ReadRate(JsonElement item)
	{
		var code = Text(item, "currency")?.Trim();
		if (code is null || code.Length != 3 || code == "XAU")
			return null;

		var updated = Text(item, "updateTime");
		if (!DateTime.TryParseExact(updated, "M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture,
				DateTimeStyles.None, out var local))
			throw new FormatException($"SeABank {code} updateTime is not a time: '{updated}'");

		var time = new DateTimeOffset(local, BankRateParsers.VietnamOffset);
		var buyCash = Rate(item, "buy", code);
		var buyTransfer = Rate(item, "transferBuy", code);
		var sell = Rate(item, "transferSell", code) ?? Rate(item, "sell", code);
		if (buyCash is null && buyTransfer is null && sell is null)
			return null;

		return new CurrencyRateBank(code, DateOnly.FromDateTime(local), time, buyCash, buyTransfer, sell);
	}

	private static string? Text(JsonElement item, string name) =>
		item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

	private static decimal? Rate(JsonElement item, string name, string code)
	{
		if (!item.TryGetProperty(name, out var value))
			return null;

		decimal rate;
		switch (value.ValueKind)
		{
			case JsonValueKind.Null:
				return null;
			case JsonValueKind.Number:
				rate = value.GetDecimal();
				break;
			case JsonValueKind.String:
				var text = value.GetString()?.Trim().Replace(",", "");
				if (string.IsNullOrEmpty(text) || text == "-")
					return null;
				if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out rate))
					throw new FormatException($"SeABank {code} {name} is not a number: '{value.GetString()}'");
				break;
			default:
				throw new FormatException($"SeABank {code} {name} is not a number");
		}

		return rate > 0 ? rate : null;
	}

	/// <summary>The scripts' text: ContentSelector is one script, or a list of them.</summary>
	private static List<string> ReadScripts(string extractedJson)
	{
		if (string.IsNullOrWhiteSpace(extractedJson))
			throw new FormatException("The data is empty");

		try
		{
			using var document = JsonDocument.Parse(extractedJson);
			if (document.RootElement.ValueKind != JsonValueKind.Object
				|| !document.RootElement.TryGetProperty("ContentSelector", out var content))
				throw new FormatException("Expected {\"ContentSelector\": script} for SeABank");

			return content.ValueKind switch
			{
				JsonValueKind.String => [content.GetString()!],
				JsonValueKind.Array => [.. content.EnumerateArray()
					.Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)],
				_ => throw new FormatException("Expected {\"ContentSelector\": script} for SeABank"),
			};
		}
		catch (JsonException ex)
		{
			throw new FormatException($"The data is not valid JSON: {ex.Message}", ex);
		}
	}

	/// <summary>The string a <c>self.__next_f.push([1,"..."])</c> script pushes, or null for another script.</summary>
	private static string? PushedData(string script)
	{
		var open = script.IndexOf('[');
		var close = script.LastIndexOf(']');
		if (open < 0 || close <= open)
			return null;

		try
		{
			using var pushed = JsonDocument.Parse(script.AsMemory(open, close - open + 1));
			return pushed.RootElement.ValueKind == JsonValueKind.Array
				? pushed.RootElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String)
					.Select(e => e.GetString()).FirstOrDefault()
				: null;
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
