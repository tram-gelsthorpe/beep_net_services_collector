using System.Net;
using Beep.Platform.Api.Backfill;
using Beep.Platform.Api.Domain.Currency;
using Beep.Platform.Api.Repositories.Currency;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beep.Collector.Tests.Integration;

/// <summary>
/// Backfilling the banks' past rates (<see cref="BankHistories"/>, <see cref="BankBackfill"/>), from what each bank
/// answered for 20/11/2026 (a Friday) and 22/11/2026 (a Sunday) in BankRatePages/history.
/// No database: the rates go to an in-memory repository.
/// </summary>
[TestClass]
public sealed class BankHistoryTests
{
	private static readonly DateOnly Friday = new(2026, 11, 20);
	private static readonly DateTimeOffset FridayEndOfDay = new(2026, 11, 20, 23, 0, 0, TimeSpan.FromHours(7));

	[TestMethod]
	// bank, currencies, then USD: buy cash, buy transfer, sell
	[DataRow("agribank", 15, 25580, 25610, 25990)]
	[DataRow("woori", 8, 25575, 25580, 26000)]
	[DataRow("bvbank", 13, 25575, 25605, 25975)]
	[DataRow("ncb", 10, 25570, 25620, 25980)]   // the large notes' row; selling by transfer
	public void History_ReadsTheBanksAnswerForADay(string bank, int currencies, int buyCash, int buyTransfer, int sell)
	{
		var history = BankHistories.ForName(bank)!;
		var rates = history.Parse(Answer($"{bank}-2026-11-20.{(bank == "bvbank" ? "json" : "html")}"), Friday);

		Assert.HasCount(currencies, rates);
		Assert.IsTrue(rates.All(r => r.Date == Friday && r.RateTime == FridayEndOfDay), "end of the day, Vietnam time");
		Assert.AreEqual(new CurrencyRateBank("USD", Friday, FridayEndOfDay, buyCash, buyTransfer, sell),
			rates.Single(r => r.CurrencyCode == "USD"));
	}

	[TestMethod]
	public void History_ReadsEachBanksWayOfSayingNoRates()
	{
		var sunday = Friday.AddDays(2);
		Assert.IsEmpty(BankHistories.ForName("woori")!.Parse(Answer("woori-2026-11-22.html"), sunday), "nothing");
		Assert.IsEmpty(BankHistories.ForName("ncb")!.Parse(Answer("ncb-2026-11-22.html"), sunday),
			"Friday's table, dated Friday");
		Assert.IsEmpty(BankHistories.ForName("bvbank")!.Parse(Answer("bvbank-2020-02-03.json"), new DateOnly(2020, 2, 3)),
			"\"Không có dữ liệu cho ngày này\"");
		Assert.IsTrue(BankHistories.ForName("agribank")!.MeansNoRates(HttpStatusCode.NotFound));
		Assert.IsFalse(BankHistories.ForName("woori")!.MeansNoRates(HttpStatusCode.NotFound));
		Assert.IsTrue(BankHistories.ForName("bvbank")!.RepeatsPreviousRates, "BVBank answers Sunday with Friday's table");

		Assert.ThrowsExactly<FormatException>(() => BankHistories.ForName("bvbank")!.Parse("<html>", Friday));
		Assert.ThrowsExactly<FormatException>(() => BankHistories.ForName("ncb")!.Parse("<table></table>", Friday));
	}

	[TestMethod]
	public async Task History_Requests_AreWhatTheBanksPagesSend()
	{
		Assert.AreEqual(
			"https://www.agribank.com.vn/wcm/connect/ttkhac/ty-gia/2026/20-11-2026?source=library&srv=cmpnt&cmpntid=b42b798a-7057-49c3-b0fd-3766e30729cf",
			BankHistories.ForName("agribank")!.CreateRequest(Friday).RequestUri!.ToString());
		Assert.AreEqual("https://www.vietcombank.com.vn/api/exchangerates?date=2026-11-20",
			BankHistories.ForName("vietcombank")!.CreateRequest(Friday).RequestUri!.ToString());

		foreach (var (bank, form) in new[]
		{
			("woori", "action=exchange_rate&locale=VI_VN&date_search=20261120"),
			("bvbank", "action=exchange_rate_by_date&date=2026-11-20"),
			("ncb", "op=ajax&date=20-11-2026"),
		})
		{
			using var request = BankHistories.ForName(bank)!.CreateRequest(Friday);
			Assert.AreEqual(HttpMethod.Post, request.Method, bank);
			Assert.AreEqual(form, await request.Content!.ReadAsStringAsync(), bank);
		}
	}

	[TestMethod]
	public void Histories_AreTheParsersBanks()
	{
		CollectionAssert.AreEqual(
			new[] { "vietcombank", "agribank", "woori", "bvbank", "ncb" }, BankHistories.All.Select(h => h.Name).ToArray());
		foreach (var history in BankHistories.All)
			Assert.AreEqual(BankRateParsers.ForName(history.Name)!.Bic, history.Bic, history.Name);
		Assert.AreEqual(new DateOnly(2022, 11, 14), BankHistories.ForName("bvbank")!.FirstAvailableDate);
		Assert.IsNull(BankHistories.ForName("hsbc"), "HSBC's page has today's rates only");
	}

	[TestMethod]
	public async Task Backfill_BvBank_SkipsDaysThatRepeatTheDayBefore()
	{
		// Thu 19 (fetched to compare with Friday), Fri 20 and Mon 23 have rates; Sat 21 and Sun 22 answer with
		// Friday's again; Tue 24 has Friday's rates again, but after Monday's.
		var friday = Answer("bvbank-2026-11-20.json");
		var monday = friday.Replace("25,975.00", "25,990.00");
		var thursday = friday.Replace("25,975.00", "25,960.00");
		var bank = new StubBank(day => (HttpStatusCode.OK, day.Day switch { 19 => thursday, 23 => monday, _ => friday }));
		var repository = new MemoryRepository();
		var backfill = Backfill("bvbank", bank, repository);

		var result = await backfill.RunAsync(Friday, Friday.AddDays(4), TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(3, result.DaysRecorded, "Fri, Mon, and Tue (Friday's rates again, but Monday's were different)");
		Assert.AreEqual(2, result.DaysWithoutRates, "Sat and Sun");
		CollectionAssert.AreEqual(new[] { Friday, Friday.AddDays(3), Friday.AddDays(4) }, repository.Days());
		Assert.AreEqual(Friday.AddDays(-1), bank.RequestedDays[0], "the day before the first is fetched to compare");

		// Running again for the weekend: Friday is recorded, so it is fetched to compare, and not recorded again.
		bank.RequestedDays.Clear();
		result = await backfill.RunAsync(Friday, Friday.AddDays(4), TimeSpan.Zero, CancellationToken.None);
		Assert.AreEqual(0, result.DaysRecorded);
		Assert.AreEqual(2, result.DaysWithoutRates);
		CollectionAssert.AreEqual(new[] { Friday, Friday.AddDays(1), Friday.AddDays(2) }, bank.RequestedDays);
	}

	[TestMethod]
	public async Task Backfill_Agribank_NotFoundIsADayWithoutRates_AndOtherErrorsFail()
	{
		var page = Answer("agribank-2026-11-20.html");
		var bank = new StubBank(day => day.Day switch
		{
			21 => (HttpStatusCode.NotFound, "Trang web không tồn tại"),
			22 => (HttpStatusCode.InternalServerError, ""),
			_ => (HttpStatusCode.OK, page),
		});
		var repository = new MemoryRepository();

		var result = await Backfill("agribank", bank, repository).RunAsync(Friday, Friday.AddDays(2), TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(1, result.DaysRecorded);
		Assert.AreEqual(15, result.RatesInserted);
		Assert.AreEqual(1, result.DaysWithoutRates, "the 404");
		CollectionAssert.AreEqual(new[] { Friday.AddDays(2) }, result.FailedDays, "the 500, after one retry");
		Assert.AreEqual(4, bank.RequestedDays.Count);
		Assert.AreEqual("VBAAVNVX", repository.Bic);
	}

	[TestMethod]
	public async Task Backfill_ADayWithATypoInOneRate_RecordsTheOtherCurrencies()
	{
		// As Agribank's AUD of 10/11/2023: "15162.." for 16798.00.
		var page = Answer("agribank-2026-11-20.html").Replace("<td>16798.00</td>", "<td>16798..</td>");
		var bank = new StubBank(_ => (HttpStatusCode.OK, page));
		var repository = new MemoryRepository();

		var result = await Backfill("agribank", bank, repository).RunAsync(Friday, Friday, TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(1, result.DaysRecorded);
		Assert.AreEqual(14, result.RatesInserted, "the 15 currencies but AUD");
		Assert.IsEmpty(result.FailedDays);
	}

	[TestMethod]
	public async Task Backfill_Ncb_ADayAnsweredWithAnEarlierDaysTable_HasNoRates()
	{
		var bank = new StubBank(day => (HttpStatusCode.OK, Answer(day.Day == 20 ? "ncb-2026-11-20.html" : "ncb-2026-11-22.html")));
		var repository = new MemoryRepository();

		var result = await Backfill("ncb", bank, repository).RunAsync(Friday, Friday.AddDays(2), TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(1, result.DaysRecorded);
		Assert.AreEqual(2, result.DaysWithoutRates);
		CollectionAssert.AreEqual(new[] { Friday }, repository.Days());
	}

	private static BankBackfill Backfill(string bank, StubBank stub, MemoryRepository repository) =>
		new(new HttpClient(stub), repository, BankHistories.ForName(bank)!, NullLogger<BankBackfill>.Instance,
			retryDelays: [TimeSpan.Zero]);

	private static string Answer(string file) =>
		File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "BankRatePages", "history", file));

	// A bank answering each day; the day is read from the request (Agribank's URL, the others' form).
	private sealed class StubBank(Func<DateOnly, (HttpStatusCode Status, string Body)> respond) : HttpMessageHandler
	{
		public List<DateOnly> RequestedDays { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var text = request.RequestUri + " " + (request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
			var match = System.Text.RegularExpressions.Regex.Match(text, @"(\d{2})-11-2026|2026-11-(\d{2})|202611(\d{2})");
			var day = new DateOnly(2026, 11, int.Parse(match.Groups.Cast<System.Text.RegularExpressions.Group>().Skip(1).First(g => g.Success).Value));
			RequestedDays.Add(day);

			var (status, body) = respond(day);
			return new HttpResponseMessage(status) { Content = new StringContent(body) };
		}
	}

	private sealed class MemoryRepository : ICurrencyRateRepository
	{
		private readonly List<CurrencyRateBank> _rates = [];

		public string Bic { get; private set; } = "";

		public DateOnly[] Days() => [.. _rates.Select(r => r.Date).Distinct().Order()];

		public Task<IReadOnlySet<DateOnly>> GetRecordedDatesAsync(
			string bankBic, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
			Task.FromResult<IReadOnlySet<DateOnly>>(_rates.Select(r => r.Date).Where(d => d >= from && d <= to).ToHashSet());

		public Task<CurrencyRateSaveResult> SaveBankRatesAsync(
			string bankBic, IReadOnlyList<CurrencyRateBank> rates, CancellationToken cancellationToken, bool skipUnchanged = false)
		{
			Bic = bankBic;
			_rates.AddRange(rates);
			return Task.FromResult(new CurrencyRateSaveResult(rates.Count, 0, []));
		}
	}
}
