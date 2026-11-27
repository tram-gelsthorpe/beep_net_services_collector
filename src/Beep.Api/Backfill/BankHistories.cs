using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Beep.Api.Domain.Currency;

namespace Beep.Api.Backfill;

/// <summary>Where a bank publishes its rates of a past day, and how to read them.</summary>
public interface IBankHistory
{
	/// <summary>The bank's name in <c>backfill &lt;bank&gt;</c>, as in <see cref="IBankRateParser.Name"/>.</summary>
	string Name { get; }

	/// <summary>The bank's BIC, as in <c>dbo.Bank.Bic</c>.</summary>
	string Bic { get; }

	/// <summary>The first day the bank has rates for.</summary>
	DateOnly FirstAvailableDate { get; }

	/// <summary>
	/// True when the bank answers a day without rates (a weekend, a holiday) with the previous day's rates and
	/// no date: a day whose rates are the same as the day before's is then taken as having none.
	/// </summary>
	bool RepeatsPreviousRates { get; }

	/// <summary>The request for one day's rates.</summary>
	HttpRequestMessage CreateRequest(DateOnly day);

	/// <summary>True when a response with this (unsuccessful) status means the bank has no rates that day.</summary>
	bool MeansNoRates(HttpStatusCode status);

	/// <summary>The day's rates from a successful response; empty when the bank has none that day.</summary>
	/// <exception cref="FormatException">The response is not the bank's rates.</exception>
	IReadOnlyList<CurrencyRateBank> Parse(string body, DateOnly day);
}

/// <summary>The banks whose past rates can be backfilled, by <see cref="IBankHistory.Name"/>.</summary>
public static class BankHistories
{
	/// <summary>A past day's rates are its end-of-day rates, recorded at 23:00 in Vietnam.</summary>
	public static DateTimeOffset EndOfDay(DateOnly day) =>
		new(day.ToDateTime(new TimeOnly(23, 0)), BankRateParsers.VietnamOffset);

	public static IReadOnlyList<IBankHistory> All { get; } =
	[
		new VietcombankHistory(),
		new AgribankHistory(),
		new WooriHistory(),
		new BvBankHistory(),
		new NcbHistory(),
	];

	/// <summary>The history of the bank with that name, or null when it cannot be backfilled.</summary>
	public static IBankHistory? ForName(string name) =>
		All.FirstOrDefault(h => string.Equals(h.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Vietcombank's API: <c>GET https://www.vietcombank.com.vn/api/exchangerates?date=yyyy-MM-dd</c>, a rate list
/// with its own date and time (<see cref="VietcombankRateParser.ParseRateList"/>).
/// </summary>
public sealed class VietcombankHistory : IBankHistory
{
	public string Name => "vietcombank";

	public string Bic => VietcombankRateParser.BankBic;

	public DateOnly FirstAvailableDate { get; } = new(2020, 2, 1);

	public bool RepeatsPreviousRates => false;

	public HttpRequestMessage CreateRequest(DateOnly day) =>
		new(HttpMethod.Get, VietcombankRateParser.RatesUrl + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

	public bool MeansNoRates(HttpStatusCode status) => false;

	public IReadOnlyList<CurrencyRateBank> Parse(string body, DateOnly day) => VietcombankRateParser.ParseRateList(body);
}

/// <summary>
/// A bank whose past day is an HTML table like its rate page: its cells are read with the bank's
/// <see cref="BankRateTable"/>, timed at <see cref="BankHistories.EndOfDay"/>.
/// </summary>
public abstract class TableBankHistory : IBankHistory
{
	private readonly BankRateTable _table;
	private readonly string _cellSelector;

	/// <param name="cellSelector">CSS selector of the table's cells in <see cref="Html"/></param>
	protected TableBankHistory(BankRateTable table, string cellSelector, DateOnly firstAvailableDate)
	{
		_table = table;
		_cellSelector = cellSelector;
		FirstAvailableDate = firstAvailableDate;
	}

	public string Name => _table.Name;

	public string Bic => _table.Bic;

	public DateOnly FirstAvailableDate { get; }

	public virtual bool RepeatsPreviousRates => false;

	public abstract HttpRequestMessage CreateRequest(DateOnly day);

	public virtual bool MeansNoRates(HttpStatusCode status) => false;

	public IReadOnlyList<CurrencyRateBank> Parse(string body, DateOnly day)
	{
		var html = Html(body, day);
		if (string.IsNullOrWhiteSpace(html))
			return [];

		var document = new HtmlParser().ParseDocument(html);
		var cells = document.QuerySelectorAll(_cellSelector).Select(c => c.TextContent.Trim()).ToList();
		return _table.ParseCells(cells, BankHistories.EndOfDay(day));
	}

	/// <summary>The HTML holding the day's table, or null when the response says the bank has no rates that day.</summary>
	/// <exception cref="FormatException">The response is not what the bank sends.</exception>
	protected abstract string? Html(string body, DateOnly day);

	protected static FormUrlEncodedContent Form(params (string Name, string Value)[] fields) =>
		new(fields.Select(f => KeyValuePair.Create(f.Name, f.Value)));

	protected static string Date(DateOnly day, string format) => day.ToString(format, CultureInfo.InvariantCulture);
}

/// <summary>
/// <c>GET https://www.agribank.com.vn/wcm/connect/ttkhac/ty-gia/yyyy/dd-MM-yyyy?...</c> (what the rate page's date
/// picker loads): the day's table, or 404 for a day without rates.
/// </summary>
public sealed class AgribankHistory() : TableBankHistory(BankRateTable.Agribank, "tbody td", new DateOnly(2020, 2, 1))
{
	public override HttpRequestMessage CreateRequest(DateOnly day) =>
		new(HttpMethod.Get, $"https://www.agribank.com.vn/wcm/connect/ttkhac/ty-gia/{day.Year}/{Date(day, "dd-MM-yyyy")}" +
			"?source=library&srv=cmpnt&cmpntid=b42b798a-7057-49c3-b0fd-3766e30729cf");

	public override bool MeansNoRates(HttpStatusCode status) => status == HttpStatusCode.NotFound;

	protected override string? Html(string body, DateOnly day) => body;
}

/// <summary>
/// <c>POST https://woori.com.vn/wp-admin/admin-ajax.php</c> with <c>action=exchange_rate&amp;date_search=yyyyMMdd</c>:
/// the table's rows, or nothing for a day without rates.
/// </summary>
public sealed class WooriHistory() : TableBankHistory(BankRateTable.Woori, "tr > *", new DateOnly(2020, 2, 1))
{
	public override HttpRequestMessage CreateRequest(DateOnly day) =>
		new(HttpMethod.Post, "https://woori.com.vn/wp-admin/admin-ajax.php")
		{
			Content = Form(("action", "exchange_rate"), ("locale", "VI_VN"), ("date_search", Date(day, "yyyyMMdd"))),
		};

	// Rows without their table would be dropped by the HTML parser.
	protected override string? Html(string body, DateOnly day) =>
		string.IsNullOrWhiteSpace(body) ? null : $"<table>{body}</table>";
}

/// <summary>
/// <c>POST https://bvbank.net.vn/wp-admin/admin-ajax.php</c> with <c>action=exchange_rate_by_date&amp;date=yyyy-MM-dd</c>:
/// <c>{"success":true,"data":{"html":"&lt;tr&gt;..."}}</c>. A day without rates gets the previous day's (from 14/11/2022).
/// </summary>
public sealed class BvBankHistory() : TableBankHistory(BankRateTable.BvBank, "td", new DateOnly(2022, 11, 14))
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

/// <summary>
/// <c>POST https://www.ncb-bank.vn/vi/ty-gia-tien-te</c> with <c>op=ajax&amp;date=dd-MM-yyyy</c>: the table, headed
/// "Hiệu lực từ: Ngày dd-MM-yyyy", the day the rates apply from. A day without rates gets the previous day's
/// table, with that day's date, so it is taken as having none.
/// </summary>
public sealed partial class NcbHistory() : TableBankHistory(BankRateTable.Ncb, "table.tb-content td", new DateOnly(2020, 2, 1))
{
	public override HttpRequestMessage CreateRequest(DateOnly day) =>
		new(HttpMethod.Post, "https://www.ncb-bank.vn/vi/ty-gia-tien-te")
		{
			Content = Form(("op", "ajax"), ("date", Date(day, "dd-MM-yyyy"))),
		};

	protected override string? Html(string body, DateOnly day)
	{
		var match = EffectiveDate().Match(body);
		if (!match.Success)
			throw new FormatException("NCB's answer has no \"Hiệu lực từ: Ngày\" date");

		return DateOnly.ParseExact(match.Groups[1].Value, "dd-MM-yyyy", CultureInfo.InvariantCulture) == day ? body : null;
	}

	[GeneratedRegex(@"Hiệu lực từ:\s*Ngày\s*(\d{2}-\d{2}-\d{4})")]
	private static partial Regex EffectiveDate();
}
