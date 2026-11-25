using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Beep.EventBus;
using Beep.ServiceBus;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace WebsiteCollector.Worker.Tests.Integration;

/// <summary>
/// EventBus on top of the ServiceBus: endpoint registration, publishing, and delivery by
/// EventBusWorker (claimed, retried and abandoned through ServiceBus work items).
/// HTTP is stubbed; nothing leaves the machine.
/// </summary>
[TestClass]
[DoNotParallelize] // shares the ServiceBus database with WebsiteCollectorIntegrationTests
public sealed class EventBusIntegrationTests
{
	private const string Schema = TestDatabase.EventBusSchema;

	private readonly EventBusOptions _options = new() { PollIntervalSeconds = 1, LeaseSeconds = 60 };
	private readonly StubHttpHandler _http = new();

	private WorkItemRepository _workItems = null!;
	private EventBusRepository _repository = null!;
	private EventBusPublisher _publisher = null!;
	private EventBusWorkProcessor _processor = null!;

	[TestInitialize]
	public async Task TestInitializeAsync()
	{
		await TestDatabase.ClearDataAsync();

		_workItems = new WorkItemRepository(TestDatabase.ServiceBusConnectionString, TestDatabase.ServiceBusSchema, NullLogger<WorkItemRepository>.Instance);
		_repository = new EventBusRepository(TestDatabase.EventBusConnectionString, Schema);
		_publisher = new EventBusPublisher(_repository, _workItems, _options, NullLogger<EventBusPublisher>.Instance);
		_processor = new EventBusWorkProcessor(
			_repository,
			new EventBusSender(new StubHttpClientFactory(_http), NullLogger<EventBusSender>.Instance),
			_workItems,
			_options,
			NullLogger<EventBusWorkProcessor>.Instance);
	}

	[TestCleanup]
	public void TestCleanup() => _http.Dispose();

	[TestMethod]
	public async Task RegisterEndpoints_AppliesOnlyChanges_AndLeavesUnlistedOwnersAlone()
	{
		var a = Guid.NewGuid();
		var b = Guid.NewGuid();

		var first = await _repository.RegisterEndpointsAsync(
			[new(a, "https://a.example.com/1"), new(a, "https://a.example.com/2"), new(b, "https://b.example.com")], default);
		var again = await _repository.RegisterEndpointsAsync(
			[new(a, "https://a.example.com/1"), new(a, "https://a.example.com/2")], default);   // b not listed
		var updated = await _repository.RegisterEndpointsAsync(
			[new(a, "https://a.example.com/1", TimeoutSeconds: 60), new(a, "not a url")], default);

		Assert.AreEqual(new EndpointRegistrationResult(3, 0), first);
		Assert.AreEqual(new EndpointRegistrationResult(0, 0), again, "unchanged, and b is untouched");
		Assert.AreEqual(new EndpointRegistrationResult(1, 1), updated, "timeout changed; /2 disabled; invalid URL ignored");
		Assert.AreEqual(1, await CountEnabledEndpointsAsync(a));
		Assert.AreEqual(1, await CountEnabledEndpointsAsync(b));
	}

	[TestMethod]
	public async Task Publish_QueuesOneDeliveryPerEnabledEndpoint_AndRepublishDoesNotDuplicate()
	{
		var owner = Guid.NewGuid();
		await _repository.RegisterEndpointsAsync([new(owner, "https://a.example.com"), new(owner, "https://b.example.com")], default);
		var eventId = Guid.NewGuid();

		Assert.AreEqual(2, await _publisher.PublishAsync(owner, eventId, new { Value = 1 }));
		Assert.AreEqual(0, await _publisher.PublishAsync(owner, eventId, new { Value = 1 }), "already queued");
		Assert.AreEqual(0, await _publisher.PublishAsync(Guid.NewGuid(), Guid.NewGuid(), new { Value = 1 }), "owner has no endpoints");

		Assert.AreEqual(2, await CountAsync(TestDatabase.OpenEventBus(), $"SELECT COUNT(*) FROM {Schema}.EventBusDeliveries"));
		Assert.AreEqual(2, await CountAsync(TestDatabase.OpenServiceBus(), $"SELECT COUNT(*) FROM {TestDatabase.ServiceBusSchema}.WorkItems WHERE DomainType = '{EventBusPublisher.DeliveryDomainType}'"));
		await Assert.ThrowsExactlyAsync<ArgumentException>(() => _publisher.PublishAsync(owner, Guid.Empty, new { }));
	}

	[TestMethod]
	public async Task TwoWorkers_DeliverEachDeliveryExactlyOnce()
	{
		var owner = Guid.NewGuid();
		await _repository.RegisterEndpointsAsync([new(owner, "https://a.example.com"), new(owner, "https://b.example.com")], default);
		var eventIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
		foreach (var eventId in eventIds)
			await _publisher.PublishAsync(owner, eventId, new { EventId = eventId });
		_http.Delay = TimeSpan.FromMilliseconds(300); // keep deliveries in flight across polls

		var workers = Enumerable.Range(0, 2)
			.Select(_ => new EventBusWorker(_workItems, _processor, _options, NullLogger<EventBusWorker>.Instance))
			.ToList();
		foreach (var worker in workers)
			await worker.StartAsync(CancellationToken.None);
		try
		{
			await WaitUntilAsync(async () => await CountDeliveredAsync() == 10, TimeSpan.FromSeconds(30));
			await Task.Delay(TimeSpan.FromSeconds(2)); // give a duplicate the chance to show up
		}
		finally
		{
			foreach (var worker in workers)
				await worker.StopAsync(CancellationToken.None);
		}

		Assert.AreEqual(10, _http.Requests.Count, "each delivery POSTed once");
		Assert.AreEqual(10, _http.Requests.Select(r => (r.Url, r.Body)).Distinct().Count());
		Assert.AreEqual(10, await CountAsync(TestDatabase.OpenServiceBus(), $"""
			SELECT COUNT(*) FROM {TestDatabase.ServiceBusSchema}.WorkItems w
			JOIN {TestDatabase.ServiceBusSchema}.WorkItemStatuses s ON s.Id = w.StatusId
			WHERE s.Name = 'SUCCEEDED' AND w.AttemptCount = 1
			"""));
	}

	[TestMethod]
	public async Task FailedDelivery_IsRetriedByServiceBus_ThenAbandoned()
	{
		var owner = Guid.NewGuid();
		await _repository.RegisterEndpointsAsync([new(owner, "https://a.example.com", MaxAttempts: 2)], default);
		await _publisher.PublishAsync(owner, Guid.NewGuid(), new { });
		_http.Status = HttpStatusCode.InternalServerError;

		var workItemId = await ProcessOneAsync();
		Assert.AreEqual("FAILED", await QueryStatusAsync(workItemId), "retry scheduled");
		Assert.AreEqual(500, await QueryDeliveryAsync<int?>("HttpStatusCode"));
		Assert.IsNull(await QueryDeliveryAsync<DateTimeOffset?>("DeliveredAt"));

		await MakeDueAsync(workItemId);
		await ProcessOneAsync();
		Assert.AreEqual("ABANDONED", await QueryStatusAsync(workItemId), "MaxAttempts reached");
		Assert.AreEqual(2, _http.Requests.Count);
	}

	[TestMethod]
	public async Task Delivery_ToEndpointDisabledAfterPublish_IsSkipped()
	{
		var owner = Guid.NewGuid();
		await _repository.RegisterEndpointsAsync([new(owner, "https://a.example.com")], default);
		await _publisher.PublishAsync(owner, Guid.NewGuid(), new { });
		await _repository.RegisterEndpointsAsync([new(owner, null)], default); // endpoint removed

		var workItemId = await ProcessOneAsync();

		Assert.AreEqual(0, _http.Requests.Count);
		Assert.AreEqual("SUCCEEDED", await QueryStatusAsync(workItemId));
	}

	[TestMethod]
	public async Task CompletedCollection_IsDeliveredToSelectorDispatchUrl()
	{
		// Selector with a DispatchUrl, registered by the schedule handler like in production.
		var collectorOptions = new WebsiteCollectorOptions();
		var selectorId = await SeedSelectorAsync("https://hooks.example.com/rates");
		var selectors = new SelectorManagementRepository(TestDatabase.CollectorConnectionString, NullLogger<SelectorManagementRepository>.Instance);
		await new WebsiteCollectorScheduleHandler(_workItems, _repository, selectors, collectorOptions.WorkerType, Guid.NewGuid(),
				NullLogger<WebsiteCollectorScheduleHandler>.Instance)
			.RegisterItemsAsync(new Beep.Scheduler.WorkerSchedule(), CancellationToken.None);

		var events = new EventAggregator();
		await new WebsiteSelectorsSubscriber(events, _publisher, NullLogger<WebsiteSelectorsSubscriber>.Instance).RegisterAsync();
		var service = new WebsiteSelectorsService(
			new WebsiteSelectorsRepository(_workItems, selectors, Options.Create(collectorOptions), NullLogger<WebsiteSelectorsRepository>.Instance),
			events);

		// Collect: claim, start, complete with content.
		var workerId = Guid.NewGuid();
		var item = (await service.ClaimSelectorsAsync(workerId, 1, 300, CancellationToken.None)).Single();
		await service.StartExecutionAsync(item.WorkItemId, item.LeaseToken, 300, workerId, selectorId, item.TimeoutSeconds, item.MaxAttempts, CancellationToken.None);
		const string content = "<p class=\"content\">1.23</p>";
		await service.CompleteExecutionAsync(item.WorkItemId, selectorId, item.LeaseToken, true, 200, null,
			Encoding.UTF8.GetBytes(content), content, "{}", CancellationToken.None);

		await ProcessOneAsync();

		await using var collector = TestDatabase.OpenCollector();
		var collectedDataId = await collector.QuerySingleAsync<Guid>("SELECT Id FROM dbo.WebsiteSelectorsCollectedData");
		var request = _http.Requests.Single();
		Assert.AreEqual("https://hooks.example.com/rates", request.Url);
		using var body = JsonDocument.Parse(request.Body);
		Assert.AreEqual(collectedDataId, body.RootElement.GetProperty("CollectedDataId").GetGuid());
		Assert.AreEqual(selectorId, body.RootElement.GetProperty("WebsiteSelectorsId").GetGuid());
	}

	/// <summary>Claim one delivery work item through the ServiceBus and run it like EventBusWorker does.</summary>
	private async Task<Guid> ProcessOneAsync()
	{
		var lease = new WorkItemLeaseManager(_workItems, _options.WorkerType);
		var claimed = (await lease.ClaimAsync(Guid.NewGuid(), 1, _options.LeaseSeconds, CancellationToken.None)).Single();
		var result = await _processor.ProcessAsync(claimed.Item, claimed.LeaseToken, CancellationToken.None);
		await _processor.CompleteAsync(claimed.Item, claimed.LeaseToken, result, CancellationToken.None);
		return claimed.Item.WorkItemId;
	}

	private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (!await condition())
		{
			if (DateTime.UtcNow > deadline)
				Assert.Fail("Timed out waiting for deliveries");
			await Task.Delay(200);
		}
	}

	private static Task<int> CountDeliveredAsync() =>
		CountAsync(TestDatabase.OpenEventBus(), $"SELECT COUNT(*) FROM {Schema}.EventBusDeliveries WHERE DeliveredAt IS NOT NULL");

	private static Task<int> CountEnabledEndpointsAsync(Guid ownerId) =>
		CountAsync(TestDatabase.OpenEventBus(), $"SELECT COUNT(*) FROM {Schema}.EventBus WHERE IsEnabled = 1 AND EventBusOwnerId = '{ownerId}'");

	private static async Task<int> CountAsync(Microsoft.Data.SqlClient.SqlConnection connection, string sql)
	{
		await using (connection)
			return await connection.QuerySingleAsync<int>(sql);
	}

	private static async Task<T> QueryDeliveryAsync<T>(string column)
	{
		await using var connection = TestDatabase.OpenEventBus();
		return await connection.QuerySingleAsync<T>($"SELECT {column} FROM {Schema}.EventBusDeliveries");
	}

	private static async Task<string> QueryStatusAsync(Guid workItemId)
	{
		await using var connection = TestDatabase.OpenServiceBus();
		return await connection.QuerySingleAsync<string>($"""
			SELECT s.Name FROM {TestDatabase.ServiceBusSchema}.WorkItems w
			JOIN {TestDatabase.ServiceBusSchema}.WorkItemStatuses s ON s.Id = w.StatusId
			WHERE w.Id = @workItemId
			""", new { workItemId });
	}

	private static async Task MakeDueAsync(Guid workItemId)
	{
		await using var connection = TestDatabase.OpenServiceBus();
		await connection.ExecuteAsync(
			$"UPDATE {TestDatabase.ServiceBusSchema}.WorkItems SET NextRunAt = SYSUTCDATETIME() WHERE Id = @workItemId",
			new { workItemId });
	}

	private static async Task<Guid> SeedSelectorAsync(string dispatchUrl)
	{
		await using var connection = TestDatabase.OpenCollector();
		return await connection.QuerySingleAsync<Guid>("""
			DECLARE @websiteId uniqueidentifier = NEWID(), @selectorId uniqueidentifier = NEWID();
			INSERT INTO dbo.Websites (Id, Name, Url) VALUES (@websiteId, 'Test', 'https://example.com');
			INSERT INTO dbo.WebsiteSelectors (Id, WebsitesId, CollectionUrl, CollectionType, ContentSelector, DispatchUrl)
			VALUES (@selectorId, @websiteId, 'https://example.com/rates', 'HTML', '.content', @dispatchUrl);
			SELECT @selectorId;
			""", new { dispatchUrl });
	}

	private sealed class StubHttpHandler : HttpMessageHandler
	{
		public ConcurrentQueue<(string Url, string Body)> Requests { get; } = new();
		public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
		public TimeSpan Delay { get; set; } = TimeSpan.Zero;

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		{
			var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
			Requests.Enqueue((request.RequestUri!.ToString(), body));
			if (Delay > TimeSpan.Zero)
				await Task.Delay(Delay, ct);
			return new HttpResponseMessage(Status);
		}
	}

	private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
	}
}
