using System.Net;
using Beep.Platform.Api.Websites.Currency.Parsers;

namespace Beep.Platform.Api.Websites.Currency.Backfill;

/// <summary>
/// <c>GET https://www.agribank.com.vn/wcm/connect/ttkhac/ty-gia/yyyy/dd-MM-yyyy?...</c> (what the rate page's date
/// picker loads): the day's table, or 404 for a day without rates.
/// </summary>
public sealed class AgribankHistory() : TableBankHistory(BankRateTableParser.Agribank, "tbody td", new DateOnly(2020, 2, 1))
{
	public override HttpRequestMessage CreateRequest(DateOnly day) =>
		new(HttpMethod.Get, $"https://www.agribank.com.vn/wcm/connect/ttkhac/ty-gia/{day.Year}/{Date(day, "dd-MM-yyyy")}" +
			"?source=library&srv=cmpnt&cmpntid=b42b798a-7057-49c3-b0fd-3766e30729cf");

	public override bool MeansNoRates(HttpStatusCode status) => status == HttpStatusCode.NotFound;

	protected override string? Html(string body, DateOnly day) => body;
}
