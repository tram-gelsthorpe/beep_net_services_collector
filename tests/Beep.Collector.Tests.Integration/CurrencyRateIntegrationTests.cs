using System.Net;
using System.Text.Json;
using Beep.Api.Backfill;
using Beep.Api.Controllers.Currency;
using Beep.Api.Domain.Currency;
using Beep.Api.Repositories.Collector;
using Beep.Api.Repositories.Currency;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beep.Collector.Tests.Integration;

/// <summary>
/// Beep.Api recording Vietcombank rates in Currency_Rate_Bank. Runs against its own LocalDB database with
/// copies of A_1_Beep_Global_Currency's Bank, Currency and Currency_Rate_Bank tables, plus the Api's
/// Currency_Rate_Bank_RateTime.sql script.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CurrencyRateIntegrationTests
{
	private const string DatabaseName = "WebsiteCollectorTests_Currency";
	private const string Bic = VietcombankRateParser.BankBic;
	private const string OtherBic = "ASCBVNVX";
	private static readonly Guid OtherCountryId = Guid.Parse("0d6f3c1e-7a42-4b8e-9c5d-1e2f3a4b5c6d");
	private static readonly Guid CreatedBy = Guid.Parse("5e0b8a52-1c7d-4f3e-9a61-2b4d6c8e0f11");
	private static readonly DateOnly Day = new(2026, 11, 26);

	private static string _connectionString = string.Empty;
	private static Guid _bankId;
	private CurrencyRateRepository _repository = null!;

	// Same columns, defaults and keys as A_1_Beep_Global_Currency (RateTime comes from the Api's script).
	private const string SchemaSql = """
		CREATE TABLE dbo.Bank (
			Id uniqueidentifier NOT NULL CONSTRAINT DF_b_Id DEFAULT NEWSEQUENTIALID() CONSTRAINT PK_b_Id PRIMARY KEY,
			Country_Id uniqueidentifier NOT NULL,
			Bic varchar(20) NULL,
			Name nvarchar(254) NOT NULL,
			IsSynchronized bit NOT NULL CONSTRAINT DF_b_IsSynchronized DEFAULT 0);

		CREATE TABLE dbo.Currency (
			Id uniqueidentifier NOT NULL CONSTRAINT DF_cur_Id DEFAULT NEWSEQUENTIALID() CONSTRAINT PK_cur_Id PRIMARY KEY,
			CodeISO char(3) NOT NULL CONSTRAINT AK_cur_CodeISO UNIQUE,
			Name nvarchar(254) NOT NULL,
			DecimalDigits tinyint NOT NULL CONSTRAINT DF_cur_DecimalDigits DEFAULT 2,
			Symbol nvarchar(10) NULL,
			IsSynchronized bit NOT NULL CONSTRAINT DF_cur_IsSynchronized DEFAULT 0);

		CREATE TABLE dbo.Currency_Rate_Bank (
			Id uniqueidentifier NOT NULL CONSTRAINT DF_currb_Id DEFAULT NEWSEQUENTIALID() CONSTRAINT PK_currb_Id PRIMARY KEY,
			Version int NOT NULL,
			Modified datetimeoffset NULL,
			ModifiedBy uniqueidentifier NULL,
			Created datetimeoffset NOT NULL,
			CreatedBy uniqueidentifier NOT NULL,
			Currency_Id uniqueidentifier NOT NULL CONSTRAINT FK_currb_Currency REFERENCES dbo.Currency (Id),
			Bank_Id uniqueidentifier NOT NULL CONSTRAINT FK_currb_Bank REFERENCES dbo.Bank (Id),
			[Date] date NOT NULL,
			BuyingRateCash decimal(28, 18) NULL,
			BuyingRateTransfer decimal(28, 18) NULL,
			SellingRate decimal(28, 18) NULL,
			IsSynchronized bit NOT NULL CONSTRAINT DF_currb_IsSynchronized DEFAULT 0);
		""";

	[ClassInitialize]
	public static async Task ClassInitializeAsync(TestContext _)
	{
		_connectionString = new SqlConnectionStringBuilder(TestDatabase.CollectorConnectionString)
		{
			InitialCatalog = DatabaseName,
		}.ConnectionString;

		await TestDatabase.DropDatabaseAsync(_connectionString);
		await using (var master = new SqlConnection(
			new SqlConnectionStringBuilder(_connectionString) { InitialCatalog = "master" }.ConnectionString))
			await master.ExecuteAsync($"CREATE DATABASE [{DatabaseName}]");

		await using var connection = new SqlConnection(_connectionString);
		await connection.ExecuteAsync(SchemaSql);

		// The script must be safe to run again.
		var script = await File.ReadAllTextAsync(
			Path.Combine(AppContext.BaseDirectory, "ApiResource", "Currency_Rate_Bank_RateTime.sql"));
		for (var run = 0; run < 2; run++)
			foreach (var batch in SplitBatches(script))
				await connection.ExecuteAsync(batch);

		_bankId = await connection.ExecuteScalarAsync<Guid>(
			"INSERT INTO dbo.Bank (Country_Id, Bic, Name) OUTPUT INSERTED.Id VALUES (NEWID(), @Bic, N'Vietcombank')",
			new { Bic });
		await connection.ExecuteAsync("""
			INSERT INTO dbo.Currency (CodeISO, Name, Symbol) VALUES ('USD', N'US Dollar', N'$'), ('EUR', N'Euro', NULL), ('SEK', N'Swedish Krona', N' ');
			""");
		// A bank of another country, for the read queries.
		await connection.ExecuteAsync(
			"INSERT INTO dbo.Bank (Country_Id, Bic, Name) VALUES (@OtherCountryId, @OtherBic, N'Asia Commercial 50% Bank')",
			new { OtherCountryId, OtherBic });
	}

	[ClassCleanup]
	public static Task ClassCleanupAsync() => TestDatabase.DropDatabaseAsync(_connectionString);

	[TestInitialize]
	public async Task TestInitializeAsync()
	{
		await using var connection = new SqlConnection(_connectionString);
		await connection.ExecuteAsync("DELETE FROM dbo.Currency_Rate_Bank");
		_repository = new CurrencyRateRepository(_connectionString, CreatedBy);
	}

	// ---------------------------------------------------------------- reading Vietcombank's JSON

	[TestMethod]
	public void Parser_ReadsVietcombankRates()
	{
		// Shape of a real collection result (icon and currencyName are ignored).
		const string json = """
			{"ContentSelector":{"Count":3,"Date":"2026-11-26T00:00:00","UpdatedDate":"2026-11-25T23:00:00+07:00",
			 "Data":[
			  {"currencyName":"US DOLLAR","currencyCode":"USD","cash":"25,610.00","transfer":"25640.00","sell":"26020.00","icon":"/-/media/usa.svg"},
			  {"currencyName":"SWEDISH KRONA","currencyCode":"sek","cash":"0.00","transfer":"2463.65","sell":"2568.18","icon":"/-/media/sek.svg"},
			  {"currencyName":"US DOLLAR AGAIN","currencyCode":"USD","cash":"1","transfer":"1","sell":"1"},
			  {"currencyName":"NO CODE","cash":"1","transfer":"1","sell":"1"}
			 ]}}
			""";

		var rates = VietcombankRateParser.Parse(json);

		Assert.HasCount(2, rates, "the repeated USD and the entry without a code are ignored");
		var rateTime = new DateTimeOffset(2026, 11, 25, 23, 0, 0, TimeSpan.FromHours(7));
		Assert.AreEqual(new CurrencyRateBank("USD", Day, rateTime, 25610.00m, 25640.00m, 26020.00m), rates[0]);
		Assert.AreEqual(new CurrencyRateBank("SEK", Day, rateTime, null, 2463.65m, 2568.18m), rates[1],
			"cash 0.00 means the bank does not buy SEK in cash");
	}

	[TestMethod]
	[DataRow("", DisplayName = "empty")]
	[DataRow("not json", DisplayName = "not JSON")]
	[DataRow("""{"ContentSelector":"text"}""", DisplayName = "selector is not an object")]
	[DataRow("""{"ContentSelector":{"Date":"2026-11-26","Data":[]}}""", DisplayName = "no UpdatedDate")]
	[DataRow("""{"ContentSelector":{"Date":"2026-11-26","UpdatedDate":"2026-11-26T10:00:00+07:00"}}""", DisplayName = "no Data")]
	[DataRow("""{"ContentSelector":{"Date":"2026-11-26","UpdatedDate":"2026-11-26T10:00:00+07:00","Data":[{"currencyCode":"USD","sell":"abc"}]}}""", DisplayName = "rate not a number")]
	public void Parser_RejectsOtherShapes(string json) =>
		Assert.ThrowsExactly<FormatException>(() => VietcombankRateParser.Parse(json));

	// ---------------------------------------------------------------- rates within a day

	[TestMethod]
	public async Task LaterRateOfTheDay_IsAddedAndBecomesTheLatest()
	{
		// 26/11/2026 10:00 rate 1
		var first = await SaveAsync(At(10), ("USD", 1m));
		Assert.AreEqual(new CurrencyRateSaveResult(1, 0, []), first, CurrencyRateSaveResultComparer.Instance);

		// 26/11/2026 14:00 rate 2: a new row; rate 1 is left as it was
		await SaveAsync(At(14), ("USD", 2m));

		var rates = await QueryRatesAsync("USD");
		Assert.HasCount(2, rates);
		var rate1 = rates.Single(r => r.SellingRate == 1m);
		var rate2 = rates.Single(r => r.SellingRate == 2m);
		Assert.AreEqual(At(10), rate1.RateTime);
		Assert.AreEqual(At(14), rate2.RateTime);
		Assert.AreEqual(1, rate1.Version);
		Assert.IsNull(rate1.Modified, "earlier rates are not changed");
		Assert.AreEqual(Day, DateOnly.FromDateTime(rate2.Date));
		Assert.AreEqual(CreatedBy, rate2.CreatedBy);
		Assert.AreEqual(2m, await LatestSellingRateAsync("USD", Day));
	}

	[TestMethod]
	public async Task RepeatedDelivery_IsNotInsertedAgain()
	{
		await SaveAsync(At(10), ("USD", 1m), ("EUR", 1.1m));
		var again = await SaveAsync(At(10), ("USD", 1m), ("EUR", 1.1m));

		Assert.AreEqual(new CurrencyRateSaveResult(0, 2, []), again, CurrencyRateSaveResultComparer.Instance);
		Assert.HasCount(1, await QueryRatesAsync("USD"));
		Assert.HasCount(1, await QueryRatesAsync("EUR"));
	}

	[TestMethod]
	public async Task EarlierRateArrivingLate_IsRecorded_AndLatestIsStillTheLatestTime()
	{
		await SaveAsync(At(14), ("USD", 2m));
		var late = await SaveAsync(At(10), ("USD", 1m));

		Assert.AreEqual(1, late.Inserted);
		Assert.HasCount(2, await QueryRatesAsync("USD"));
		Assert.AreEqual(2m, await LatestSellingRateAsync("USD", Day), "14:00 is still the latest");
	}

	[TestMethod]
	public async Task RatePublishedTheEveningBefore_SortsBeforeTheMorningRate()
	{
		// Vietcombank's 01:00 result: Date 26/11, published 25/11 23:00 (+07:00).
		var eveningBefore = new DateTimeOffset(2026, 11, 25, 23, 0, 0, TimeSpan.FromHours(7));
		await _repository.SaveBankRatesAsync(Bic,
			[new CurrencyRateBank("USD", Day, eveningBefore, 1m, 1m, 1m)], CancellationToken.None);
		await SaveAsync(At(10), ("USD", 2m));

		Assert.AreEqual(2m, await LatestSellingRateAsync("USD", Day));
	}

	[TestMethod]
	public async Task OtherDaysAndCurrencies_AreSeparate()
	{
		await SaveAsync(At(10), ("USD", 1m), ("EUR", 1.1m));
		await SaveAsync(At(14), ("USD", 2m));                    // EUR has no 14:00 rate
		await SaveAsync(At(10, day: 27), ("USD", 3m));           // next day

		Assert.AreEqual(1.1m, await LatestSellingRateAsync("EUR", Day));
		Assert.AreEqual(2m, await LatestSellingRateAsync("USD", Day));
		Assert.AreEqual(3m, await LatestSellingRateAsync("USD", Day.AddDays(1)));
	}

	[TestMethod]
	public async Task UnknownCurrency_IsReported_AndUnknownBankFails()
	{
		var result = await SaveAsync(At(10), ("USD", 1m), ("XYZ", 9m));

		Assert.AreEqual(1, result.Inserted);
		CollectionAssert.AreEqual(new[] { "XYZ" }, result.UnknownCurrencies.ToArray());

		var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
			_repository.SaveBankRatesAsync("NOTABANK", [Rate("USD", At(11), 1m)], CancellationToken.None));
		StringAssert.Contains(error.Message, "NOTABANK");
	}

	// ---------------------------------------------------------------- the endpoint

	[TestMethod]
	public async Task Endpoint_ReadsCollectedData_AndRecordsVietcombankRates()
	{
		var collectedDataId = Guid.NewGuid();
		var collected = new StubCollectedData { [collectedDataId] = VietcombankJson(At(10), ("USD", 1m), ("EUR", 1.1m)) };
		var alerts = new RecordingQueue();
		var controller = new CurrencyRateController(collected, _repository, alerts, NullLogger<CurrencyRateController>.Instance);

		var ok = await controller.RecordBankCurrencyRate("vietcombank", Request(collectedDataId), CancellationToken.None);
		var result = (CurrencyRateSaveResult)((OkObjectResult)ok).Value!;
		Assert.AreEqual(new CurrencyRateSaveResult(2, 0, []), result, CurrencyRateSaveResultComparer.Instance);
		Assert.AreEqual("BFTVVNVX rate alerts", alerts.Descriptions.Single(), "new rates: subscribers are emailed");

		var again = await controller.RecordBankCurrencyRate("vietcombank", Request(collectedDataId), CancellationToken.None);
		Assert.AreEqual(2, ((CurrencyRateSaveResult)((OkObjectResult)again).Value!).Duplicates, "a redelivery is accepted");
		Assert.HasCount(1, alerts.Descriptions, "a redelivery sends no alert");

		Assert.IsInstanceOfType<NotFoundObjectResult>(
			await controller.RecordBankCurrencyRate("vietcombank", Request(Guid.NewGuid()), CancellationToken.None));

		collected[collectedDataId] = """{"ContentSelector":"something else"}""";
		Assert.IsInstanceOfType<UnprocessableEntityObjectResult>(
			await controller.RecordBankCurrencyRate("vietcombank", Request(collectedDataId), CancellationToken.None));

		Assert.IsInstanceOfType<BadRequestObjectResult>(
			await controller.RecordBankCurrencyRate("vietcombank", Request(Guid.Empty), CancellationToken.None));
	}

	[TestMethod]
	public async Task OtherBankEndpoint_RecordsAtCollectionTime_AndSkipsUnchangedRates()
	{
		const string WooriBic = "HVBKVNVX";
		await using (var connection = new SqlConnection(_connectionString))
			await connection.ExecuteAsync("""
				IF NOT EXISTS (SELECT 1 FROM dbo.Bank WHERE Bic = @WooriBic)
					INSERT INTO dbo.Bank (Country_Id, Bic, Name) VALUES (NEWID(), @WooriBic, N'Woori Bank Vietnam')
				""", new { WooriBic });

		var page = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "BankRatePages", "woori.json"));
		var collected = new StubCollectedData();
		var alerts = new RecordingQueue();
		var controller = new CurrencyRateController(collected, _repository, alerts, NullLogger<CurrencyRateController>.Instance);
		var morning = new DateTimeOffset(2026, 11, 26, 2, 15, 30, TimeSpan.Zero); // 09:15:30 in Vietnam

		async Task<CurrencyRateSaveResult> Deliver(string json, DateTimeOffset collectedAt)
		{
			var id = Guid.NewGuid();
			collected[id] = json;
			var ok = await controller.RecordBankCurrencyRate("woori",
				new() { WebsiteSelectorsId = Guid.NewGuid(), CollectedDataId = id, CollectedAt = collectedAt }, CancellationToken.None);
			return (CurrencyRateSaveResult)((OkObjectResult)ok).Value!;
		}

		// USD and EUR are known currencies here; Woori's KRW, JPY, GBP, AUD, SGD and CAD are not.
		var first = await Deliver(page, morning);
		Assert.AreEqual(2, first.Inserted);
		Assert.HasCount(6, first.UnknownCurrencies);
		Assert.AreEqual("HVBKVNVX rate alerts", alerts.Descriptions.Single());

		var unchanged = await Deliver(page, morning.AddHours(3));
		Assert.AreEqual(new CurrencyRateSaveResult(0, 2, unchanged.UnknownCurrencies), unchanged, CurrencyRateSaveResultComparer.Instance,
			"collected again with the same rates: nothing recorded");
		Assert.HasCount(1, alerts.Descriptions, "and no alert");

		var usdChanged = await Deliver(page.Replace("\"26,060\"", "\"26,070\""), morning.AddHours(6));
		Assert.AreEqual(1, usdChanged.Inserted, "USD changed at 15:15");
		Assert.AreEqual(1, usdChanged.Duplicates, "EUR did not");

		await using (var connection = new SqlConnection(_connectionString))
		{
			var usd = (await connection.QueryAsync<(DateTimeOffset RateTime, decimal SellingRate)>("""
				SELECT r.RateTime, r.SellingRate
				FROM dbo.Currency_Rate_Bank r JOIN dbo.Currency c ON c.Id = r.Currency_Id JOIN dbo.Bank b ON b.Id = r.Bank_Id
				WHERE b.Bic = @WooriBic AND c.CodeISO = 'USD' ORDER BY r.RateTime
				""", new { WooriBic })).ToList();
			CollectionAssert.AreEqual(
				new[] { (new DateTimeOffset(2026, 11, 26, 9, 15, 0, TimeSpan.FromHours(7)), 26060m),
				        (new DateTimeOffset(2026, 11, 26, 15, 15, 0, TimeSpan.FromHours(7)), 26070m) },
				usd);
		}

		Assert.IsInstanceOfType<NotFoundObjectResult>(await controller.RecordBankCurrencyRate(
			"notabank", Request(Guid.NewGuid()), CancellationToken.None));
		Assert.IsInstanceOfType<BadRequestObjectResult>(await controller.RecordBankCurrencyRate(
			"woori", new() { WebsiteSelectorsId = Guid.NewGuid(), CollectedDataId = Guid.NewGuid() }, CancellationToken.None),
			"the time it was collected is needed");
		var other = Guid.NewGuid();
		collected[other] = VietcombankJson(At(10), ("USD", 1m));
		Assert.IsInstanceOfType<UnprocessableEntityObjectResult>(await controller.RecordBankCurrencyRate(
			"woori", Request(other), CancellationToken.None), "another bank's data");
	}

	// ---------------------------------------------------------------- backfill from Vietcombank's API

	[TestMethod]
	public void ParseRateList_ReadsTheApiResponse_AndAnEmptyDay()
	{
		const string json = """
			{"Count":1,"Date":"2025-01-02T00:00:00","UpdatedDate":"2025-01-02T23:00:00+07:00",
			 "Data":[{"currencyName":"US DOLLAR","currencyCode":"USD","cash":"25229.00","transfer":"25259.00","sell":"25559.00","icon":"/x.svg"}]}
			""";

		var rates = VietcombankRateParser.ParseRateList(json);

		Assert.AreEqual(new CurrencyRateBank("USD", new DateOnly(2025, 1, 2),
			new DateTimeOffset(2025, 1, 2, 23, 0, 0, TimeSpan.FromHours(7)), 25229m, 25259m, 25559m), rates.Single());
		Assert.IsEmpty(VietcombankRateParser.ParseRateList(
			"""{"Count":0,"Date":"0001-01-01T00:00:00","UpdatedDate":"0001-01-01T00:00:00","Data":[]}"""));
		Assert.ThrowsExactly<FormatException>(() => VietcombankRateParser.ParseRateList("""{"ContentSelector":{}}"""));
	}

	[TestMethod]
	public async Task Backfill_FetchesOnlyMissingDays_AndRecordsThem()
	{
		// 01..05/03/2025: the 2nd is already recorded, the 3rd has no rates, the 4th always fails.
		var day = new DateOnly(2025, 3, 1);
		await _repository.SaveBankRatesAsync(Bic, [EndOfDay("USD", day.AddDays(1), 2m)], CancellationToken.None);

		var api = new StubVietcombankApi(date => date.Day switch
		{
			3 => (HttpStatusCode.OK, """{"Count":0,"Date":"0001-01-01T00:00:00","UpdatedDate":"0001-01-01T00:00:00","Data":[]}"""),
			4 => (HttpStatusCode.InternalServerError, ""),
			_ => (HttpStatusCode.OK, RateListJson(date, ("USD", date.Day), ("EUR", date.Day + 0.5m))),
		});
		var backfill = new BankBackfill(new HttpClient(api), _repository, BankHistories.ForName("vietcombank")!,
			NullLogger<BankBackfill>.Instance, retryDelays: [TimeSpan.Zero]);

		var result = await backfill.RunAsync(day, day.AddDays(4), TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(4, result.DaysMissing, "the 2nd is skipped");
		Assert.AreEqual(2, result.DaysRecorded, "the 1st and 5th");
		Assert.AreEqual(4, result.RatesInserted);
		Assert.AreEqual(1, result.DaysWithoutRates);
		CollectionAssert.AreEqual(new[] { day.AddDays(3) }, result.FailedDays);
		CollectionAssert.AreEqual(
			new[] { "2025-03-01", "2025-03-03", "2025-03-04", "2025-03-04", "2025-03-05" }, api.RequestedDates,
			"the failed day is tried twice (one retry); the recorded day is not fetched");

		var usd = await QueryRatesAsync("USD");
		CollectionAssert.AreEquivalent(new[] { 1m, 2m, 5m }, usd.Select(r => r.SellingRate!.Value).ToArray());
		Assert.AreEqual(new DateTimeOffset(2025, 3, 5, 23, 0, 0, TimeSpan.FromHours(7)), usd.Single(r => r.SellingRate == 5m).RateTime);

		// Running again fetches only what is still missing: the empty day and the failed day.
		api.RequestedDates.Clear();
		await backfill.RunAsync(day, day.AddDays(4), TimeSpan.Zero, CancellationToken.None);
		CollectionAssert.AreEqual(new[] { "2025-03-03", "2025-03-04", "2025-03-04" }, api.RequestedDates);
	}

	[TestMethod]
	public void Backfill_LastCompleteDay_IsYesterdayInVietnam()
	{
		// 26/11 00:30 in Vietnam is still 25/11 in UTC.
		Assert.AreEqual(new DateOnly(2026, 11, 25),
			BankBackfill.LastCompleteDay(new DateTimeOffset(2026, 11, 25, 17, 30, 0, TimeSpan.Zero)));
		Assert.AreEqual(new DateOnly(2026, 11, 24),
			BankBackfill.LastCompleteDay(new DateTimeOffset(2026, 11, 25, 16, 30, 0, TimeSpan.Zero)));
	}

	// ---------------------------------------------------------------- reading the latest rates

	[TestMethod]
	public async Task LatestRates_AreTheLatestDateAndTime_OnOrBeforeTheDate()
	{
		await _repository.SaveBankRatesAsync(Bic,
			[Rate("USD", At(10), 1m), Rate("USD", At(14), 2m), Rate("USD", At(23, day: 27), 3m), Rate("EUR", At(23, day: 25), 4m)],
			CancellationToken.None);
		await _repository.SaveBankRatesAsync(OtherBic, [Rate("USD", At(9), 9m)], CancellationToken.None);
		var queries = new CurrencyRateQueries(_connectionString);

		Assert.AreEqual("ACB USD 9 | VCB EUR 4 | VCB USD 3",
			Describe(await queries.GetLatestRatesAsync(null, null, null, CancellationToken.None)));
		Assert.AreEqual("ACB USD 9 | VCB EUR 4 | VCB USD 2",
			Describe(await queries.GetLatestRatesAsync(Day, null, null, CancellationToken.None)),
			"on the 26th: the 14:00 rate, not the 27th's");
		Assert.AreEqual("VCB EUR 4",
			Describe(await queries.GetLatestRatesAsync(Day.AddDays(-1), null, null, CancellationToken.None)));
		Assert.AreEqual("",
			Describe(await queries.GetLatestRatesAsync(Day.AddDays(-2), null, null, CancellationToken.None)));

		var latest = (await queries.GetLatestRatesAsync(Day, null, Bic, CancellationToken.None)).Single(r => r.CurrencyCode == "USD");
		Assert.AreEqual(At(14), latest.RateTime);
		Assert.AreEqual(new DateTime(2026, 11, 26), latest.Date);
		Assert.AreEqual("US Dollar", latest.CurrencyName);
		Assert.AreEqual("$", latest.CurrencySymbol);
		Assert.IsNull((await queries.GetLatestRatesAsync(Day, null, Bic, CancellationToken.None)).Single(r => r.CurrencyCode == "EUR").CurrencySymbol);

		await using var connection = new SqlConnection(_connectionString);
		var usdId = await connection.ExecuteScalarAsync<Guid>("SELECT Id FROM dbo.Currency WHERE CodeISO = 'USD'");
		var codes = await queries.GetCurrencyCodesAsync([usdId, Guid.NewGuid()], CancellationToken.None);
		Assert.AreEqual("USD", codes.Single().Value);
		Assert.IsEmpty(await queries.GetCurrencyCodesAsync([], CancellationToken.None));
	}

	[TestMethod]
	public async Task LatestRates_FilterByBicOrName_AndCountry()
	{
		await _repository.SaveBankRatesAsync(Bic, [Rate("USD", At(10), 1m)], CancellationToken.None);
		await _repository.SaveBankRatesAsync(OtherBic, [Rate("USD", At(9), 9m)], CancellationToken.None);
		var queries = new CurrencyRateQueries(_connectionString);

		Assert.AreEqual("VCB USD 1", Describe(await queries.GetLatestRatesAsync(null, null, Bic, CancellationToken.None)));
		Assert.AreEqual("VCB USD 1", Describe(await queries.GetLatestRatesAsync(null, null, " vietcom ", CancellationToken.None)));
		Assert.AreEqual("ACB USD 9", Describe(await queries.GetLatestRatesAsync(null, null, "50%", CancellationToken.None)),
			"% is matched literally");
		Assert.AreEqual("", Describe(await queries.GetLatestRatesAsync(null, null, "5_%", CancellationToken.None)));
		Assert.AreEqual("ACB USD 9", Describe(await queries.GetLatestRatesAsync(null, OtherCountryId, null, CancellationToken.None)));
		Assert.AreEqual("", Describe(await queries.GetLatestRatesAsync(null, OtherCountryId, Bic, CancellationToken.None)));

		var banks = await queries.GetBanksWithRatesAsync(null, null, CancellationToken.None);
		CollectionAssert.AreEqual(new[] { OtherBic, Bic }, banks.Select(b => b.Bic).ToArray(), "ordered by name");
		Assert.AreEqual(new DateTime(2026, 11, 26), banks[1].LatestDate);
		Assert.AreEqual(OtherBic, (await queries.GetBanksWithRatesAsync(OtherCountryId, null, CancellationToken.None)).Single().Bic);
	}

	[TestMethod]
	public async Task Banks_WithoutRates_AreNotListed()
	{
		await _repository.SaveBankRatesAsync(Bic, [Rate("USD", At(10), 1m)], CancellationToken.None);
		var queries = new CurrencyRateQueries(_connectionString);

		Assert.AreEqual(Bic, (await queries.GetBanksWithRatesAsync(null, null, CancellationToken.None)).Single().Bic);
		Assert.AreEqual("VCB USD 1", Describe(await queries.GetLatestRatesAsync(null, null, null, CancellationToken.None)));
	}

	// ---------------------------------------------------------------- helpers

	private static DateTimeOffset At(int hour, int day = 26) => new(2026, 11, day, hour, 0, 0, TimeSpan.FromHours(7));

	private static CurrencyRateBank Rate(string code, DateTimeOffset at, decimal sell) =>
		new(code, DateOnly.FromDateTime(at.DateTime), at, sell - 0.5m, sell - 0.25m, sell);

	private Task<CurrencyRateSaveResult> SaveAsync(DateTimeOffset at, params (string Code, decimal Sell)[] rates) =>
		_repository.SaveBankRatesAsync(Bic, [.. rates.Select(r => Rate(r.Code, at, r.Sell))], CancellationToken.None);

	private static CurrencyRateBank EndOfDay(string code, DateOnly day, decimal sell) =>
		new(code, day, new DateTimeOffset(day.ToDateTime(new TimeOnly(23, 0)), TimeSpan.FromHours(7)), sell, sell, sell);

	private static string RateListJson(DateOnly day, params (string Code, decimal Sell)[] rates) =>
		JsonSerializer.Serialize(new
		{
			Count = rates.Length,
			Date = $"{day:yyyy-MM-dd}T00:00:00",
			UpdatedDate = $"{day:yyyy-MM-dd}T23:00:00+07:00",
			Data = rates.Select(r => new
			{
				currencyCode = r.Code,
				cash = r.Sell.ToString(System.Globalization.CultureInfo.InvariantCulture),
				transfer = r.Sell.ToString(System.Globalization.CultureInfo.InvariantCulture),
				sell = r.Sell.ToString(System.Globalization.CultureInfo.InvariantCulture),
			}),
		});

	// "VCB USD 2 | ACB USD 9": bank, currency and selling rate of each row, in the query's order.
	private static string Describe(IEnumerable<LatestBankRate> rates) =>
		string.Join(" | ", rates.Select(r =>
			$"{(r.Bic == Bic ? "VCB" : "ACB")} {r.CurrencyCode} {r.SellingRate!.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}"));

	private static string VietcombankJson(DateTimeOffset at, params (string Code, decimal Sell)[] rates) =>
		JsonSerializer.Serialize(new
		{
			ContentSelector = new
			{
				Count = rates.Length,
				Date = $"{at:yyyy-MM-dd}T00:00:00",
				UpdatedDate = at,
				Data = rates.Select(r => new
				{
					currencyCode = r.Code,
					cash = (r.Sell - 0.5m).ToString(System.Globalization.CultureInfo.InvariantCulture),
					transfer = (r.Sell - 0.25m).ToString(System.Globalization.CultureInfo.InvariantCulture),
					sell = r.Sell.ToString(System.Globalization.CultureInfo.InvariantCulture),
				}),
			},
		});

	private static CurrencyRateController.CollectionDeliveryRequest Request(Guid collectedDataId) => new()
	{
		WebsiteSelectorsId = Guid.NewGuid(),
		CollectedDataId = collectedDataId,
		CollectedAt = DateTimeOffset.UtcNow,
	};

	private static async Task<List<RateRow>> QueryRatesAsync(string code)
	{
		await using var connection = new SqlConnection(_connectionString);
		return [.. await connection.QueryAsync<RateRow>("""
			SELECT r.Id, r.[Date], r.RateTime, r.SellingRate, r.Version, r.Modified, r.CreatedBy
			FROM dbo.Currency_Rate_Bank r JOIN dbo.Currency c ON c.Id = r.Currency_Id
			WHERE c.CodeISO = @code AND r.Bank_Id = @bankId
			""", new { code, bankId = _bankId })];
	}

	// How readers find the current rate: the highest RateTime of the day.
	private static async Task<decimal?> LatestSellingRateAsync(string code, DateOnly day)
	{
		await using var connection = new SqlConnection(_connectionString);
		return await connection.QuerySingleOrDefaultAsync<decimal?>("""
			SELECT TOP (1) r.SellingRate
			FROM dbo.Currency_Rate_Bank r JOIN dbo.Currency c ON c.Id = r.Currency_Id
			WHERE c.CodeISO = @code AND r.Bank_Id = @bankId AND r.[Date] = @day
			ORDER BY r.RateTime DESC
			""", new { code, bankId = _bankId, day = day.ToDateTime(TimeOnly.MinValue) });
	}

	private static IEnumerable<string> SplitBatches(string script) =>
		System.Text.RegularExpressions.Regex.Split(script, @"^\s*GO\s*$",
				System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase)
			.Where(b => !string.IsNullOrWhiteSpace(b));

	private sealed class RecordingQueue : Beep.Notification.INotificationQueue
	{
		public List<string> Descriptions { get; } = [];

		public void Enqueue(string description, Func<IServiceProvider, CancellationToken, Task> work) => Descriptions.Add(description);
	}

	private sealed record RateRow(
		Guid Id, DateTime Date, DateTimeOffset? RateTime, decimal? SellingRate, int Version,
		DateTimeOffset? Modified, Guid CreatedBy);

	// Vietcombank's exchangerates API: answers per ?date=, and records which dates were asked for.
	private sealed class StubVietcombankApi(Func<DateOnly, (HttpStatusCode Status, string Body)> respond) : HttpMessageHandler
	{
		public List<string> RequestedDates { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var url = request.RequestUri!.ToString();
			Assert.StartsWith(VietcombankRateParser.RatesUrl, url);
			var date = url[VietcombankRateParser.RatesUrl.Length..];
			RequestedDates.Add(date);

			var (status, body) = respond(DateOnly.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
			return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
		}
	}

	private sealed class StubCollectedData : Dictionary<Guid, string>, ICollectedDataRepository
	{
		public Task<string?> GetExtractedJsonAsync(Guid collectedDataId, CancellationToken cancellationToken) =>
			Task.FromResult(TryGetValue(collectedDataId, out var json) ? json : null);
	}

	// Records with a list compare the list by reference; compare its items instead.
	private sealed class CurrencyRateSaveResultComparer : IEqualityComparer<CurrencyRateSaveResult>
	{
		public static readonly CurrencyRateSaveResultComparer Instance = new();

		public bool Equals(CurrencyRateSaveResult? x, CurrencyRateSaveResult? y) =>
			x is not null && y is not null
			&& (x.Inserted, x.Duplicates) == (y.Inserted, y.Duplicates)
			&& x.UnknownCurrencies.SequenceEqual(y.UnknownCurrencies);

		public int GetHashCode(CurrencyRateSaveResult obj) => HashCode.Combine(obj.Inserted, obj.Duplicates);
	}
}
