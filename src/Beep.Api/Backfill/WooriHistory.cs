using Beep.Api.Domain.Currency;

namespace Beep.Api.Backfill;

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
