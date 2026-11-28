using System.Globalization;
using System.Net;
using AngleSharp.Html.Parser;
using Beep.Platform.Api.Domain.Currency;

namespace Beep.Platform.Api.Backfill;

/// <summary>
/// A bank whose past day is an HTML table like its rate page: its cells are read with the bank's
/// <see cref="BankRateTableParser"/>, timed at <see cref="BankHistories.EndOfDay"/>.
/// </summary>
public abstract class TableBankHistory : IBankHistory
{
	private readonly BankRateTableParser _table;
	private readonly string _cellSelector;

	/// <param name="cellSelector">CSS selector of the table's cells in <see cref="Html"/></param>
	protected TableBankHistory(BankRateTableParser table, string cellSelector, DateOnly firstAvailableDate)
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

	public IReadOnlyList<CurrencyRateBank> Parse(string body, DateOnly day, ICollection<string>? skipped = null)
	{
		var html = Html(body, day);
		if (string.IsNullOrWhiteSpace(html))
			return [];

		var document = new HtmlParser().ParseDocument(html);
		var cells = document.QuerySelectorAll(_cellSelector).Select(c => c.TextContent.Trim()).ToList();
		return _table.ParseCells(cells, BankHistories.EndOfDay(day), skipped);
	}

	/// <summary>The HTML holding the day's table, or null when the response says the bank has no rates that day.</summary>
	/// <exception cref="FormatException">The response is not what the bank sends.</exception>
	protected abstract string? Html(string body, DateOnly day);

	protected static FormUrlEncodedContent Form(params (string Name, string Value)[] fields) =>
		new(fields.Select(f => KeyValuePair.Create(f.Name, f.Value)));

	protected static string Date(DateOnly day, string format) => day.ToString(format, CultureInfo.InvariantCulture);
}
