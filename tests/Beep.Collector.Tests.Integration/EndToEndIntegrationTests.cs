using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Beep.EventBus;
using Beep.Scheduler;
using Beep.ServiceBus;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Beep.Collector.Tests.Integration;

/// <summary>
/// The three ServiceBus worker types (Scheduler, WebsiteCollector, EventBus) running together, with the
/// worker's real appsettings.json and the same registrations as Program.cs, in two host instances:
/// a due schedule is run once, every selector is collected once, and every result is delivered once.
/// Only the databases (test LocalDB), the schedule time (due now) and HTTP (stubbed) differ.
/// </summary>
[TestClass]
[DoNotParallelize] // shares the ServiceBus, Collector, EventBus and Scheduler databases
public sealed class EndToEndIntegrationTests
{
	private const int Selectors = 30;
	private const int WithDispatchUrl = 20;
	private const int Instances = 2;

	public TestContext TestContext { get; set; } = null!;

	[TestInitialize]
	public async Task TestInitializeAsync()
	{
		await TestDatabase.ClearDataAsync();
		await TestDatabase.ClearSchedulerDataAsync();
	}

	[TestMethod]
	[Timeout(180_000, CooperativeCancellation = true)]
	public async Task ThreeWorkerTypes_TwoInstances_ScheduleCollectAndDeliverEachOnce()
	{
		var selectorIds = new List<Guid>();
		for (var i = 0; i < Selectors; i++)
			selectorIds.Add(await SeedSelectorAsync(i, i < WithDispatchUrl ? $"https://hooks.test/selector/{i}" : null));

		var sites = new StubHttpHandler(request => Html($"<div class=\"content\">value of {request.RequestUri!.AbsolutePath}</div>"));
		var hooks = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

		var hosts = new List<IHost>();
		try
		{
			for (var i = 0; i < Instances; i++)
				hosts.Add(await StartHostAsync(sites, hooks));

			await WaitUntilAsync(async () =>
				hooks.Requests.Count >= WithDispatchUrl && await CountUnfinishedWorkItemsAsync() == 0,
				TimeSpan.FromSeconds(150));
			await Task.Delay(TimeSpan.FromSeconds(3)); // give a duplicate the chance to show up
		}
		finally
		{
			foreach (var host in hosts)
			{
				await host.StopAsync(CancellationToken.None);
				host.Dispose();
			}
		}

		var workItems = await QueryWorkItemsAsync();
		foreach (var row in workItems)
			TestContext.WriteLine($"{row.WorkerType,-17} {row.Status,-10} {row.Count,3} item(s)");

		// Scheduler: the due schedule ran once, across both instances.
		Assert.AreEqual(1, Count(workItems, "Scheduler", "SUCCEEDED"), "one schedule run");
		await using (var scheduler = TestDatabase.OpenScheduler())
		{
			var runs = (await scheduler.QueryAsync<(string Status, int ItemsRegistered)>(
				$"SELECT Status, ItemsRegistered FROM {TestDatabase.SchedulerSchema}.ScheduleExecutions")).ToList();
			Assert.HasCount(1, runs);
			Assert.AreEqual(("Success", Selectors), runs[0]);
		}

		// WebsiteCollector: every selector was fetched and saved once.
		Assert.AreEqual(Selectors, Count(workItems, "WebsiteCollector", "SUCCEEDED"));
		Assert.HasCount(Selectors, sites.Requests, "each site fetched once");
		Assert.AreEqual(Selectors, sites.Requests.Select(r => r.Url).Distinct().Count());

		await using var collector = TestDatabase.OpenCollector();
		var collected = (await collector.QueryAsync<(Guid Id, Guid SelectorId, string Content)>("""
			SELECT d.Id, e.WebsiteSelectorsId, d.ExtractedJson
			FROM dbo.WebsiteSelectorsCollectedData d
			JOIN dbo.WebsiteSelectorsExecutions e ON e.Id = d.WebsiteSelectorsExecutionsId
			""")).ToList();
		Assert.HasCount(Selectors, collected);
		CollectionAssert.AreEquivalent(selectorIds, collected.Select(c => c.SelectorId).ToList());

		// Which instance collected what (a completed work item no longer records its worker; the execution does).
		foreach (var share in await collector.QueryAsync<(Guid WorkerId, int Count)>(
			"SELECT WorkerId, COUNT(*) FROM dbo.WebsiteSelectorsExecutions GROUP BY WorkerId"))
			TestContext.WriteLine($"WebsiteCollector  instance {share.WorkerId}: {share.Count} collection(s)");
		foreach (var data in collected)
			StringAssert.Contains(data.Content, $"value of /page/{selectorIds.IndexOf(data.SelectorId)}", "the selector's own page, extracted with .content");

		// EventBus: every result with a DispatchUrl was POSTed once, to its selector's URL, with its data Id.
		Assert.AreEqual(WithDispatchUrl, Count(workItems, "EventBus", "SUCCEEDED"));
		Assert.HasCount(WithDispatchUrl, hooks.Requests, "each delivery POSTed once");
		foreach (var (url, body) in hooks.Requests)
		{
			var payload = JsonSerializer.Deserialize<CollectionDeliveryPayload>(body)!;
			var data = collected.Single(c => c.Id == payload.CollectedDataId);
			Assert.AreEqual(data.SelectorId, payload.WebsiteSelectorsId);
			Assert.AreEqual($"https://hooks.test/selector/{selectorIds.IndexOf(data.SelectorId)}", url);
		}

		Assert.IsTrue(workItems.All(w => w.Status == "SUCCEEDED"), "nothing failed, abandoned or left behind");
		Assert.HasCount(3, workItems.Select(w => w.WorkerType).Distinct(), "each worker type handled its own items");
	}

	/// <summary>Build and start a host like Program.cs, from the worker's appsettings.json.</summary>
	private static async Task<IHost> StartHostAsync(StubHttpHandler sites, StubHttpHandler hooks)
	{
		var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
		{
			DisableDefaults = true,
			EnvironmentName = Environments.Production,
			ContentRootPath = AppContext.BaseDirectory
		});
		builder.Configuration
			.AddJsonFile(Path.Combine("WorkerSettings", "appsettings.json"), optional: false)
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				[$"ConnectionStrings:{CreationDatabaseService.ServiceBusDatabaseName}"] = TestDatabase.ServiceBusConnectionString,
				[$"ConnectionStrings:{CreationDatabaseService.SelectorDatabaseName}"] = TestDatabase.CollectorConnectionString,
				[$"ConnectionStrings:{CreationDatabaseService.EventBusDatabaseName}"] = TestDatabase.EventBusConnectionString,
				[$"ConnectionStrings:{CreationDatabaseService.SchedulerDatabaseName}"] = TestDatabase.SchedulerConnectionString,
				// Due now: a daily schedule at midnight UTC that has never run.
				["Scheduler:StartTime"] = "00:00",
				["Scheduler:TimeZone"] = "UTC",
			});
		builder.Services.AddLogging(logging => logging.AddProvider(Errors));

		// Same registrations as Program.cs (without NLog and database creation; TestDatabase creates them).
		builder.Services.RegisterServiceBus(builder.Configuration);
		builder.Services.RegisterEventBus(builder.Configuration);
		builder.Services.RegisterScheduler(builder.Configuration);
		builder.Services.AddSingleton<ISubscriber, WebsiteSelectorsSubscriber>();
		builder.Services.AddCollectorHttpClient().ConfigurePrimaryHttpMessageHandler(() => sites.Share());
		builder.Services.AddHttpClient(EventBusSender.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => hooks.Share());
		builder.Services.AddHostedService<WebsiteCollectorWorker>();

		var host = builder.Build();
		using (var scope = host.Services.CreateScope())
		{
			await scope.ServiceProvider.GetRequiredService<ScheduleInitializationService>().InitializeAsync();
			var stopping = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
			foreach (var subscriber in scope.ServiceProvider.GetServices<ISubscriber>())
				await subscriber.RegisterAsync(stopping);
		}

		await host.StartAsync();
		return host;
	}

	private static int Count(IEnumerable<WorkItemRow> rows, string workerType, string status) =>
		rows.Where(r => r.WorkerType == workerType && r.Status == status).Sum(r => r.Count);

	private sealed record WorkItemRow(string WorkerType, string Status, int Count);

	private static async Task<List<WorkItemRow>> QueryWorkItemsAsync()
	{
		await using var connection = TestDatabase.OpenServiceBus();
		var rows = await connection.QueryAsync<WorkItemRow>($"""
			SELECT w.WorkerType, s.Name AS Status, COUNT(*) AS Count
			FROM {TestDatabase.ServiceBusSchema}.WorkItems w
			JOIN {TestDatabase.ServiceBusSchema}.WorkItemStatuses s ON s.Id = w.StatusId
			GROUP BY w.WorkerType, s.Name
			ORDER BY w.WorkerType, s.Name
			""");
		return rows.AsList();
	}

	private static async Task<int> CountUnfinishedWorkItemsAsync()
	{
		await using var connection = TestDatabase.OpenServiceBus();
		return await connection.QuerySingleAsync<int>($"""
			SELECT COUNT(*) FROM {TestDatabase.ServiceBusSchema}.WorkItems w
			JOIN {TestDatabase.ServiceBusSchema}.WorkItemStatuses s ON s.Id = w.StatusId
			WHERE s.Name NOT IN ('SUCCEEDED', 'ABANDONED')
			""");
	}

	private static async Task<Guid> SeedSelectorAsync(int index, string? dispatchUrl)
	{
		await using var connection = TestDatabase.OpenCollector();
		return await connection.QuerySingleAsync<Guid>("""
			DECLARE @websiteId uniqueidentifier = NEWID(), @selectorId uniqueidentifier = NEWID();
			INSERT INTO dbo.Websites (Id, Name, Url) VALUES (@websiteId, @Name, @Url);
			INSERT INTO dbo.WebsiteSelectors (Id, WebsitesId, CollectionUrl, CollectionType, ContentSelector, DispatchUrl, IsEnabled)
			VALUES (@selectorId, @websiteId, @Url, 'HTML', '.content', @DispatchUrl, 1);
			SELECT @selectorId;
			""", new { Name = $"Site {index}", Url = $"https://site{index}.test/page/{index}", DispatchUrl = dispatchUrl });
	}

	/// <summary>Work items by worker type and status, schedule runs, and their last errors: for a timeout message.</summary>
	private static async Task<string> DescribeStateAsync()
	{
		var items = string.Join("; ", (await QueryWorkItemsAsync()).Select(r => $"{r.WorkerType} {r.Status}={r.Count}"));
		await using var serviceBus = TestDatabase.OpenServiceBus();
		var errors = string.Join("; ", await serviceBus.QueryAsync<string>(
			$"SELECT DISTINCT TOP 5 WorkerType + ': ' + LastError FROM {TestDatabase.ServiceBusSchema}.WorkItems WHERE LastError IS NOT NULL"));
		await using var scheduler = TestDatabase.OpenScheduler();
		var runs = string.Join("; ", await scheduler.QueryAsync<string>(
			$"SELECT Status + ' ' + CAST(ItemsRegistered AS varchar(10)) + ' ' + COALESCE(Error, '') FROM {TestDatabase.SchedulerSchema}.ScheduleExecutions"));
		var schedules = string.Join("; ", await scheduler.QueryAsync<string>(
			$"SELECT HandlerType + ' ' + Frequency + ' ' + CONVERT(varchar(8), StartTime) + ' ' + TimeZone + ' last=' + COALESCE(CONVERT(varchar(40), LastRunAt), 'never') FROM {TestDatabase.SchedulerSchema}.Schedules"));
		await using var eventBus = TestDatabase.OpenEventBus();
		var deliveries = await eventBus.QuerySingleAsync<string>(
			$"SELECT CONCAT((SELECT COUNT(*) FROM {TestDatabase.EventBusSchema}.EventBus WHERE IsEnabled = 1), ' endpoints, ', COUNT(*), ' deliveries') FROM {TestDatabase.EventBusSchema}.EventBusDeliveries");
		var logged = string.Join(" | ", Errors.Messages.Take(5));
		return $"Work items: [{items}]. Work item errors: [{errors}]. EventBus: [{deliveries}]. Runs: [{runs}]. Schedules: [{schedules}]. Logged errors ({Errors.Messages.Count}): [{logged}]";
	}

	/// <summary>Errors logged by either host, so a failure shows what the workers reported.</summary>
	private static readonly ErrorLog Errors = new();

	private sealed class ErrorLog : ILoggerProvider
	{
		public ConcurrentQueue<string> Messages { get; } = new();
		public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
		public void Dispose() { }

		private sealed class Logger(ErrorLog log, string category) : ILogger
		{
			public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
			public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;
			public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
			{
				if (IsEnabled(logLevel))
					log.Messages.Enqueue($"{category}: {formatter(state, exception)} {exception?.GetType().Name}: {exception?.Message}");
			}
		}
	}

	private static HttpResponseMessage Html(string html) =>
		new(HttpStatusCode.OK) { Content = new StringContent($"<html><body>{html}</body></html>", Encoding.UTF8, "text/html") };

	private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (!await condition())
		{
			if (DateTime.UtcNow > deadline)
				Assert.Fail($"Timed out waiting for the pipeline to finish. {await DescribeStateAsync()}");
			await Task.Delay(500);
		}
	}

	/// <summary>Records every request; shared by both hosts so totals cover all instances.</summary>
	private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
	{
		public ConcurrentQueue<(string Url, string Body)> Requests { get; } = new();

		/// <summary>A handler for one HttpClient pipeline that forwards here (handlers cannot be reused).</summary>
		public HttpMessageHandler Share() => new Forwarder(this);

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		{
			var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
			Requests.Enqueue((request.RequestUri!.ToString(), body));
			return respond(request);
		}

		private sealed class Forwarder(StubHttpHandler target) : HttpMessageHandler
		{
			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
				target.SendAsync(request, ct);
		}
	}
}
