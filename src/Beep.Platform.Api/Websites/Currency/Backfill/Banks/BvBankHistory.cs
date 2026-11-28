using System.Text.Json;
using Beep.Platform.Api.Websites.Currency.Parsers;

namespace Beep.Platform.Api.Websites.Currency.Backfill;

/// <summary>
/// <c>POST https://bvbank.net.vn/wp-admin/admin-ajax.php</c> with <c>action=exchange_rate_by_date&amp;date=yyyy-MM-dd</c>:
/// <c>{"success":true,"data":{"html":"&lt;tr&gt;..."}}</c>. A day without rates gets the previous day's (from 14/11/2022).
/// </summary>
public sealed class BvBankHistory() : TableBankHistory(BankRateTableParser.BvBank, "td", new DateOnly(2022, 11, 14))
{
	public override bool RepeatsPreviousRates => true;

	public override HttpRequestMessage CreateRequest(DateOnly day) =>
		new(HttpMethod.Post, "https://bvbank.net.vn/wp-admin/admin-ajax.php")
		{
			Content = Form(("action", "exchange_rate_by_date"), ("date", Date(day, "yyyy-MM-dd"))),
		};

	protected override string? Html(string body, DateOnly day)
	{
		try
		{
			using var json = JsonDocument.Parse(body);
			if (json.RootElement.ValueKind == JsonValueKind.Object
				&& json.RootElement.TryGetProperty("data", out var data)
				&& data.ValueKind == JsonValueKind.Object
				&& data.TryGetProperty("html", out var html)
				&& html.ValueKind == JsonValueKind.String)
				return $"<table>{html.GetString()}</table>";
		}
		catch (JsonException ex)
		{
			throw new FormatException($"BVBank's answer is not JSON: {ex.Message}", ex);
		}

		throw new FormatException("BVBank's answer has no data.html");
	}
}
