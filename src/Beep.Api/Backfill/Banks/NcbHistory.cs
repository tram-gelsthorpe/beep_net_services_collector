using System.Globalization;
using System.Text.RegularExpressions;
using Beep.Api.Domain.Currency;

namespace Beep.Api.Backfill;

/// <summary>
/// <c>POST https://www.ncb-bank.vn/vi/ty-gia-tien-te</c> with <c>op=ajax&amp;date=dd-MM-yyyy</c>: the table, headed
/// "Hiệu lực từ: Ngày dd-MM-yyyy", the day the rates apply from. A day without rates gets the previous day's
/// table, with that day's date, so it is taken as having none.
/// </summary>
public sealed partial class NcbHistory() : TableBankHistory(BankRateTableParser.Ncb, "table.tb-content td", new DateOnly(2020, 2, 1))
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
