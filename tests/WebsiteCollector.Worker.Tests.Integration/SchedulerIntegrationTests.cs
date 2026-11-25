using Beep.Scheduler;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;

namespace WebsiteCollector.Worker.Tests.Integration;

/// <summary>
/// Integration tests for when a schedule runs: StartTime, once per day, one row per worker type,
/// and exactly one worker instance claiming a due schedule.
/// Most tests call ClaimDueSchedules with a fixed @Now so they do not depend on the time of day.
/// </summary>
[TestClass]
public sealed class SchedulerIntegrationTests
{
	private const string Schema = TestDatabase.SchedulerSchema;
	private static readonly DateTimeOffset Day = new(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);

	private SchedulerRepository _repository = null!;

	[TestInitialize]
	public async Task TestInitializeAsync()
	{
		await TestDatabase.ClearSchedulerDataAsync();
		_repository = new SchedulerRepository(TestDatabase.SchedulerConnectionString, Schema, NullLogger<SchedulerRepository>.Instance);
	}

	[TestMethod]
	public async Task CreateOrUpdateSchedule_CalledConcurrently_KeepsOneRowPerWorkerType()
	{
		await Task.WhenAll(Enumerable.Range(0, 20).Select(i => _repository.CreateOrUpdateScheduleAsync(
			"Collector", $"run {i}", "Daily", new TimeOnly(2, 0), isActive: true, CancellationToken.None)));

		await using var connection = TestDatabase.OpenScheduler();
		Assert.AreEqual(1, await connection.QuerySingleAsync<int>(
			$"SELECT COUNT(*) FROM {Schema}.WorkerSchedules WHERE WorkerType = 'Collector'"));
	}

	[TestMethod]
	public async Task Daily_RunsAtOrAfterStartTime_OncePerDay()
	{
		var id = await InsertScheduleAsync("Daily", new TimeSpan(2, 0, 0));

		Assert.AreEqual(0, (await ClaimAsync(Day.AddHours(1).AddMinutes(59))).Count, "before StartTime");
		CollectionAssert.AreEqual(new[] { id }, (await ClaimAsync(Day.AddHours(2))).ToArray(), "at StartTime");
		Assert.AreEqual(0, (await ClaimAsync(Day.AddHours(2).AddMinutes(1))).Count, "claimed by the first caller");

		await _repository.LogExecutionAsync(id, "Success", 0, null, "Test", DateTimeOffset.UtcNow, CancellationToken.None);

		Assert.AreEqual(Day.AddHours(2), await QueryAsync<DateTimeOffset>(id, "LastRunAt"), "LastRunAt is the claim time");
		Assert.IsNull(await QueryAsync<DateTimeOffset?>(id, "ClaimedUntil"), "claim released");
		Assert.AreEqual(0, (await ClaimAsync(Day.AddHours(23))).Count, "already ran today");
		Assert.AreEqual(0, (await ClaimAsync(Day.AddDays(1).AddHours(1))).Count, "next day, before StartTime");
		Assert.AreEqual(1, (await ClaimAsync(Day.AddDays(1).AddHours(2))).Count, "next day, at StartTime");
	}

	[TestMethod]
	public async Task Daily_WhenWorkerWasDownAtStartTime_CatchesUpLaterThatDay()
	{
		await InsertScheduleAsync("Daily", new TimeSpan(2, 0, 0));

		Assert.AreEqual(1, (await ClaimAsync(Day.AddHours(15))).Count);
	}

	[TestMethod]
	public async Task Claim_WhenInstanceDiesMidRun_ExpiresAfterLease()
	{
		await InsertScheduleAsync("Daily", TimeSpan.Zero);

		Assert.AreEqual(1, (await ClaimAsync(Day.AddHours(2), leaseSeconds: 3600)).Count);
		Assert.AreEqual(0, (await ClaimAsync(Day.AddHours(2).AddMinutes(59), leaseSeconds: 3600)).Count, "lease still held");
		Assert.AreEqual(1, (await ClaimAsync(Day.AddHours(3), leaseSeconds: 3600)).Count, "lease expired");
	}

	[TestMethod]
	public async Task ReleaseClaim_MakesScheduleDueAgainImmediately()
	{
		var id = await InsertScheduleAsync("Daily", TimeSpan.Zero);
		Assert.AreEqual(1, (await ClaimAsync(Day.AddHours(2))).Count);

		await _repository.ReleaseClaimAsync(id, CancellationToken.None);

		Assert.AreEqual(1, (await ClaimAsync(Day.AddHours(2).AddMinutes(1))).Count);
	}

	[TestMethod]
	public async Task Claim_ByConcurrentInstances_HandsScheduleToExactlyOne()
	{
		// Due right now on the real database clock: StartTime midnight, never run.
		await InsertScheduleAsync("Daily", TimeSpan.Zero);

		var results = await Task.WhenAll(Enumerable.Range(0, 20)
			.Select(_ => _repository.ClaimDueSchedulesAsync(3600, CancellationToken.None)));

		Assert.AreEqual(1, results.Sum(r => r.Count));
	}

	[TestMethod]
	public async Task Weekly_MatchesDayOfWeek_RegardlessOfDateFirst()
	{
		await InsertScheduleAsync("Weekly", TimeSpan.Zero, dayOfWeek: (int)Day.DayOfWeek);

		// DATEFIRST changes DATEPART(WEEKDAY); the procedure must not depend on it.
		Assert.AreEqual(0, (await ClaimAsync(Day.AddDays(1).AddHours(1), dateFirst: 1)).Count, "wrong day");
		Assert.AreEqual(1, (await ClaimAsync(Day.AddHours(1), dateFirst: 1)).Count, "matching day");
	}

	[TestMethod]
	public async Task Hourly_RunsOneHourAfterPreviousRunStarted()
	{
		var id = await InsertScheduleAsync("Hourly", TimeSpan.Zero);
		await using (var connection = TestDatabase.OpenScheduler())
			await connection.ExecuteAsync($"UPDATE {Schema}.WorkerSchedules SET LastRunAt = @at WHERE Id = @id",
				new { id, at = Day.AddHours(10).AddMinutes(59) });

		Assert.AreEqual(0, (await ClaimAsync(Day.AddHours(11))).Count, "one minute later is not an hour");
		Assert.AreEqual(1, (await ClaimAsync(Day.AddHours(11).AddMinutes(59))).Count);
	}

	private static async Task<Guid> InsertScheduleAsync(string frequency, TimeSpan startTime, int? dayOfWeek = null)
	{
		await using var connection = TestDatabase.OpenScheduler();
		return await connection.QuerySingleAsync<Guid>($"""
			INSERT INTO {Schema}.WorkerSchedules (WorkerType, Frequency, StartTime, DayOfWeek)
			OUTPUT inserted.Id
			VALUES ('Test', @frequency, @startTime, @dayOfWeek)
			""", new { frequency, startTime, dayOfWeek });
	}

	private static async Task<List<Guid>> ClaimAsync(DateTimeOffset now, int leaseSeconds = 3600, int dateFirst = 7)
	{
		await using var connection = TestDatabase.OpenScheduler();
		var ids = await connection.QueryAsync<Guid>(
			$"SET DATEFIRST {dateFirst}; EXEC {Schema}.ClaimDueSchedules @LeaseSeconds = @leaseSeconds, @Now = @now;",
			new { leaseSeconds, now });
		return ids.AsList();
	}

	private static async Task<T> QueryAsync<T>(Guid id, string column)
	{
		await using var connection = TestDatabase.OpenScheduler();
		return await connection.QuerySingleAsync<T>($"SELECT {column} FROM {Schema}.WorkerSchedules WHERE Id = @id", new { id });
	}
}
