using System.Globalization;
using System.Net;
using Beep.Api.Domain.Currency;

namespace Beep.Api.Backfill;

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

	public IReadOnlyList<CurrencyRateBank> Parse(string body, DateOnly day, ICollection<string>? skipped = null) =>
		VietcombankRateParser.ParseRateList(body);
}
