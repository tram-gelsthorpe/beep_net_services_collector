using System.Text.Json;
using Beep.Api.Notifications;
using Beep.Api.Repositories.Country;
using Beep.Api.Repositories.Currency;
using Beep.Notification;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beep.Collector.Tests.Integration;

/// <summary>
/// Beep.Notification's subscriptions (dbo.User and dbo.User_Notification) and Beep.Api's currency rate alerts.
/// Runs against its own LocalDB database with copies of A_1_Beep_Global_Default's Licence, User and User_Notification
/// tables (User_Notification as Beep.Database.Processor creates it, in Create/Global.sql).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class NotificationIntegrationTests
{
	private const string DatabaseName = "WebsiteCollectorTests_Default";
	private const string Bic = "BFTVVNVX";
	private const string OtherBic = "ASCBVNVX";
	private static readonly Guid CreatedBy = Guid.Parse("5e0b8a52-1c7d-4f3e-9a61-2b4d6c8e0f11");
	private static readonly Guid VietnamId = Guid.Parse("0d6f3c1e-7a42-4b8e-9c5d-1e2f3a4b5c6d");
	private static readonly Guid VndId = Guid.Parse("6a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d");

	private static string _connectionString = string.Empty;
	private static Guid _licenceId;

	private SubscriptionRepository _repository = null!;
	private ImmediateQueue _queue = null!;
	private RecordingSender _sender = null!;
	private NotificationOptions _options = null!;

	// Same columns, defaults and keys as A_1_Beep_Global_Default (the columns subscriptions use; all of User_Notification).
	private const string SchemaSql = """
		CREATE TABLE dbo.Licence (
			Id uniqueidentifier NOT NULL CONSTRAINT DF_li_Id DEFAULT NEWSEQUENTIALID() CONSTRAINT PK_li_Id PRIMARY KEY,
			Version int NOT NULL CONSTRAINT DF_li_Version DEFAULT 0,
			Created datetimeoffset NOT NULL,
			CreatedBy uniqueidentifier NOT NULL,
			Code varchar(256) NOT NULL CONSTRAINT AK_li_Code UNIQUE,
			Name nvarchar(512) NOT NULL);

		CREATE TABLE dbo.[User] (
			Id uniqueidentifier NOT NULL CONSTRAINT DF_u_Id DEFAULT NEWSEQUENTIALID() CONSTRAINT PK_u_Id PRIMARY KEY,
			Version int NOT NULL CONSTRAINT DF_u_Version DEFAULT 1,
			Modified datetimeoffset NULL,
			ModifiedBy uniqueidentifier NULL,
			Created datetimeoffset NOT NULL,
			CreatedBy uniqueidentifier NOT NULL,
			Licence_Id uniqueidentifier NULL CONSTRAINT FK_u_Licence REFERENCES dbo.Licence (Id),
			Folder_Id uniqueidentifier NULL,
			Login varchar(150) NOT NULL CONSTRAINT AK_u_Login UNIQUE,
			Password varchar(250) NOT NULL,
			FullName nvarchar(300) NOT NULL,
			EmailAddress varchar(150) NULL,
			CultureInfoUI char(5) NULL,
			IsActivated bit NOT NULL CONSTRAINT DF_u_IsActivated DEFAULT 1,
			IsDeleted bit NOT NULL CONSTRAINT DF_u_IsDeleted DEFAULT 0,
			User_Type tinyint NOT NULL CONSTRAINT DF_u_User_Type DEFAULT 0,
			User_Level tinyint NOT NULL CONSTRAINT DF_u_User_Level DEFAULT 0,
			AuthenticationMethod tinyint NOT NULL CONSTRAINT DF_u_AuthenticationMethod DEFAULT 0);

		CREATE TABLE dbo.User_Notification (
			Id uniqueidentifier NOT NULL CONSTRAINT DF_uno_Id DEFAULT NEWSEQUENTIALID() CONSTRAINT PK_uno_Id PRIMARY KEY,
			Version int NOT NULL CONSTRAINT DF_uno_Version DEFAULT 1,
			Modified datetimeoffset NULL,
			ModifiedBy uniqueidentifier NULL,
			Created datetimeoffset NOT NULL,
			CreatedBy uniqueidentifier NOT NULL,
			User_Id uniqueidentifier NOT NULL CONSTRAINT FK_uno_User REFERENCES dbo.[User] (Id),
			Licence_Id uniqueidentifier NOT NULL CONSTRAINT FK_uno_Licence REFERENCES dbo.Licence (Id),
			Website varchar(100) NOT NULL,
			NotificationType varchar(100) NOT NULL,
			Channel tinyint NOT NULL CONSTRAINT DF_uno_Channel DEFAULT 1,
			Filter nvarchar(max) NULL
				CONSTRAINT CHK_uno_Filter CHECK (Filter IS NULL OR LTRIM(RTRIM(Filter)) LIKE '{%}' OR LTRIM(RTRIM(Filter)) LIKE '[[]%]'),
			IsConfirmed bit NOT NULL CONSTRAINT DF_uno_IsConfirmed DEFAULT 0,
			ConfirmedAt datetimeoffset NULL,
			Token varchar(64) NOT NULL,
			LastSentAt datetimeoffset NULL,
			IsDeleted bit NOT NULL CONSTRAINT DF_uno_IsDeleted DEFAULT 0,
			IsSynchronized bit NOT NULL CONSTRAINT DF_uno_IsSynchronized DEFAULT 0);

		CREATE UNIQUE INDEX AK_uno_User_Website_NotificationType ON dbo.User_Notification (User_Id, Website, NotificationType);
		CREATE UNIQUE INDEX AK_uno_User_Token ON dbo.User_Notification (Token);
		CREATE INDEX IDX_uno_Website_NotificationType ON dbo.User_Notification (Website, NotificationType)
			INCLUDE (User_Id, IsConfirmed, IsDeleted);
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

		_licenceId = await connection.ExecuteScalarAsync<Guid>("""
			INSERT INTO dbo.Licence (Created, CreatedBy, Code, Name) OUTPUT INSERTED.Id
			VALUES (SYSDATETIMEOFFSET(), @CreatedBy, 'TEST-ALERTS', N'Test alerts')
			""", new { CreatedBy });
	}

	[ClassCleanup]
	public static Task ClassCleanupAsync() => TestDatabase.DropDatabaseAsync(_connectionString);

	[TestInitialize]
	public async Task TestInitializeAsync()
	{
		await using var connection = new SqlConnection(_connectionString);
		await connection.ExecuteAsync("DELETE FROM dbo.User_Notification; DELETE FROM dbo.[User];");
		_repository = new SubscriptionRepository(_connectionString, _licenceId, CreatedBy);
		_sender = new RecordingSender();
		_queue = new ImmediateQueue(new ServiceCollection().AddSingleton<INotificationEmailSender>(_sender).BuildServiceProvider());
		_options = new NotificationOptions
		{
			LicenceId = _licenceId,
			CreatedBy = CreatedBy,
			Websites = new Dictionary<string, NotificationWebsite>(StringComparer.OrdinalIgnoreCase)
			{
				["Currency"] = new("Currency", "Beep Currency", "https://currency.example"),
			},
		};
	}

	// ---------------------------------------------------------------- repository

	[TestMethod]
	public async Task Save_NewEmail_CreatesUserWithNineDigitLogin()
	{
		var result = await _repository.SaveAsync("ana@example.com", "vi", "Currency", "BankRates", """{"banks":["BFTVVNVX"]}""", default);

		Assert.IsTrue(result.IsNewUser);
		var s = result.Subscription;
		Assert.MatchesRegex(@"^[1-9]\d{8}$", s.Login);
		Assert.AreEqual("ana@example.com", s.Email);
		Assert.AreEqual("vi", s.Culture);
		Assert.IsFalse(s.IsConfirmed);
		Assert.IsFalse(s.IsDeleted);
		Assert.AreEqual(43, s.Token.Length);

		await using var connection = new SqlConnection(_connectionString);
		var user = await connection.QuerySingleAsync<(Guid LicenceId, string Password, string FullName, Guid CreatedBy,
			byte UserType, byte UserLevel, byte AuthenticationMethod)>(
			"SELECT Licence_Id, Password, FullName, CreatedBy, User_Type, User_Level, AuthenticationMethod FROM dbo.[User] WHERE Id = @UserId",
			new { s.UserId });
		Assert.AreEqual(_licenceId, user.LicenceId);
		Assert.AreEqual("ana@example.com", user.FullName, "the email address is the full name");
		Assert.StartsWith("$2", user.Password, "a bcrypt hash");
		Assert.AreEqual(CreatedBy, user.CreatedBy);
		Assert.AreEqual((10, 0, 0), ((int)user.UserType, (int)user.UserLevel, (int)user.AuthenticationMethod),
			"a subscriber: User_Type 10, User_Level 0, AuthenticationMethod 0");
	}

	[TestMethod]
	public async Task Save_SameEmailAgain_ReusesUserAndReplacesFilter()
	{
		var first = await _repository.SaveAsync("ana@example.com", "en", "Currency", "BankRates", """{"banks":["A"]}""", default);
		var second = await _repository.SaveAsync("ana@example.com", "fr", "Currency", "BankRates", """{"banks":["B"]}""", default);

		Assert.IsFalse(second.IsNewUser);
		Assert.AreEqual(first.Subscription.Id, second.Subscription.Id);
		Assert.AreEqual(first.Subscription.Login, second.Subscription.Login);
		Assert.AreEqual(first.Subscription.Token, second.Subscription.Token, "links already sent keep working");
		Assert.AreEqual("""{"banks":["B"]}""", second.Subscription.Filter);
		Assert.AreEqual("fr", second.Subscription.Culture, "the language follows the latest subscription");

		// Another type of the same website is another subscription of the same user.
		var other = await _repository.SaveAsync("ana@example.com", "fr", "Currency", "Other", null, default);
		Assert.AreEqual(first.Subscription.UserId, other.Subscription.UserId);
		Assert.AreNotEqual(first.Subscription.Id, other.Subscription.Id);
	}

	[TestMethod]
	public async Task ConfirmAndUnsubscribe_ChangeWhoIsActive()
	{
		var saved = (await _repository.SaveAsync("ana@example.com", "en", "Currency", "BankRates", "{}", default)).Subscription;
		Assert.IsEmpty(await _repository.GetActiveAsync("Currency", "BankRates", default), "not confirmed yet");

		var confirmed = await _repository.ConfirmAsync(saved.Token, default);
		Assert.IsNotNull(confirmed);
		Assert.IsTrue(confirmed.IsConfirmed);
		Assert.HasCount(1, await _repository.GetActiveAsync("Currency", "BankRates", default));

		Assert.IsNotNull(await _repository.UnsubscribeAsync(saved.Token, default));
		Assert.IsEmpty(await _repository.GetActiveAsync("Currency", "BankRates", default));

		// Subscribing again: active again, and still confirmed.
		var again = (await _repository.SaveAsync("ana@example.com", "en", "Currency", "BankRates", "{}", default)).Subscription;
		Assert.IsTrue(again.IsConfirmed);
		Assert.IsFalse(again.IsDeleted);
		Assert.HasCount(1, await _repository.GetActiveAsync("Currency", "BankRates", default));

		Assert.IsNull(await _repository.ConfirmAsync("unknown", default));
		Assert.IsNull(await _repository.UnsubscribeAsync("unknown", default));
	}

	[TestMethod]
	public void NewLogin_IsNineDigits()
	{
		for (var i = 0; i < 1000; i++)
			Assert.MatchesRegex(@"^[1-9]\d{8}$", SubscriptionRepository.NewLogin());
	}

	// ---------------------------------------------------------------- subscribing

	[TestMethod]
	public async Task Subscribe_SendsOneConfirmationEmail()
	{
		var service = Service();

		var result = await service.SubscribeAsync(Request("Ana@Example.com "), default);
		await _queue.RunAsync();

		Assert.AreEqual(SubscribeStatus.ConfirmationSent, result.Status);
		var email = _sender.Emails.Single();
		Assert.AreEqual("Ana@Example.com", email.To.Single());
		Assert.Contains("https://currency.example/alerts/confirm?token=", email.Body);
		Assert.Contains("Beep Currency", email.Subject);

		// Asking again at once does not send another one.
		Assert.AreEqual(SubscribeStatus.ConfirmationSent, (await service.SubscribeAsync(Request("ana@example.com"), default)).Status);
		await _queue.RunAsync();
		Assert.HasCount(1, _sender.Emails);
	}

	[TestMethod]
	public async Task Subscribe_ConfirmedAddress_UpdatesWithoutEmail()
	{
		var service = Service();
		await service.SubscribeAsync(Request("ana@example.com"), default);
		await _queue.RunAsync();
		await service.ConfirmAsync(await TokenOfAsync("ana@example.com"), default);

		var result = await service.SubscribeAsync(Request("ana@example.com"), default);
		await _queue.RunAsync();

		Assert.AreEqual(SubscribeStatus.Updated, result.Status);
		Assert.HasCount(1, _sender.Emails, "only the first confirmation email");
	}

	[TestMethod]
	public async Task Subscribe_Invalid_ReportsEachField()
	{
		var result = await Service().SubscribeAsync(
			new SubscribeRequest("not-an-email", "Currency", "BankRates", Json("""{"country":"VN","banks":[]}"""), "en"), default);

		Assert.AreEqual(SubscribeStatus.Invalid, result.Status);
		Assert.IsTrue(result.Errors.ContainsKey("email"));
		Assert.IsTrue(result.Errors.ContainsKey("banks"));

		var unknown = await Service().SubscribeAsync(
			new SubscribeRequest("ana@example.com", "Elsewhere", "BankRates", Json("{}"), "en"), default);
		Assert.IsTrue(unknown.Errors.ContainsKey("website"));
		Assert.IsEmpty(_sender.Emails);
	}

	// ---------------------------------------------------------------- Beep Currency's rate alerts

	[TestMethod]
	public async Task BankRates_Normalize_KeepsOnlyKnownBanksAndCurrencies()
	{
		var type = new BankRatesNotification(new FakeRates(), new FakeCountries());

		var result = await type.NormalizeAsync(Json("""
			{"country":"vn","banks":["bftvvnvx","NOPE"],"currencies":["usd","EUR","XXX"]}
			"""), default);

		Assert.IsTrue(result.IsValid);
		Assert.AreEqual("""{"country":"VN","banks":["BFTVVNVX"],"currencies":["EUR","USD"]}""", result.Filter);

		var invalid = await type.NormalizeAsync(Json("""{"country":"VN","banks":["NOPE"],"currencies":["XXX"]}"""), default);
		Assert.IsTrue(invalid.Errors.ContainsKey("banks"));
		Assert.IsTrue(invalid.Errors.ContainsKey("currencies"));
		Assert.IsTrue((await type.NormalizeAsync(Json("""{"country":"ZZ"}"""), default)).Errors.ContainsKey("country"));
	}

	[TestMethod]
	public async Task BankRates_Alerts_GoToConfirmedSubscribersOfTheBank()
	{
		await SubscribeConfirmedAsync("ana@example.com", "vi", [Bic], ["USD"]);
		await SubscribeConfirmedAsync("ben@example.com", "en", [OtherBic], ["USD"]);
		await _repository.SaveAsync("cat@example.com", "en", "Currency", "BankRates",
			new BankRatesFilter("VN", [Bic], ["USD"]).ToJson(), default); // not confirmed

		var alerts = new BankRatesAlerts(_repository, new FakeRates(), new FakeCountries(), _sender, _options, NullLogger<BankRatesAlerts>.Instance);
		await alerts.SendAsync(Bic, default);

		var email = _sender.Emails.Single();
		Assert.AreEqual("ana@example.com", email.To.Single());
		Assert.AreEqual("Vietcombank: tỷ giá mới", email.Subject, "in the subscriber's language");
		Assert.Contains("26,020.00", email.Body, "USD sell rate");
		Assert.DoesNotContain("29,000.00", email.Body, "EUR was not chosen");
		Assert.Contains("https://currency.example/alerts/unsubscribe?token=", email.Body);

		await using var connection = new SqlConnection(_connectionString);
		Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.User_Notification WHERE LastSentAt IS NOT NULL AND Filter LIKE '%BFTVVNVX%'"));
	}

	[TestMethod]
	public async Task Emails_InArabic_AreRightToLeft()
	{
		await Service().SubscribeAsync(new SubscribeRequest("dana@example.com", "Currency", "BankRates",
			Json("""{"country":"VN","banks":["BFTVVNVX"],"currencies":["USD"]}"""), "ar"), default);
		await _queue.RunAsync();
		var confirmation = _sender.Emails.Single();
		Assert.Contains("<html lang=\"ar\" dir=\"rtl\">", confirmation.Body);
		Assert.Contains("أكّد تنبيهات", confirmation.Subject);

		await Service().ConfirmAsync(await TokenOfAsync("dana@example.com"), default);
		var alerts = new BankRatesAlerts(_repository, new FakeRates(), new FakeCountries(), _sender, _options, NullLogger<BankRatesAlerts>.Instance);
		await alerts.SendAsync(Bic, default);

		var alert = _sender.Emails.Last();
		Assert.AreEqual("Vietcombank: أسعار جديدة", alert.Subject);
		Assert.Contains("dir=\"rtl\"", alert.Body);
		Assert.MatchesRegex(@"text-align:right;[^>]*>USD<", alert.Body, "the currency column is on the right");
		Assert.MatchesRegex(@"text-align:left;[^>]*>26,020.00<", alert.Body, "the rates are on the left");
	}

	// ---------------------------------------------------------------- helpers

	private SubscriptionService Service() =>
		new(_repository, [new BankRatesNotification(new FakeRates(), new FakeCountries())], _options, _queue, NullLogger<SubscriptionService>.Instance);

	private static SubscribeRequest Request(string email) =>
		new(email, "Currency", "BankRates", Json("""{"country":"VN","banks":["BFTVVNVX"],"currencies":["USD"]}"""), "en");

	private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

	private async Task<string> TokenOfAsync(string email)
	{
		await using var connection = new SqlConnection(_connectionString);
		return await connection.ExecuteScalarAsync<string>(
			"SELECT n.Token FROM dbo.User_Notification n JOIN dbo.[User] u ON u.Id = n.User_Id WHERE u.EmailAddress = @email",
			new { email }) ?? throw new InvalidOperationException("no subscription");
	}

	private async Task SubscribeConfirmedAsync(string email, string culture, string[] banks, string[] currencies)
	{
		var saved = await _repository.SaveAsync(email, culture, "Currency", "BankRates",
			new BankRatesFilter("VN", banks, currencies).ToJson(), default);
		await _repository.ConfirmAsync(saved.Subscription.Token, default);
	}

	private sealed class RecordingSender : INotificationEmailSender
	{
		public List<NotificationEmail> Emails { get; } = [];

		public Task SendAsync(NotificationEmail email, CancellationToken cancellationToken)
		{
			Emails.Add(email);
			return Task.CompletedTask;
		}
	}

	private sealed class ImmediateQueue(IServiceProvider services) : INotificationQueue
	{
		private readonly List<Func<IServiceProvider, CancellationToken, Task>> _work = [];

		public void Enqueue(string description, Func<IServiceProvider, CancellationToken, Task> work) => _work.Add(work);

		public async Task RunAsync()
		{
			foreach (var work in _work.ToList())
				await work(services, default);
			_work.Clear();
		}
	}

	private sealed class FakeCountries : ICountryRepository
	{
		public Task<IReadOnlyDictionary<Guid, CountryInfo>> GetCountriesAsync(CancellationToken cancellationToken) =>
			Task.FromResult<IReadOnlyDictionary<Guid, CountryInfo>>(
				new Dictionary<Guid, CountryInfo> { [VietnamId] = new(VietnamId, "VN", "Vietnam", VndId) });
	}

	private sealed class FakeRates : ICurrencyRateQueries
	{
		private static readonly DateTimeOffset Published = new(2026, 11, 26, 9, 30, 0, TimeSpan.FromHours(7));

		private static readonly LatestBankRate[] Rates =
		[
			new(VietnamId, Bic, "Ngân hàng TMCP Ngoại thương Việt Nam – Vietcombank", "Vietcombank", "USD", "US Dollar", "$", new DateTime(2026, 11, 26), Published, 25610m, 25640m, 26020m),
			new(VietnamId, Bic, "Ngân hàng TMCP Ngoại thương Việt Nam – Vietcombank", "Vietcombank", "EUR", "Euro", "€", new DateTime(2026, 11, 26), Published, 27500m, 27800m, 29000m),
			new(VietnamId, OtherBic, "Asia Commercial Bank – ACB", "ACB", "USD", "US Dollar", "$", new DateTime(2026, 11, 26), Published, 25600m, 25630m, 26010m),
		];

		public Task<IReadOnlyList<BankWithRates>> GetBanksWithRatesAsync(Guid? countryId, string? bank, CancellationToken cancellationToken) =>
			Task.FromResult<IReadOnlyList<BankWithRates>>(Rates.GroupBy(r => r.Bic)
				.Select(g => new BankWithRates(VietnamId, g.Key, g.First().BankName, null, new DateTime(2026, 11, 26))).ToList());

		public Task<IReadOnlyList<LatestBankRate>> GetLatestRatesAsync(DateOnly? date, Guid? countryId, string? bank, CancellationToken cancellationToken) =>
			Task.FromResult<IReadOnlyList<LatestBankRate>>(Rates
				.Where(r => (countryId is null || r.CountryId == countryId) && (bank is null || r.Bic == bank)).ToList());

		public Task<IReadOnlyDictionary<Guid, string>> GetCurrencyCodesAsync(IReadOnlyCollection<Guid> currencyIds, CancellationToken cancellationToken) =>
			Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string> { [VndId] = "VND" });
	}
}
