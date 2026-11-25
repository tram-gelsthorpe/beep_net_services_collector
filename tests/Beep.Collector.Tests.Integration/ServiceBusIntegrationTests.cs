using Beep.ServiceBus;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beep.Collector.Tests.Integration;

/// <summary>
/// Crash recovery in the ServiceBus: a work item whose worker died (lease expired while CLAIMED
/// or RUNNING) must be claimed again, or abandoned if it was on its last attempt.
/// </summary>
[TestClass]
[DoNotParallelize] // shares the ServiceBus database with WebsiteCollectorIntegrationTests
public sealed class ServiceBusIntegrationTests
{
	private const string WorkerType = "CrashTest";
	private const string DomainType = "Thing";
	private const string Schema = TestDatabase.ServiceBusSchema;

	private WorkItemRepository _workItems = null!;

	[TestInitialize]
	public async Task TestInitializeAsync()
	{
		await TestDatabase.ClearDataAsync();
		_workItems = new WorkItemRepository(TestDatabase.ServiceBusConnectionString, Schema, NullLogger<WorkItemRepository>.Instance);
	}

	[TestMethod]
	public async Task Claim_WhenPreviousWorkerDied_ReclaimsItemWithNewLease()
	{
		await _workItems.EnqueueBatchAsync(WorkerType, DomainType, [new WorkItemRequest(Guid.NewGuid(), MaxAttempts: 3)]);
		var first = (await _workItems.ClaimAsync(WorkerType, Guid.NewGuid(), 10, 60)).Single();
		Assert.AreEqual(0, (await _workItems.ClaimAsync(WorkerType, Guid.NewGuid(), 10, 60)).Count, "leased");

		await ExpireLeaseAsync(first.WorkItemId);   // the first worker died
		var second = (await _workItems.ClaimAsync(WorkerType, Guid.NewGuid(), 10, 60)).Single();

		Assert.AreEqual(first.WorkItemId, second.WorkItemId);
		Assert.AreEqual(2, second.AttemptNumber);
		Assert.AreNotEqual(first.LeaseToken, second.LeaseToken);
		Assert.IsFalse(await _workItems.CompleteAsync(first.WorkItemId, first.LeaseToken, succeeded: true), "old lease is void");
		Assert.IsTrue(await _workItems.CompleteAsync(second.WorkItemId, second.LeaseToken, succeeded: true));
	}

	[TestMethod]
	public async Task Claim_WhenWorkerDiedOnLastAttempt_AbandonsItem()
	{
		await _workItems.EnqueueBatchAsync(WorkerType, DomainType, [new WorkItemRequest(Guid.NewGuid(), MaxAttempts: 1)]);
		var claimed = (await _workItems.ClaimAsync(WorkerType, Guid.NewGuid(), 10, 60)).Single();

		await ExpireLeaseAsync(claimed.WorkItemId);

		Assert.AreEqual(0, (await _workItems.ClaimAsync(WorkerType, Guid.NewGuid(), 10, 60)).Count);
		Assert.AreEqual("ABANDONED", await QueryStatusAsync(claimed.WorkItemId));
	}

	[TestMethod]
	public async Task Claim_AfterLeaseIsReleased_ClaimsItemAgain()
	{
		await _workItems.EnqueueBatchAsync(WorkerType, DomainType, [new WorkItemRequest(Guid.NewGuid())]);
		var claimed = (await _workItems.ClaimAsync(WorkerType, Guid.NewGuid(), 10, 60)).Single();

		Assert.IsTrue(await _workItems.ReleaseLeaseAsync(claimed.WorkItemId, claimed.LeaseToken));

		Assert.AreEqual(claimed.WorkItemId, (await _workItems.ClaimAsync(WorkerType, Guid.NewGuid(), 10, 60)).Single().WorkItemId);
	}

	private static async Task ExpireLeaseAsync(Guid workItemId)
	{
		await using var connection = TestDatabase.OpenServiceBus();
		await connection.ExecuteAsync(
			$"UPDATE {Schema}.WorkItems SET LeaseUntil = DATEADD(second, -1, SYSUTCDATETIME()) WHERE Id = @workItemId",
			new { workItemId });
	}

	private static async Task<string> QueryStatusAsync(Guid workItemId)
	{
		await using var connection = TestDatabase.OpenServiceBus();
		return await connection.QuerySingleAsync<string>($"""
			SELECT s.Name FROM {Schema}.WorkItems w JOIN {Schema}.WorkItemStatuses s ON s.Id = w.StatusId
			WHERE w.Id = @workItemId
			""", new { workItemId });
	}
}
