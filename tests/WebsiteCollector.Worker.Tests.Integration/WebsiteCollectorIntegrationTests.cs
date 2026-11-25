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

	private async Task<Guid> EnqueueAsync(Guid selectorId, int maxAttempts = 5) =>
		await _workItems.EnqueueAsync(_options.WorkerType, selectorId, DomainType, maxAttempts);

	private static async Task<Guid> SeedSelectorAsync(
		string collectionUrl = "https://example.com",
		string collectionType = "HTML",
		bool isEnabled = true)
	{
		await using var connection = TestDatabase.OpenCollector();
		return await connection.QuerySingleAsync<Guid>("""
			DECLARE @websiteId uniqueidentifier = NEWID(), @selectorId uniqueidentifier = NEWID();
			INSERT INTO dbo.Websites (Id, Name, Url) VALUES (@websiteId, 'Test', @CollectionUrl);
			INSERT INTO dbo.WebsiteSelectors (Id, WebsitesId, CollectionUrl, CollectionType, ContentSelector, IsEnabled)
			VALUES (@selectorId, @websiteId, @CollectionUrl, @CollectionType, '.content', @IsEnabled);
			SELECT @selectorId;
			""", new { CollectionUrl = collectionUrl, CollectionType = collectionType, IsEnabled = isEnabled });
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
