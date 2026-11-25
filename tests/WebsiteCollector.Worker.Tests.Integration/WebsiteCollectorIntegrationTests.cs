using System.Security.Cryptography;
using System.Text;
using Beep.EventBus;
using Beep.ServiceBus;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace WebsiteCollector.Worker.Tests.Integration;

/// <summary>
/// Integration tests for the website collector work flow against the real embedded schemas:
/// claim -> start -> renew -> complete.
/// </summary>
[TestClass]
public sealed class WebsiteCollectorIntegrationTests
{
	private const string DomainType = "WebsiteSelector";
	private readonly WebsiteCollectorOptions _options = new();

	private WorkItemRepository _workItems = null!;
	private IWebsiteSelectorsService _service = null!;

	[TestInitialize]
	public async Task TestInitializeAsync()
	{
		await TestDatabase.ClearDataAsync();

		_workItems = new WorkItemRepository(
			TestDatabase.ServiceBusConnectionString,
			TestDatabase.ServiceBusSchema,
			NullLogger<WorkItemRepository>.Instance);

		var selectors = new SelectorManagementRepository(
			TestDatabase.CollectorConnectionString,
			NullLogger<SelectorManagementRepository>.Instance);

		var repository = new WebsiteSelectorsRepository(
			_workItems,
			selectors,
			Options.Create(_options),
			NullLogger<WebsiteSelectorsRepository>.Instance);

		_service = new WebsiteSelectorsService(repository, new EventAggregator());
	}

	[TestMethod]
	public async Task CreateDatabase_WhenAlreadyCreated_IsIdempotent()
	{
		await TestDatabase.CreateAsync();

		await using var connection = TestDatabase.OpenServiceBus();
		var versions = await connection.QueryAsync<string>($"SELECT Version FROM {TestDatabase.ServiceBusSchema}.DatabaseVersion");
		CollectionAssert.AreEqual(new[] { "0" }, versions.ToArray());
	}

	[TestMethod]
	public async Task ClaimSelectors_WithAvailableItem_ReturnsEnrichedSelector()
	{
		var selectorId = await SeedSelectorAsync("https://example.com/data", "HTML");
		var workItemId = await EnqueueAsync(selectorId);

		var claimed = await _service.ClaimSelectorsAsync(Guid.NewGuid(), count: 5, lease: 300, CancellationToken.None);

		Assert.HasCount(1, claimed);
		Assert.AreEqual(workItemId, claimed[0].WorkItemId);
		Assert.AreEqual(selectorId, claimed[0].DomainId);
		Assert.AreEqual("https://example.com/data", claimed[0].CollectionUrl);
		Assert.AreEqual("HTML", claimed[0].CollectionType);
		Assert.AreNotEqual(Guid.Empty, claimed[0].LeaseToken);
	}

	[TestMethod]
	public async Task ClaimSelectors_WhenLeasedByAnotherWorker_ReturnsNothing()
	{
		await EnqueueAsync(await SeedSelectorAsync());
		var first = await _service.ClaimSelectorsAsync(Guid.NewGuid(), count: 5, lease: 300, CancellationToken.None);
		Assert.HasCount(1, first);

		var second = await _service.ClaimSelectorsAsync(Guid.NewGuid(), count: 5, lease: 300, CancellationToken.None);

		Assert.IsEmpty(second, "Items with an active lease must not be claimed again");
	}

	[TestMethod]
	public async Task ClaimSelectors_WithMultipleItems_ClaimsAll()
	{
		var selectorIds = new List<Guid>();
		for (var i = 0; i < 3; i++)
		{
			var selectorId = await SeedSelectorAsync($"https://example.com/page{i}");
			await EnqueueAsync(selectorId);
			selectorIds.Add(selectorId);
		}

		var claimed = await _service.ClaimSelectorsAsync(Guid.NewGuid(), count: 5, lease: 300, CancellationToken.None);

		CollectionAssert.AreEquivalent(selectorIds, claimed.Select(c => c.DomainId).ToList());
	}

	[TestMethod]
	public async Task ClaimSelectors_WithDisabledSelector_SkipsIt()
	{
		await EnqueueAsync(await SeedSelectorAsync(isEnabled: false));

		var claimed = await _service.ClaimSelectorsAsync(Guid.NewGuid(), count: 5, lease: 300, CancellationToken.None);

		Assert.IsEmpty(claimed, "Disabled selectors should not be returned for processing");
	}

	[TestMethod]
	public async Task StartExecution_RecordsRunningExecutionAndMonitor()
	{
		var selectorId = await SeedSelectorAsync();
		await EnqueueAsync(selectorId);
		var workerId = Guid.NewGuid();
		var item = (await _service.ClaimSelectorsAsync(workerId, 1, 300, CancellationToken.None)).Single();

		await _service.StartExecutionAsync(item.WorkItemId, item.LeaseToken, 300, workerId, selectorId, item.TimeoutSeconds, item.MaxAttempts, CancellationToken.None);

		await using var collector = TestDatabase.OpenCollector();
		var executionStatus = await collector.QuerySingleAsync<string>(
			"SELECT Status FROM dbo.WebsiteSelectorsExecutions WHERE LeaseToken = @LeaseToken", new { item.LeaseToken });
		var monitorLease = await collector.QuerySingleAsync<Guid?>(
			"SELECT LeaseToken FROM dbo.WebsiteSelectorsMonitor WHERE WebsiteSelectorsId = @selectorId", new { selectorId });

		Assert.AreEqual("Running", executionStatus);
		Assert.AreEqual(item.LeaseToken, monitorLease);
	}

	[TestMethod]
	public async Task RenewLease_WithValidLease_ExtendsLeaseUntil()
	{
		var workItemId = await EnqueueAsync(await SeedSelectorAsync());
		var item = (await _service.ClaimSelectorsAsync(Guid.NewGuid(), 1, 60, CancellationToken.None)).Single();

		var renewed = await _service.RenewLeaseAsync(item.WorkItemId, item.LeaseToken, leaseSeconds: 600, CancellationToken.None);

		Assert.IsTrue(renewed);
		var leaseUntil = await QueryWorkItemAsync<DateTimeOffset>(workItemId, "LeaseUntil");
		Assert.IsGreaterThan(DateTimeOffset.UtcNow.AddSeconds(590), leaseUntil);
	}

	[TestMethod]
	public async Task RenewLease_WithWrongToken_IsRejected()
	{
		var workItemId = await EnqueueAsync(await SeedSelectorAsync());
		await _service.ClaimSelectorsAsync(Guid.NewGuid(), 1, 60, CancellationToken.None);

		var renewed = await _service.RenewLeaseAsync(workItemId, Guid.NewGuid(), leaseSeconds: 600, CancellationToken.None);

		Assert.IsFalse(renewed);
	}

	[TestMethod]
	public async Task CompleteExecution_WithSuccess_MarksSucceededAndStoresData()
	{
		var selectorId = await SeedSelectorAsync();
		var workItemId = await EnqueueAsync(selectorId);
		var workerId = Guid.NewGuid();
		var item = (await _service.ClaimSelectorsAsync(workerId, 1, 300, CancellationToken.None)).Single();
		await _service.StartExecutionAsync(item.WorkItemId, item.LeaseToken, 300, workerId, selectorId, item.TimeoutSeconds, item.MaxAttempts, CancellationToken.None);

		const string content = "{\"status\": \"success\"}";
		var result = await _service.CompleteExecutionAsync(
			item.WorkItemId, selectorId, item.LeaseToken,
			success: true, httpStatus: 200, error: null,
			hash: SHA256.HashData(Encoding.UTF8.GetBytes(content)),
			raw: content, extracted: "{\"processed\": true}",
			CancellationToken.None);

		Assert.AreEqual(0, result);
		Assert.AreEqual("SUCCEEDED", await QueryWorkItemStatusAsync(workItemId));

		await using var collector = TestDatabase.OpenCollector();
		var stored = await collector.QuerySingleAsync<string>("SELECT ResponseContent FROM dbo.WebsiteSelectorsCollectedData");
		var executionStatus = await collector.QuerySingleAsync<string>("SELECT Status FROM dbo.WebsiteSelectorsExecutions");
		Assert.AreEqual(content, stored);
		Assert.AreEqual("Succeeded", executionStatus);
	}

	[TestMethod]
	public async Task CompleteExecution_WithFailure_SchedulesRetry()
	{
		var workItemId = await EnqueueAsync(await SeedSelectorAsync(), maxAttempts: 3);
		var item = (await _service.ClaimSelectorsAsync(Guid.NewGuid(), 1, 300, CancellationToken.None)).Single();

		await _service.CompleteExecutionAsync(
			item.WorkItemId, item.DomainId, item.LeaseToken,
			success: false, httpStatus: 500, error: "Internal Server Error",
			hash: null, raw: null, extracted: null,
			CancellationToken.None);

		Assert.AreEqual("FAILED", await QueryWorkItemStatusAsync(workItemId));
		Assert.IsGreaterThan(DateTimeOffset.UtcNow, await QueryWorkItemAsync<DateTimeOffset>(workItemId, "NextRunAt"));
	}

	[TestMethod]
	public async Task CompleteExecution_WithStaleLease_ReportsLeaseLost()
	{
		var workItemId = await EnqueueAsync(await SeedSelectorAsync());
		var item = (await _service.ClaimSelectorsAsync(Guid.NewGuid(), 1, 300, CancellationToken.None)).Single();

		var result = await _service.CompleteExecutionAsync(
			item.WorkItemId, item.DomainId, Guid.NewGuid(),
			success: true, httpStatus: 200, error: null,
			hash: null, raw: null, extracted: null,
			CancellationToken.None);

		Assert.AreEqual(-1, result);
		Assert.AreEqual("CLAIMED", await QueryWorkItemStatusAsync(workItemId));
	}

	[TestMethod]
	public async Task EnqueueBatch_SkipsEntitiesWithUnfinishedWorkItem()
	{
		var queued = Guid.NewGuid();
		var finished = Guid.NewGuid();
		var fresh = Guid.NewGuid();

		var queuedItem = await EnqueueAsync(queued);
		var finishedItem = await EnqueueAsync(finished);
		await SetWorkItemStatusAsync(finishedItem, "SUCCEEDED");

		var inserted = await _workItems.EnqueueBatchAsync(
			_options.WorkerType, DomainType,
			[new(queued), new(finished, MaxAttempts: 3), new(fresh), new(fresh)]);

		Assert.AreEqual(2, inserted, "finished and fresh are enqueued; queued is skipped; duplicates collapse");
		Assert.AreEqual(1, await CountWorkItemsAsync(queued));
		Assert.AreEqual(2, await CountWorkItemsAsync(finished));
		Assert.AreEqual(1, await CountWorkItemsAsync(fresh));
		Assert.AreEqual(0, await _workItems.EnqueueBatchAsync(_options.WorkerType, DomainType, [new(queued), new(fresh)]));
		Assert.AreEqual("PENDING", await QueryWorkItemStatusAsync(queuedItem));
	}

	[TestMethod]
	public async Task ScheduleHandler_PagesThroughEnabledSelectors_AndDoesNotDuplicateOnRerun()
	{
		var enabled = new List<Guid>();
		for (var i = 0; i < 7; i++)
			enabled.Add(await SeedSelectorAsync($"https://example.com/{i}", dispatchUrl: i < 5 ? $"https://hooks.example.com/{i}" : null));
		var disabled = await SeedSelectorAsync("https://example.com/disabled", isEnabled: false, dispatchUrl: "https://hooks.example.com/disabled");

		var handler = CreateScheduleHandler(pageSize: 3);

		var first = await handler.RegisterItemsAsync(new Beep.Scheduler.WorkerSchedule(), CancellationToken.None);
		var second = await handler.RegisterItemsAsync(new Beep.Scheduler.WorkerSchedule(), CancellationToken.None);

		Assert.IsTrue(first.Success, first.Error);
		Assert.AreEqual(7, first.ItemsRegistered);
		foreach (var selectorId in enabled)
			Assert.AreEqual(1, await CountWorkItemsAsync(selectorId));
		Assert.AreEqual(0, await CountWorkItemsAsync(disabled));

		// Endpoints: one per enabled selector with a DispatchUrl, none for the rest.
		for (var i = 0; i < enabled.Count; i++)
			Assert.AreEqual(i < 5 ? 1 : 0, await CountEnabledEndpointsAsync(enabled[i]), $"selector {i}");
		Assert.AreEqual(0, await CountEnabledEndpointsAsync(disabled));
		StringAssert.Contains(first.Details, "Endpoints changed: 5");

		Assert.IsTrue(second.Success, second.Error);
		Assert.AreEqual(0, second.ItemsRegistered, second.Details);
		StringAssert.Contains(second.Details, "Endpoints changed: 0, disabled: 0");
	}

	[TestMethod]
	public async Task ScheduleHandler_WhenDispatchUrlChangesOrIsRemoved_UpdatesEndpoints()
	{
		var changed = await SeedSelectorAsync("https://example.com/a", dispatchUrl: "https://hooks.example.com/old");
		var removed = await SeedSelectorAsync("https://example.com/b", dispatchUrl: "https://hooks.example.com/b");
		var handler = CreateScheduleHandler();
		await handler.RegisterItemsAsync(new Beep.Scheduler.WorkerSchedule(), CancellationToken.None);

		await using (var connection = TestDatabase.OpenCollector())
		{
			await connection.ExecuteAsync("UPDATE dbo.WebsiteSelectors SET DispatchUrl = 'https://hooks.example.com/new' WHERE Id = @changed", new { changed });
			await connection.ExecuteAsync("UPDATE dbo.WebsiteSelectors SET DispatchUrl = NULL WHERE Id = @removed", new { removed });
		}

		var result = await handler.RegisterItemsAsync(new Beep.Scheduler.WorkerSchedule(), CancellationToken.None);

		StringAssert.Contains(result.Details, "Endpoints changed: 1, disabled: 2");
		await using var eventBus = TestDatabase.OpenEventBus();
		CollectionAssert.AreEqual(
			new[] { "https://hooks.example.com/new" },
			(await eventBus.QueryAsync<string>(
				$"SELECT EventBusOwnerDeliveryUrl FROM {TestDatabase.EventBusSchema}.EventBus WHERE IsEnabled = 1 AND EventBusOwnerId IN @ids",
				new { ids = new[] { changed, removed } })).ToArray());
	}

	private WebsiteCollectorScheduleHandler CreateScheduleHandler(int pageSize = WebsiteCollectorScheduleHandler.DefaultPageSize) => new(
		_workItems,
		new EventBusRepository(TestDatabase.EventBusConnectionString, TestDatabase.EventBusSchema),
		new SelectorManagementRepository(TestDatabase.CollectorConnectionString, NullLogger<SelectorManagementRepository>.Instance),
		_options.WorkerType,
		Guid.NewGuid(),
		NullLogger<WebsiteCollectorScheduleHandler>.Instance,
		pageSize);

	private static async Task<int> CountEnabledEndpointsAsync(Guid ownerId)
	{
		await using var connection = TestDatabase.OpenEventBus();
		return await connection.QuerySingleAsync<int>(
			$"SELECT COUNT(*) FROM {TestDatabase.EventBusSchema}.EventBus WHERE EventBusOwnerId = @ownerId AND IsEnabled = 1",
			new { ownerId });
	}

	private static async Task<int> CountWorkItemsAsync(Guid domainId)
	{
		await using var connection = TestDatabase.OpenServiceBus();
		return await connection.QuerySingleAsync<int>(
			$"SELECT COUNT(*) FROM {TestDatabase.ServiceBusSchema}.WorkItems WHERE DomainId = @domainId", new { domainId });
	}

	private static async Task SetWorkItemStatusAsync(Guid workItemId, string status)
	{
		await using var connection = TestDatabase.OpenServiceBus();
		await connection.ExecuteAsync($"""
			UPDATE {TestDatabase.ServiceBusSchema}.WorkItems
			SET StatusId = (SELECT Id FROM {TestDatabase.ServiceBusSchema}.WorkItemStatuses WHERE Name = @status)
			WHERE Id = @workItemId
			""", new { workItemId, status });
	}

	/// <summary>Enqueues one work item through the batch API and returns its Id.</summary>
	private async Task<Guid> EnqueueAsync(Guid selectorId, int maxAttempts = 5)
	{
		var inserted = await _workItems.EnqueueBatchAsync(
			_options.WorkerType, DomainType, [new WorkItemRequest(selectorId, maxAttempts)]);
		Assert.AreEqual(1, inserted, $"Selector {selectorId} already had an unfinished work item");

		await using var connection = TestDatabase.OpenServiceBus();
		return await connection.QuerySingleAsync<Guid>($"""
			SELECT TOP 1 Id FROM {TestDatabase.ServiceBusSchema}.WorkItems
			WHERE DomainId = @selectorId ORDER BY CreatedAt DESC
			""", new { selectorId });
	}

	private static async Task<Guid> SeedSelectorAsync(
		string collectionUrl = "https://example.com",
		string collectionType = "HTML",
		bool isEnabled = true,
		string? dispatchUrl = null)
	{
		await using var connection = TestDatabase.OpenCollector();
		return await connection.QuerySingleAsync<Guid>("""
			DECLARE @websiteId uniqueidentifier = NEWID(), @selectorId uniqueidentifier = NEWID();
			INSERT INTO dbo.Websites (Id, Name, Url) VALUES (@websiteId, 'Test', @CollectionUrl);
			INSERT INTO dbo.WebsiteSelectors (Id, WebsitesId, CollectionUrl, CollectionType, ContentSelector, DispatchUrl, IsEnabled)
			VALUES (@selectorId, @websiteId, @CollectionUrl, @CollectionType, '.content', @DispatchUrl, @IsEnabled);
			SELECT @selectorId;
			""", new { CollectionUrl = collectionUrl, CollectionType = collectionType, DispatchUrl = dispatchUrl, IsEnabled = isEnabled });
	}

	private static async Task<string> QueryWorkItemStatusAsync(Guid workItemId)
	{
		await using var connection = TestDatabase.OpenServiceBus();
		return await connection.QuerySingleAsync<string>($"""
			SELECT s.Name
			FROM {TestDatabase.ServiceBusSchema}.WorkItems w
			JOIN {TestDatabase.ServiceBusSchema}.WorkItemStatuses s ON s.Id = w.StatusId
			WHERE w.Id = @workItemId
			""", new { workItemId });
	}

	private static async Task<T> QueryWorkItemAsync<T>(Guid workItemId, string column)
	{
		await using var connection = TestDatabase.OpenServiceBus();
		return await connection.QuerySingleAsync<T>(
			$"SELECT {column} FROM {TestDatabase.ServiceBusSchema}.WorkItems WHERE Id = @workItemId", new { workItemId });
	}
}
