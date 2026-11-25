using Beep.Scheduler;
using Beep.ServiceBus;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace WebsiteCollector.Worker.Tests.Integration;

/// <summary>
/// When a schedule is due (StartTime, once per period, weekday, hourly), one row per worker type,
/// and running schedules through the ServiceBus: one run across instances, retries, and the
/// last failed attempt finishing the run.
/// Due-time tests call GetDueSchedules with a fixed @Now so they do not depend on the time of day.
/// </summary>
[TestClass]
[DoNotParallelize] // schedule runs are ServiceBus work items (shared ServiceBus database)
public sealed class SchedulerIntegrationTests
{
	private const string Schema = TestDatabase.SchedulerSchema;
	private const string HandlerWorkerType = "Test";

	/// <summary>A fixed UTC day, recent enough that run durations computed against the real clock stay small.</summary>
	private static readonly DateTimeOffset Day = new(DateTime.UtcNow.Date.AddDays(-2), TimeSpan.Zero);

	private readonly SchedulerWorkerOptions _options = new() { PollIntervalSeconds = 1, LeaseSeconds = 60 };
	private SchedulerRepository _repository = null!;
	private WorkItemRepository _workItems = null!;

	[TestInitialize]
	public async Task TestInitializeAsync()
	{
		await TestDatabase.ClearDataAsync();
		await TestDatabase.ClearSchedulerDataAsync();
		_repository = new SchedulerRepository(TestDatabase.SchedulerConnectionString, Schema, NullLogger<SchedulerRepository>.Instance);
		_workItems = new WorkItemRepository(TestDatabase.ServiceBusConnectionString, TestDatabase.ServiceBusSchema, NullLogger<WorkItemRepository>.Instance);
	}

	// ---------------------------------------------------------------- when a schedule is due

	[TestMethod]
	public async Task CreateOrUpdateSchedule_CalledConcurrently_KeepsOneRowPerWorkerType()
	{
		await Task.WhenAll(Enumerable.Range(0, 20).Select(i => _repository.CreateOrUpdateScheduleAsync(
			"Collector", $"run {i}", "Daily", new TimeOnly(2, 0), isActive: true, CancellationToken.None)));

		Assert.AreEqual(1, await CountAsync("SELECT COUNT(*) FROM {0}.WorkerSchedules WHERE WorkerType = 'Collector'"));
	}

	[TestMethod]
	public async Task Daily_IsDueAtOrAfterStartTime_UntilTheRunIsRecorded()
	{
		var id = await InsertScheduleAsync("Daily", new TimeSpan(2, 0, 0));

		Assert.AreEqual(0, (await DueAsync(Day.AddHours(1).AddMinutes(59))).Count, "before StartTime");
		CollectionAssert.AreEqual(new[] { id }, (await DueAsync(Day.AddHours(2))).ToArray(), "at StartTime");
		Assert.AreEqual(1, (await DueAsync(Day.AddHours(2).AddMinutes(1))).Count, "still due: reading does not claim");

		await _repository.LogExecutionAsync(id, "Success", 0, null, "Test", Day.AddHours(2), CancellationToken.None);

		Assert.AreEqual(Day.AddHours(2), await QueryAsync<DateTimeOffset>(id, "LastRunAt"), "LastRunAt is the run start");
		Assert.AreEqual(0, (await DueAsync(Day.AddHours(23))).Count, "already ran today");
		Assert.AreEqual(0, (await DueAsync(Day.AddDays(1).AddHours(1))).Count, "next day, before StartTime");
		Assert.AreEqual(1, (await DueAsync(Day.AddDays(1).AddHours(2))).Count, "next day, at StartTime");
	}

	[TestMethod]
	public async Task Daily_WhenWorkerWasDownAtStartTime_CatchesUpLaterThatDay()
	{
		await InsertScheduleAsync("Daily", new TimeSpan(2, 0, 0));

		Assert.AreEqual(1, (await DueAsync(Day.AddHours(15))).Count);
	}

	[TestMethod]
	public async Task LogExecution_ForAttemptThatWillBeRetried_DoesNotFinishTheRun()
	{
		var id = await InsertScheduleAsync("Daily", TimeSpan.Zero);

		await _repository.LogExecutionAsync(id, "Retrying", 0, "boom", "Test", Day.AddHours(2), CancellationToken.None, completesRun: false);

		Assert.IsNull(await QueryAsync<DateTimeOffset?>(id, "LastRunAt"));
		Assert.AreEqual(1, (await DueAsync(Day.AddHours(3))).Count);
	}

	[TestMethod]
	public async Task Weekly_MatchesDayOfWeek_RegardlessOfDateFirst()
	{
		await InsertScheduleAsync("Weekly", TimeSpan.Zero, dayOfWeek: (int)Day.DayOfWeek);

		// DATEFIRST changes DATEPART(WEEKDAY); the procedure must not depend on it.
		Assert.AreEqual(0, (await DueAsync(Day.AddDays(1).AddHours(1), dateFirst: 1)).Count, "wrong day");
		Assert.AreEqual(1, (await DueAsync(Day.AddHours(1), dateFirst: 1)).Count, "matching day");
	}

	[TestMethod]
	public async Task Hourly_IsDueOneHourAfterPreviousRunStarted()
	{
		var id = await InsertScheduleAsync("Hourly", TimeSpan.Zero);
		await using (var connection = TestDatabase.OpenScheduler())
			await connection.ExecuteAsync($"UPDATE {Schema}.WorkerSchedules SET LastRunAt = @at WHERE Id = @id",
				new { id, at = Day.AddHours(10).AddMinutes(59) });

		Assert.AreEqual(0, (await DueAsync(Day.AddHours(11))).Count, "one minute later is not an hour");
		Assert.AreEqual(1, (await DueAsync(Day.AddHours(11).AddMinutes(59))).Count);
	}

	// ---------------------------------------------------------------- time zones

	private const string Bangkok = "SE Asia Standard Time"; // UTC+7, no daylight saving

	[TestMethod]
	public async Task Daily_InATimeZone_StartsAtLocalStartTime_OncePerLocalDay()
	{
		// 01:00 in UTC+7 is 18:00 UTC the day before.
		var id = await InsertScheduleAsync("Daily", new TimeSpan(1, 0, 0), timeZone: Bangkok);
		await SetLastRunAsync(id, Day); // 07:00 local on Day

		Assert.AreEqual(0, (await DueAsync(Day.AddHours(17).AddMinutes(59))).Count, "00:59 local on Day+1");
		Assert.AreEqual(1, (await DueAsync(Day.AddHours(18))).Count, "01:00 local on Day+1");

		await SetLastRunAsync(id, Day.AddHours(18));

		// Day+1 16:59 UTC is 23:59 local, still the local day of the last run (by UTC it is a new day).
		Assert.AreEqual(0, (await DueAsync(Day.AddDays(1).AddHours(16).AddMinutes(59))).Count, "already ran this local day");
		Assert.AreEqual(1, (await DueAsync(Day.AddDays(1).AddHours(18))).Count, "01:00 local on Day+2");
	}

	[TestMethod]
	public async Task Weekly_InATimeZone_MatchesTheLocalDayOfWeek()
	{
		var localDay = Day.AddDays(1); // 17:30 UTC on Day is 00:30 local on Day+1
		await InsertScheduleAsync("Weekly", TimeSpan.Zero, dayOfWeek: (int)localDay.DayOfWeek, timeZone: Bangkok);

		Assert.AreEqual(1, (await DueAsync(Day.AddHours(17).AddMinutes(30))).Count, "local day matches");
		Assert.AreEqual(0, (await DueAsync(Day.AddHours(16).AddMinutes(30))).Count, "23:30 local on Day: wrong day");
	}

	[TestMethod]
	public async Task Daily_InATimeZoneWithDaylightSaving_FollowsTheLocalClock()
	{
		var id = await InsertScheduleAsync("Daily", new TimeSpan(1, 0, 0), timeZone: "Eastern Standard Time");

		// Winter: 01:00 EST = 06:00 UTC.
		var winter = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
		Assert.AreEqual(0, (await DueAsync(winter.AddHours(5).AddMinutes(59))).Count, "00:59 EST");
		Assert.AreEqual(1, (await DueAsync(winter.AddHours(6))).Count, "01:00 EST");

		// Summer: 01:00 EDT = 05:00 UTC.
		await SetLastRunAsync(id, new DateTimeOffset(2026, 6, 30, 5, 0, 0, TimeSpan.Zero));
		var summer = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
		Assert.AreEqual(0, (await DueAsync(summer.AddHours(4).AddMinutes(59))).Count, "00:59 EDT");
		Assert.AreEqual(1, (await DueAsync(summer.AddHours(5))).Count, "01:00 EDT");
	}

	[TestMethod]
	public async Task CreateOrUpdateSchedule_StoresTimeZoneAndDays()
	{
		await _repository.CreateOrUpdateScheduleAsync("Collector", "weekly", "Weekly", new TimeOnly(1, 0), isActive: true,
			CancellationToken.None, timeZone: "Asia/Bangkok", dayOfWeek: 3);

		var schedule = await _repository.GetScheduleByWorkerTypeAsync("Collector", CancellationToken.None);
		Assert.IsNotNull(schedule);
		Assert.AreEqual(Bangkok, schedule.TimeZone, "IANA id stored as its Windows id");
		Assert.AreEqual(3, schedule.DayOfWeek);

		await _repository.CreateOrUpdateScheduleAsync("Collector", "daily", "Daily", new TimeOnly(1, 0), isActive: true,
			CancellationToken.None);

		schedule = await _repository.GetScheduleByWorkerTypeAsync("Collector", CancellationToken.None);
		Assert.AreEqual(ScheduleTimeZone.Resolve(null), schedule!.TimeZone, "no time zone: this machine's");
		Assert.IsNull(schedule.DayOfWeek, "update clears days that no longer apply");
	}

	[TestMethod]
	public async Task CreateOrUpdateSchedule_WithUnknownTimeZone_Throws()
	{
		await Assert.ThrowsAsync<ArgumentException>(() => _repository.CreateOrUpdateScheduleAsync(
			"Collector", "bad", "Daily", new TimeOnly(1, 0), isActive: true, CancellationToken.None, timeZone: "Mars/Olympus"));

		Assert.AreEqual(0, await CountAsync("SELECT COUNT(*) FROM {0}.WorkerSchedules"));
	}

	// ---------------------------------------------------------------- running through the ServiceBus

	[TestMethod]
	public async Task TwoInstances_RunADueScheduleOnce()
	{
		var id = await InsertScheduleAsync("Daily", TimeSpan.Zero); // due now on the real clock
		var handler = new TestHandler { Delay = TimeSpan.FromSeconds(1.5) };
		using var provider = BuildProvider(handler);

		var services = Enumerable.Range(0, 2).Select(_ => new SchedulerService(
			_repository, _workItems, _options, NullLogger<SchedulerService>.Instance)).ToList();
		var workers = Enumerable.Range(0, 2).Select(_ => new SchedulerWorker(
			_workItems, CreateProcessor(provider), _options, NullLogger<SchedulerWorker>.Instance)).ToList();

		var hosted = services.Cast<Microsoft.Extensions.Hosting.IHostedService>().Concat(workers).ToList();
		foreach (var service in hosted)
			await service.StartAsync(CancellationToken.None);
		try
		{
			await WaitUntilAsync(async () => await QueryAsync<DateTimeOffset?>(id, "LastRunAt") is not null, TimeSpan.FromSeconds(30));
			await Task.Delay(TimeSpan.FromSeconds(2)); // give a duplicate run the chance to show up
		}
		finally
		{
			foreach (var service in hosted)
				await service.StopAsync(CancellationToken.None);
		}

		Assert.AreEqual(1, handler.Calls, "handler ran once");
		Assert.AreEqual(1, await CountAsync("SELECT COUNT(*) FROM {0}.ScheduleExecutionLogs WHERE Status = 'Success'"));
		Assert.AreEqual(0, (await _repository.GetDueSchedulesAsync(CancellationToken.None)).Count);
	}

	[TestMethod]
	public async Task FailedRun_IsRetriedByServiceBus_AndSucceeds()
	{
		var id = await InsertScheduleAsync("Daily", TimeSpan.Zero);
		var handler = new TestHandler { FailTimes = 1 };
		using var provider = BuildProvider(handler);
		var processor = CreateProcessor(provider);

		Assert.AreEqual(1, await NewSchedulerService().EnqueueDueSchedulesAsync(CancellationToken.None));
		var workItemId = await ProcessOneAsync(processor);
		Assert.AreEqual("FAILED", await QueryWorkItemStatusAsync(workItemId), "retry scheduled");
		Assert.IsNull(await QueryAsync<DateTimeOffset?>(id, "LastRunAt"), "run not finished");
		Assert.AreEqual(0, await NewSchedulerService().EnqueueDueSchedulesAsync(CancellationToken.None), "retry already queued");

		await MakeDueAsync(workItemId);
		await ProcessOneAsync(processor);

		Assert.AreEqual(2, handler.Calls);
		Assert.AreEqual("SUCCEEDED", await QueryWorkItemStatusAsync(workItemId));
		CollectionAssert.AreEqual(new[] { "Retrying", "Success" },
			(await QueryLogStatusesAsync()).ToArray());
		Assert.IsNotNull(await QueryAsync<DateTimeOffset?>(id, "LastRunAt"));
	}

	[TestMethod]
	public async Task FailedLastAttempt_FinishesTheRun_SoItIsNotQueuedAgainUntilNextPeriod()
	{
		var id = await InsertScheduleAsync("Daily", TimeSpan.Zero);
		var handler = new TestHandler { FailTimes = int.MaxValue };
		using var provider = BuildProvider(handler);
		_options.MaxAttempts = 1;

		await NewSchedulerService().EnqueueDueSchedulesAsync(CancellationToken.None);
		var workItemId = await ProcessOneAsync(CreateProcessor(provider));

		Assert.AreEqual("ABANDONED", await QueryWorkItemStatusAsync(workItemId));
		CollectionAssert.AreEqual(new[] { "Failed" }, (await QueryLogStatusesAsync()).ToArray());
		Assert.IsNotNull(await QueryAsync<DateTimeOffset?>(id, "LastRunAt"));
		Assert.AreEqual(0, await NewSchedulerService().EnqueueDueSchedulesAsync(CancellationToken.None));
	}

	[TestMethod]
	public async Task LateDuplicateWorkItem_AfterTheRun_IsSkipped()
	{
		var id = await InsertScheduleAsync("Daily", TimeSpan.Zero);
		var handler = new TestHandler();
		using var provider = BuildProvider(handler);
		var processor = CreateProcessor(provider);

		await NewSchedulerService().EnqueueDueSchedulesAsync(CancellationToken.None);
		await ProcessOneAsync(processor);

		// Another instance read the schedule as due just before the run was recorded, and enqueues now.
		await _workItems.EnqueueBatchAsync(_options.WorkerType, SchedulerWorkerOptions.ScheduleDomainType, [new WorkItemRequest(id)]);
		var duplicate = await ProcessOneAsync(processor);

		Assert.AreEqual(1, handler.Calls);
		Assert.AreEqual("SUCCEEDED", await QueryWorkItemStatusAsync(duplicate));
	}

	// ---------------------------------------------------------------- helpers

	private SchedulerService NewSchedulerService() =>
		new(_repository, _workItems, _options, NullLogger<SchedulerService>.Instance);

	private SchedulerWorkProcessor CreateProcessor(ServiceProvider provider) => new(
		_repository, _workItems, provider.GetRequiredService<IServiceScopeFactory>(), _options,
		NullLogger<SchedulerWorkProcessor>.Instance);

	private static ServiceProvider BuildProvider(TestHandler handler) =>
		new ServiceCollection().AddScoped<IScheduleHandler>(_ => handler).BuildServiceProvider();

	/// <summary>Claim one schedule work item through the ServiceBus and run it like SchedulerWorker does.</summary>
	private async Task<Guid> ProcessOneAsync(SchedulerWorkProcessor processor)
	{
		var lease = new WorkItemLeaseManager(_workItems, _options.WorkerType);
		var claimed = (await lease.ClaimAsync(Guid.NewGuid(), 1, _options.LeaseSeconds, CancellationToken.None)).Single();
		var result = await processor.ProcessAsync(claimed.Item, claimed.LeaseToken, CancellationToken.None);
		await processor.CompleteAsync(claimed.Item, claimed.LeaseToken, result, CancellationToken.None);
		return claimed.Item.WorkItemId;
	}

	private static async Task<Guid> InsertScheduleAsync(string frequency, TimeSpan startTime, int? dayOfWeek = null, string timeZone = "UTC")
	{
		await using var connection = TestDatabase.OpenScheduler();
		return await connection.QuerySingleAsync<Guid>($"""
			INSERT INTO {Schema}.WorkerSchedules (WorkerType, Frequency, StartTime, DayOfWeek, TimeZone)
			OUTPUT inserted.Id
			VALUES (@worker, @frequency, @startTime, @dayOfWeek, @timeZone)
			""", new { worker = HandlerWorkerType, frequency, startTime, dayOfWeek, timeZone });
	}

	private static async Task SetLastRunAsync(Guid id, DateTimeOffset at)
	{
		await using var connection = TestDatabase.OpenScheduler();
		await connection.ExecuteAsync($"UPDATE {Schema}.WorkerSchedules SET LastRunAt = @at WHERE Id = @id", new { id, at });
	}

	private static async Task<List<Guid>> DueAsync(DateTimeOffset now, int dateFirst = 7)
	{
		await using var connection = TestDatabase.OpenScheduler();
		var ids = await connection.QueryAsync<Guid>(
			$"SET DATEFIRST {dateFirst}; EXEC {Schema}.GetDueSchedules @Now = @now;", new { now });
		return ids.AsList();
	}

	private static async Task<T> QueryAsync<T>(Guid id, string column)
	{
		await using var connection = TestDatabase.OpenScheduler();
		return await connection.QuerySingleAsync<T>($"SELECT {column} FROM {Schema}.WorkerSchedules WHERE Id = @id", new { id });
	}

	private static async Task<int> CountAsync(string sqlWithSchemaPlaceholder)
	{
		await using var connection = TestDatabase.OpenScheduler();
		return await connection.QuerySingleAsync<int>(string.Format(sqlWithSchemaPlaceholder, Schema));
	}

	/// <summary>Logged statuses, sorted by name: attempts run milliseconds apart, so FinishedAt can tie.</summary>
	private static async Task<List<string>> QueryLogStatusesAsync()
	{
		await using var connection = TestDatabase.OpenScheduler();
		return (await connection.QueryAsync<string>($"SELECT Status FROM {Schema}.ScheduleExecutionLogs ORDER BY Status")).AsList();
	}

	private static async Task<string> QueryWorkItemStatusAsync(Guid workItemId)
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

	private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (!await condition())
		{
			if (DateTime.UtcNow > deadline)
				Assert.Fail("Timed out waiting for the schedule to run");
			await Task.Delay(200);
		}
	}

	private sealed class TestHandler : IScheduleHandler
	{
		private int _calls;

		public string WorkerType => HandlerWorkerType;
		public Guid WorkerId => Guid.Empty;
		public int Calls => _calls;
		public TimeSpan Delay { get; init; }
		public int FailTimes { get; init; }

		public async Task<ScheduleHandlerResult> RegisterItemsAsync(WorkerSchedule schedule, CancellationToken cancellationToken)
		{
			var call = Interlocked.Increment(ref _calls);
			if (Delay > TimeSpan.Zero)
				await Task.Delay(Delay, cancellationToken);
			return call <= FailTimes
				? new ScheduleHandlerResult { Success = false, Error = $"failure {call}" }
				: new ScheduleHandlerResult { ItemsRegistered = 1 };
		}
	}
}
