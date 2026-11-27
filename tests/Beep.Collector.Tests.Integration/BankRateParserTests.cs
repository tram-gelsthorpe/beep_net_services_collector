using Beep.Api.Domain.Currency;

namespace Beep.Collector.Tests.Integration;

/// <summary>
/// Reading the banks' rate pages (<see cref="BankRateParsers"/>), from what the collector extracted from each page
/// (BankRatePages/*.json): the Vietnamese banks' on 26/11/2026, the ECB's on 27/11/2026 and SNS's on 28/11/2026.
/// </summary>
[TestClass]
public sealed class BankRateParserTests
{
	// 09:15:30 in Vietnam.
	private static readonly DateTimeOffset CollectedAt = new(2026, 11, 26, 2, 15, 30, TimeSpan.Zero);
	private static readonly DateTimeOffset RecordedAt = new(2026, 11, 26, 9, 15, 0, TimeSpan.FromHours(7));

	[TestMethod]
	// bank, currencies read, then USD: buy cash, buy transfer, sell (0: not offered)
	[DataRow("hsbc", 11, 25716, 25716, 26025)]
	[DataRow("woori", 8, 25635, 25640, 26060)]
	[DataRow("ocb", 11, 25638, 25678, 26100)]          // the $100 notes' row, not $20 or $1
	[DataRow("agribank", 15, 25610, 25640, 26020)]
	[DataRow("bvbank", 13, 25635, 25665, 26035)]       // no gold
	[DataRow("vietabank", 11, 25630, 25680, 26030)]    // the $50-$100 notes' row
	[DataRow("vietinbank", 19, 25470, 25470, 26010)]   // "# 25.470": large notes; 25.470 written as a number
	[DataRow("namabank", 10, 25610, 25660, 26040)]     // headers and the gold table left out
	[DataRow("ncb", 10, 25570, 25620, 26080)]          // the large notes' row; selling by transfer
	public void Table_ReadsTheBanksPage(string bank, int currencies, int buyCash, int buyTransfer, int sell)
	{
		var parser = BankRateParsers.ForName(bank)!;
		var rates = parser.Parse(Page(bank), CollectedAt);

		Assert.HasCount(currencies, rates);
		Assert.HasCount(currencies, rates.Select(r => r.CurrencyCode).Distinct(), "one row per currency");
		Assert.IsFalse(rates.Any(r => r.CurrencyCode is "XAU" or "VND"), "no gold");
		Assert.IsTrue(parser.UsesCollectionTime);
		Assert.IsTrue(rates.All(r => r.RateTime == RecordedAt && r.Date == new DateOnly(2026, 11, 26)),
			"collected at 09:15:30 in Vietnam: recorded at 09:15");

		var usd = rates.Single(r => r.CurrencyCode == "USD");
		Assert.AreEqual(new CurrencyRateBank("USD", new DateOnly(2026, 11, 26), RecordedAt, buyCash, buyTransfer, sell), usd);
	}

	[TestMethod]
	public void Table_ReadsEachNumberFormat()
	{
		// 158,61 (HSBC, dots for thousands); 15,882 per 100 yen (Woori); 3.166 written as a number (VietinBank)
		Assert.AreEqual(158.61m, Rate("hsbc", "JPY").BuyingRateCash);
		Assert.AreEqual(734m, Rate("hsbc", "THB").BuyingRateCash);
		Assert.AreEqual(158.82m, Rate("woori", "JPY").BuyingRateCash);
		Assert.AreEqual(18.66m, Rate("woori", "KRW").BuyingRateCash);
		Assert.AreEqual(3166m, Rate("vietinbank", "HKD").BuyingRateCash);
		Assert.AreEqual(80819m, Rate("vietinbank", "KWD").BuyingRateTransfer);
		Assert.AreEqual(0.65m, Rate("vietinbank", "LAK").BuyingRateTransfer);
		Assert.AreEqual(159.33m, Rate("ocb", "JPY").BuyingRateCash);
		Assert.AreEqual(25610m, Rate("namabank", "USD").BuyingRateCash);
		Assert.AreEqual(18.27m, Rate("namabank", "KRW").BuyingRateCash);
	}

	[TestMethod]
	public void Table_NotOffered_IsNull()
	{
		// BVBank 0.00, VietABank and VietinBank "-", OCB 0, Agribank's missing cell
		CollectionAssert.AreEqual(new decimal?[] { null, 30618m, 31589m }, Rates(Rate("bvbank", "CHF")));
		CollectionAssert.AreEqual(new decimal?[] { null, 3196m, 3453m }, Rates(Rate("vietabank", "HKD")));
		CollectionAssert.AreEqual(new decimal?[] { null, 3798m, 3948m }, Rates(Rate("vietinbank", "CNY")));
		CollectionAssert.AreEqual(new decimal?[] { null, null, 31494m }, Rates(Rate("ocb", "CHF")));
		CollectionAssert.AreEqual(new decimal?[] { null, 18.57m, 20.55m }, Rates(Rate("agribank", "KRW")));
	}

	[TestMethod]
	public void Table_Date_IsTheDayInVietnam()
	{
		// 26/11 18:30 UTC is 27/11 01:30 in Vietnam.
		var rates = BankRateTableParser.Woori.Parse(Page("woori"), new DateTimeOffset(2026, 11, 26, 18, 30, 0, TimeSpan.Zero));
		Assert.AreEqual(new DateOnly(2026, 11, 27), rates[0].Date);
		Assert.AreEqual(new DateTimeOffset(2026, 11, 27, 1, 30, 0, TimeSpan.FromHours(7)), rates[0].RateTime);
	}

	[TestMethod]
	public void SeABank_ReadsEachUpdateOfTheDay_WithItsTime()
	{
		var parser = BankRateParsers.ForName("seabank")!;
		var rates = parser.Parse(Page("seabank"), CollectedAt);

		Assert.IsFalse(parser.UsesCollectionTime);
		Assert.HasCount(44, rates, "4 updates of 11 currencies");
		Assert.IsFalse(rates.Any(r => r.CurrencyCode.Length != 3 || r.CurrencyCode == "XAU"), "no USD_1, USD_5_20 or gold");

		var usd = rates.Where(r => r.CurrencyCode == "USD").ToList();
		CollectionAssert.AreEqual(
			new[] { At(8, 51), At(11, 25), At(13, 30), At(17, 2) },
			usd.Select(r => r.RateTime).ToArray());
		Assert.AreEqual(new CurrencyRateBank("USD", new DateOnly(2026, 11, 26), At(17, 2), 25630m, 25630m, 26010m), usd[^1]);

		var krw = rates.First(r => r.CurrencyCode == "KRW");
		CollectionAssert.AreEqual(new decimal?[] { null, 17.99m, 20.99m }, Rates(krw), "no cash rate");
		Assert.AreEqual(157.52m, rates.First(r => r.CurrencyCode == "JPY").BuyingRateCash);

		static DateTimeOffset At(int hour, int minute) => new(2026, 11, 26, hour, minute, 0, TimeSpan.FromHours(7));
	}

	[TestMethod]
	[DataRow("hsbc", "")]
	[DataRow("hsbc", "not json")]
	[DataRow("hsbc", """{"ContentSelector":"USD 25.716"}""")]
	[DataRow("hsbc", """{"ContentSelector":["Ngoại tệ","Mua"]}""")]
	[DataRow("hsbc", """{"ContentSelector":["USD","abc"]}""")]
	[DataRow("hsbc", """{"Other":[]}""")]
	[DataRow("seabank", """{"ContentSelector":"self.__next_f.push([1,\"nothing here\"])"}""")]
	[DataRow("seabank", """{"ContentSelector":["USD","25,635"]}""")]
	[DataRow("seabank", """{"ContentSelector":"self.__next_f.push([1,\"{\\\"listExchangeRate\\\":{\\\"details\\\":[]}}\"])"}""")]
	public void Parsers_RejectOtherData(string bank, string json) =>
		Assert.ThrowsExactly<FormatException>(() => BankRateParsers.ForName(bank)!.Parse(json, CollectedAt));

	[TestMethod]
	public void TableParser_LeavesOutACurrencyWithATypo_AndReadsTheOthers()
	{
		// Agribank wrote AUD's cash rate "15162.." on 10/11/2023.
		var json = """{"ContentSelector":["USD","25,580","25,610","25,990","AUD","15162..","15,200","15,700","EUR","29,000","29,100","30,000"]}""";
		var skipped = new List<string>();

		var rates = BankRateTableParser.Agribank.Parse(json, CollectedAt, skipped);

		CollectionAssert.AreEqual(new[] { "USD", "EUR" }, rates.Select(r => r.CurrencyCode).ToArray());
		Assert.AreEqual("VBAAVNVX AUD BuyCash is not a number: '15162..'", skipped.Single());
	}

	[TestMethod]
	public void Parsers_HaveOneNameAndBicEach()
	{
		Assert.HasCount(13, BankRateParsers.All);
		Assert.HasCount(13, BankRateParsers.All.Select(p => p.Name).Distinct());
		Assert.HasCount(13, BankRateParsers.All.Select(p => p.Bic).Distinct());
		Assert.AreSame(VietcombankRateParser.Instance, BankRateParsers.ForName("vietcombank"));
		Assert.AreEqual(VietcombankRateParser.BankBic, VietcombankRateParser.Instance.Bic);
		Assert.IsFalse(VietcombankRateParser.Instance.UsesCollectionTime, "Vietcombank's page gives its time");
		Assert.AreSame(BankRateTableParser.Hsbc, BankRateParsers.ForName("HSBC"));
		Assert.IsNull(BankRateParsers.ForName("notabank"));
	}

	[TestMethod]
	public void Ecb_ReadsTheReferenceRates()
	{
		// The ECB page of 27/11/2026, as the collector extracted it: the day, then each currency's rate per euro.
		var parser = BankRateParsers.ForName("ecb")!;
		var rates = parser.Parse(Page("ecb"), CollectedAt);

		Assert.AreSame(EcbRateParser.Instance, parser);
		Assert.AreEqual("NBBEBEBB", parser.Bic);
		Assert.IsFalse(parser.UsesCollectionTime, "the page gives its day");
		Assert.IsTrue(parser.IsReferenceRate, "a central bank's middle rate, not a bank's prices");
		Assert.IsTrue(BankRateParsers.IsReferenceRate("NBBEBEBB"));
		Assert.IsFalse(BankRateParsers.IsReferenceRate("BFTVVNVX"), "Vietcombank's rates are its own prices");
		Assert.HasCount(29, rates);
		Assert.IsFalse(rates.Any(r => r.CurrencyCode == "EUR"));

		// 16:00 in Brussels (winter time: UTC+1) on the page's day, whenever it was collected.
		var published = new DateTimeOffset(2026, 11, 27, 16, 0, 0, TimeSpan.FromHours(1));
		Assert.IsTrue(rates.All(r => r.Date == new DateOnly(2026, 11, 27) && r.RateTime == published));

		// 1 EUR = 1.0957 USD, so 1 USD = 0.9126585744 EUR: one middle rate, no cash rate.
		var usd = rates.Single(r => r.CurrencyCode == "USD");
		Assert.AreEqual(new CurrencyRateBank("USD", new DateOnly(2026, 11, 27), published, null, 0.9126585744m, 0.9126585744m), usd);
		Assert.AreEqual(Math.Round(1m / 174.81m, 10), rates.Single(r => r.CurrencyCode == "JPY").SellingRate);
		Assert.AreEqual(Math.Round(1m / 20136.83m, 10), rates.Single(r => r.CurrencyCode == "IDR").SellingRate);
		Assert.AreEqual(Math.Round(1m / 11.2080m, 10), rates.Single(r => r.CurrencyCode == "SEK").SellingRate);
	}

	[TestMethod]
	public void Ecb_SummerTime_IsUtcPlusTwo()
	{
		var rates = EcbRateParser.Instance.Parse("""{"ContentSelector":["3 July 2026","USD",1.1712]}""", CollectedAt);

		Assert.AreEqual(new DateTimeOffset(2026, 7, 3, 16, 0, 0, TimeSpan.FromHours(2)), rates.Single().RateTime);
	}

	[TestMethod]
	public void Ecb_BadRate_IsLeftOut()
	{
		var skipped = new List<string>();
		var rates = EcbRateParser.Instance.Parse("""{"ContentSelector":["27 November 2026","USD","n/a","JPY",174.81]}""", CollectedAt, skipped);

		Assert.AreEqual("JPY", rates.Single().CurrencyCode);
		Assert.AreEqual("NBBEBEBB USD is not a rate: 'n/a'", skipped.Single());
	}

	[TestMethod]
	public void Ecb_NoDay_IsRefused()
	{
		Assert.ThrowsExactly<FormatException>(() =>
			EcbRateParser.Instance.Parse("""{"ContentSelector":["USD",1.0957]}""", CollectedAt));
	}

	[TestMethod]
	public void Sns_ReadsTheTransferRates()
	{
		// SNS's page of 28/11/2026, as the collector extracted its rates table: code, name, aankoop, verkoop per euro.
		var parser = BankRateParsers.ForName("sns")!;
		var rates = parser.Parse(Page("sns"), CollectedAt);

		Assert.AreSame(SnsRateParser.Instance, parser);
		Assert.AreEqual("SNSBNL2A", parser.Bic);
		Assert.IsTrue(parser.UsesCollectionTime, "the page gives no time");
		Assert.IsFalse(parser.IsReferenceRate, "SNS's own prices");
		Assert.HasCount(13, rates);
		Assert.IsFalse(rates.Any(r => r.CurrencyCode == "EUR"), "the euro's row (1,00000) is left out");

		// Collected at 02:15:30 UTC: 03:15 in Amsterdam (winter time, UTC+1).
		var recorded = new DateTimeOffset(2026, 11, 26, 3, 15, 0, TimeSpan.FromHours(1));
		Assert.IsTrue(rates.All(r => r.Date == new DateOnly(2026, 11, 26) && r.RateTime == recorded));

		// "USD 1,09335 1,10235": SNS sells a dollar at 1 / 1.09335 euro and buys one at 1 / 1.10235; no cash rate.
		var usd = rates.Single(r => r.CurrencyCode == "USD");
		Assert.AreEqual(new CurrencyRateBank("USD", new DateOnly(2026, 11, 26), recorded, null, 0.9071529006m, 0.9146202040m), usd);
		Assert.IsTrue(usd.SellingRate > usd.BuyingRateTransfer, "a real spread");
		Assert.AreEqual(Math.Round(1m / 365.68905m, 10), rates.Single(r => r.CurrencyCode == "HUF").SellingRate);
	}

	[TestMethod]
	public void Sns_StopsAtTheFeesTable()
	{
		// Should the selector also take the page's second table (fees per currency), its repeated codes end the rates.
		var rates = SnsRateParser.Instance.Parse(
			"""{"ContentSelector":["USD","Amerikaanse dollar","1,09335","1,10235","USD","Dollar (VS)","0,0045","€ 0,00412"]}""",
			CollectedAt);

		Assert.AreEqual(Math.Round(1m / 1.09335m, 10), rates.Single().SellingRate);
	}

	[TestMethod]
	public void Sns_BadRate_IsLeftOut()
	{
		var skipped = new List<string>();
		var rates = SnsRateParser.Instance.Parse(
			"""{"ContentSelector":["GBP","Britse pond","-","0,84093","USD","Amerikaanse dollar","1,09335","1,10235"]}""",
			CollectedAt, skipped);

		Assert.AreEqual("USD", rates.Single().CurrencyCode);
		Assert.AreEqual("SNSBNL2A GBP is not a rate: '-' / '0,84093'", skipped.Single());
	}

	[TestMethod]
	public void Sns_NoRates_IsRefused()
	{
		Assert.ThrowsExactly<FormatException>(() =>
			SnsRateParser.Instance.Parse("""{"ContentSelector":["Afkorting","Omschrijving"]}""", CollectedAt));
	}

	private static string Page(string bank) =>
		File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "BankRatePages", bank + ".json"));

	private static CurrencyRateBank Rate(string bank, string code) =>
		BankRateParsers.ForName(bank)!.Parse(Page(bank), CollectedAt).Single(r => r.CurrencyCode == code);

	private static decimal?[] Rates(CurrencyRateBank rate) => [rate.BuyingRateCash, rate.BuyingRateTransfer, rate.SellingRate];
}
