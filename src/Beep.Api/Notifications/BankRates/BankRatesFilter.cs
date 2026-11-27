using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beep.Api.Notifications;

/// <summary>The choices of a (Currency, BankRates) subscription, as stored in <c>User_Notification.Filter</c>.</summary>
/// <param name="Country">Country code, e.g. VN</param>
/// <param name="Banks">BICs of banks of that country</param>
/// <param name="Currencies">ISO codes</param>
public sealed record BankRatesFilter(
	[property: JsonPropertyName("country")] string Country,
	[property: JsonPropertyName("banks")] IReadOnlyList<string> Banks,
	[property: JsonPropertyName("currencies")] IReadOnlyList<string> Currencies)
{
	public static BankRatesFilter? Parse(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
			return null;
		try
		{
			return JsonSerializer.Deserialize<BankRatesFilter>(json);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	public string ToJson() => JsonSerializer.Serialize(this);
}
