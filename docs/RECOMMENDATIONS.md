# ServiceBus Integration - Recommendations & Action Items

## Critical Issues (Fix Immediately)

### 1. ❌ Empty Selector Collection Not Handled

**Location**: `WebsiteSelectorsRepository.ClaimAsync()` line ~37

**Current Code**:
```csharp
foreach (var item in items)
{
	var selectors = await _selectorManagementRepository.GetWebsiteSelectorAsync(item.DomainId, ct);
	var selector = selectors.First();  // ⚠️ THROWS if empty

	result.Add(new ClaimedWebsiteSelector(...));
}
```

**Problem**: If `GetWebsiteSelectorAsync` returns an empty collection, `.First()` throws `InvalidOperationException`, causing:
- Entire batch to fail
- No items returned to worker
- Worker remains idle until next poll
- Log filled with unhandled exceptions

**Impact**: **HIGH** - Can stop worker processing entirely

**Fix**:
```csharp
foreach (var item in items)
{
	try
	{
		var selectors = await _selectorManagementRepository.GetWebsiteSelectorAsync(item.DomainId, ct);

		if (selectors.Count == 0)
		{
			_logger.LogWarning(
				"No selector found for work item {WorkItemId}. Marking as failed.",
				item.DomainId);

			// Option A: Mark as failed
			await _workItemRepository.CompleteAsync(
				workItemId: item.DomainId,
				leaseToken: item.LeaseToken,
				succeeded: false,
				error: "Selector not found",
				resultData: null,
				backoffStrategy: "exponential",
				cancellationToken: ct);
			continue;

			// OR Option B: Skip this item (risky - may never be processed)
			// continue;
		}

		var selector = selectors.First();

		result.Add(new ClaimedWebsiteSelector(
			WebsiteSelectorsId: item.DomainId,
			WebsitesId: selector.WebsitesId,
			CollectionUrl: selector.CollectionUrl,
			CollectionType: selector.CollectionType,
			ContentSelector: selector.ContentSelector,
			TableSelector: selector.TableSelector,
			TimeoutSeconds: selector.TimeoutSeconds,
			MaxAttempts: item.MaxAttempts,
			AttemptCount: item.AttemptNumber - 1,
			LeaseToken: item.LeaseToken));
	}
	catch (Exception ex)
	{
		// Additional safety net
		_logger.LogError(ex,
			"Error claiming selector for work item {WorkItemId}. Skipping.",
			item.DomainId);

		// Could also complete as failed here
		continue;
	}
}

return result.AsReadOnly();
```

**Decision**: Choose Option A (fail the item) unless you have retention policy requirements.

---

## Important Design Clarifications

### 2. ⚠️ Lease Release Semantics

**Location**: `CollectorLeaseManager.ReleaseAsync()`

**Current Code**:
```csharp
public async Task ReleaseAsync(object item, Guid leaseToken, CancellationToken ct)
{
	if (item is not ClaimedWebsiteSelector selector)
		return;

	// Release is handled via CompleteExecutionAsync in the processor
	await Task.CompletedTask;
}
```

**Issue**: This is a no-op. The GenericWorkerBase may call `ReleaseAsync` if an exception occurs before `CompleteAsync` is called.

**Analysis**:
- ✅ If `CompleteAsync` **always** runs (even on exception), this is safe
- ❌ If `CompleteAsync` can be skipped, items will be stuck in CLAIMED state

**Recommendation**:

```csharp
public async Task ReleaseAsync(object item, Guid leaseToken, CancellationToken ct)
{
	if (item is not ClaimedWebsiteSelector selector)
		return;

	_logger.LogWarning(
		"Releasing lease for selector {SelectorsId} (unexpected path). " +
		"This should only happen if processing was aborted.",
		selector.WebsiteSelectorsId);

	// Optional: Mark as failed with generic error message
	try
	{
		await _collectionService.CompleteExecutionAsync(
			websiteSelectorsId: selector.WebsiteSelectorsId,
			leaseToken: leaseToken,
			success: false,
			httpStatus: null,
			error: "Processing aborted - lease released without completion",
			hash: null,
			raw: null,
			extracted: null,
			ct: ct);
	}
	catch (Exception ex)
	{
		_logger.LogError(ex,
			"Failed to complete selector {SelectorsId} on lease release",
			selector.WebsiteSelectorsId);
		// Log but don't throw - this is cleanup
	}
}
```

**Or, document the contract**:
```csharp
/// <summary>
/// Release is a no-op because CompleteAsync is guaranteed to be called
/// for all code paths via GenericWorkerBase.ProcessItemAsync finally block.
/// If this method is called, logs a warning for diagnostic purposes.
/// </summary>
public async Task ReleaseAsync(object item, Guid leaseToken, CancellationToken ct)
{
	if (item is not ClaimedWebsiteSelector selector)
		return;

	_logger.LogWarning(
		"ReleaseAsync called for selector {SelectorsId}. " +
		"This indicates an unexpected code path. " +
		"Verify that CompleteAsync will still run.",
		selector.WebsiteSelectorsId);

	await Task.CompletedTask;
}
```

**Your choice**: Implementation depends on your failure recovery strategy.

---

### 3. ⚠️ Competitor Claiming (Double-Processing Risk)

**Scenario**: 
```
Worker A claims item X (lease expires at T+300)
Worker A processes (slow, takes 250 seconds)
At T+250: Heartbeat renewal FAILS (DB unavailable for 10 seconds)
Worker A logs: "Lease renewal rejected"
Worker A continues processing anyway

Meanwhile...

Worker B's next poll (T+260) 
Worker B calls ClaimAsync
Item X lease appears expired (from B's perspective)
Worker B claims item X
Worker B processes item X

Result: Item X processed by BOTH workers!
```

**Controls You Have**:

1. **Idempotency** - Is processing safe if called twice?

   Your `CollectorWorkProcessor.ProcessAsync` makes HTTP requests and extracts data.
   If two workers do this simultaneously:
   - Same HTTP request sent twice ❌
   - Data may be different (if website changed)
   - Events published twice

   **Status**: ⚠️ **Not idempotent** - needs protection

2. **Lease Token Validation** - Does DB reject completion with wrong token?

   Your `WorkItemRepository.CompleteAsync` should verify the `leaseToken` matches.

   **Recommendation**: Verify `WorkItemRepository.CompleteAsync` implementation owns this.

3. **Atomic Claim+Lock** - Can claims guarantee exclusive access?

   Your `WorkItemRepository.ClaimAsync` should use database EXCLUSIVE locks.

   **Recommendation**: Verify the Beep.ServiceBus uses it correctly.

**Action**:

```csharp
// Add diagnostics to detect double-processing
public sealed class CollectorWorkProcessor : IWorkProcessor<ClaimedWebsiteSelector>
{
	private static readonly Dictionary<Guid, DateTimeOffset> _processingTracker = new();

	public async Task<ProcessingResult> ProcessAsync(
		ClaimedWebsiteSelector item, 
		Guid leaseToken, 
		CancellationToken ct)
	{
		// Check for double-processing
		lock (_processingTracker)
		{
			if (_processingTracker.TryGetValue(item.WebsiteSelectorsId, out var lastTime))
			{
				if ((DateTimeOffset.UtcNow - lastTime).TotalSeconds < item.TimeoutSeconds + 60)
				{
					_logger.LogError(
						"ALERT: Double-processing detected for selector {SelectorsId}. " +
						"Last processed {TimeAgo} ago. Lease collision likely.",
						item.WebsiteSelectorsId,
						(DateTimeOffset.UtcNow - lastTime).TotalSeconds);

					return new ProcessingResult
					{
						Success = false,
						ErrorMessage = "Double-processing detected (lease collision)"
					};
				}
			}
			_processingTracker[item.WebsiteSelectorsId] = DateTimeOffset.UtcNow;
		}

		try
		{
			// ... existing processing logic
		}
		finally
		{
			lock (_processingTracker)
			{
				_processingTracker.Remove(item.WebsiteSelectorsId);
			}
		}
	}
}
```

---

## Performance Recommendations

### 4. Connection Reuse

**Current Pattern**:
```csharp
await _httpClientFactory.CreateClient("collector").SendAsync(...)
```

✅ **Good** - Using `IHttpClientFactory` ensures connection pooling.

**Verify** `HttpClientConfiguration.cs`:
```csharp
services.AddHttpClient("collector", client =>
{
	client.DefaultRequestHeaders.Add("User-Agent", "WebsiteCollector/1.0");
	// Ensure timeouts are reasonable
	client.Timeout = TimeSpan.FromSeconds(60);
})
.SetHandlerLifetime(TimeSpan.FromMinutes(2));  // ✅ Reuse connections
```

---

### 5. Database Connection Pooling

**Current Pattern**:
```csharp
services.AddScoped<IWebsiteSelectorsRepository, WebsiteSelectorsRepository>(
	p => new WebsiteSelectorsRepository(
		configuration,
		ServiceBusDatabaseName,
		SelectorDatabaseName,
		p.GetRequiredService<ILoggerFactory>()));
```

⚠️ **Issue**: Repository creates `WorkItemRepository` internally each time.

**Check**: Does `WorkItemRepository` reuse a shared `DbContextFactory`?

If not, each claim/renew/complete creates a new `SqlConnection`, causing:
- Connection pool exhaustion at high concurrency
- Slower queries

**Recommendation**:

```csharp
// In DependencyInjection.cs
services.AddScoped<IWorkItemRepository>(p =>
	new WorkItemRepository(
		configuration.GetConnectionString(ServiceBusDatabaseName) 
			?? throw new InvalidOperationException("Missing ServiceBus connection string"),
		p.GetRequiredService<ILoggerFactory>().CreateLogger<WorkItemRepository>()));

services.AddScoped<ISelectorManagementRepository>(p =>
	new SelectorManagementRepository(
		configuration.GetConnectionString(SelectorDatabaseName)
			?? throw new InvalidOperationException("Missing Selector connection string"),
		p.GetRequiredService<ILoggerFactory>().CreateLogger<SelectorManagementRepository>()));

services.AddScoped<IWebsiteSelectorsRepository>(p =>
	new WebsiteSelectorsRepository(
		p.GetRequiredService<IWorkItemRepository>(),
		p.GetRequiredService<ISelectorManagementRepository>()));
```

Then in `WebsiteSelectorsRepository`:
```csharp
public sealed class WebsiteSelectorsRepository : IWebsiteSelectorsRepository
{
	private readonly IWorkItemRepository _workItemRepository;
	private readonly ISelectorManagementRepository _selectorManagementRepository;

	public WebsiteSelectorsRepository(
		IWorkItemRepository workItemRepository,
		ISelectorManagementRepository selectorManagementRepository)
	{
		_workItemRepository = workItemRepository ?? throw new ArgumentNullException(nameof(workItemRepository));
		_selectorManagementRepository = selectorManagementRepository ?? throw new ArgumentNullException(nameof(selectorManagementRepository));
	}

	// Rest of implementation unchanged
}
```

---

### 6. Logging Verbosity

**Warning**: Your current logging is sparse. Add structured logging:

```csharp
_logger.LogInformation(
	"Started processing selector {SelectorsId} from URL {Url} " +
	"with timeout {TimeoutSeconds}s",
	item.WebsiteSelectorsId,
	item.CollectionUrl,
	item.TimeoutSeconds);

_logger.LogDebug(
	"HTTP response {StatusCode}, size {ResponseBytes} bytes",
	statusCode,
	ms.Length);

_logger.LogInformation(
	"Completed selector {SelectorsId} in {ElapsedMs}ms with {ExtractedCount} items",
	item.WebsiteSelectorsId,
	sw.ElapsedMilliseconds,
	extractedCount);
```

This enables:
- Performance monitoring
- Tracing slow requests
- Debugging failures at scale

---

## Testing Recommendations

### 7. Unit Test Resilience

Create tests for failure scenarios:

```csharp
[Fact]
public async Task ClaimAsync_WithMissingSelectorData_ShouldFailGracefully()
{
	// Arrange
	var workItem = new ClaimedWorkItem { DomainId = Guid.NewGuid() };
	var mockWorkItemRepo = new Mock<IWorkItemRepository>();
	mockWorkItemRepo
		.Setup(x => x.ClaimAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
		.ReturnsAsync(new[] { workItem });

	var mockSelectorRepo = new Mock<ISelectorManagementRepository>();
	mockSelectorRepo
		.Setup(x => x.GetWebsiteSelectorAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
		.ReturnsAsync(new List<WebsiteSelector>()); // ⚠️ Empty!

	var repository = new WebsiteSelectorsRepository(mockWorkItemRepo.Object, mockSelectorRepo.Object);

	// Act & Assert
	var result = await repository.ClaimAsync(Guid.NewGuid(), 1, 300, CancellationToken.None);
	Assert.True(result.Count == 0 || result.First() != null); // Should handle gracefully
}
```

---

### 8. Integration Test Lease Renewal

```csharp
[Fact]
public async Task HeartbeatRenewal_ShouldPreventTimeoutDuringLongProcessing()
{
	// Arrange
	var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

	// Act
	var leaseManager = new CollectorLeaseManager(_service);
	var items = await leaseManager.ClaimAsync(
		Guid.NewGuid(), 1, 5, cts.Token); // 5 second lease

	// Simulate long processing (8 seconds)
	var renewalCount = 0;
	for (int i = 0; i < 8; i++)
	{
		if (await leaseManager.RenewAsync(items[0].Item, items[0].LeaseToken, 5, cts.Token))
		{
			renewalCount++;
		}
		await Task.Delay(1000, cts.Token);
	}

	// Assert
	Assert.True(renewalCount >= 1, "Lease should renew at least once during processing");
}
```

---

## Monitoring & Alerting Checklist

### 9. Key Metrics to Track

| Metric | Type | Alert Threshold | Why |
|--------|------|-----------------|-----|
| Items claimed per minute | Counter | < 1 for 5 min | Worker may be stuck |
| Lease renewal failures | Counter | > 5/min | DB connectivity issue |
| Double-processing detections | Counter | > 0 | Lease collision bug |
| Processing time (p95) | Histogram | > 2× avg | Slowdown detected |
| Items in queue | Gauge | > 1000 | Backlog building |
| Event publication lag | Histogram | > 5s | EventBus bottleneck |

### 10. Logging Checklist

Add logs at these critical points:

- [ ] ClaimAsync entry/exit (log count claimed)
- [ ] ClaimAsync error handling (missing selector data)
- [ ] InitializeAsync entry (log selector details)
- [ ] ProcessAsync start/end (with elapsed time)
- [ ] HeartbeatAsync renewal success/failure
- [ ] CompleteAsync entry/exit (log success/failure)
- [ ] Event publication (log subscriber count informed)
- [ ] Worker startup/shutdown (log configuration)

---

## Configuration Review Checklist

Verify `appsettings.json`:

```json
{
  "WebsiteCollectorOptions": {
	"PollIntervalSeconds": 5,        // ⚠️ If < 1: hammers DB
	"MaxConcurrentItems": 10,       // ⚠️ If > 50: connection pool exhaustion
	"LeaseSeconds": 300,            // ⚠️ Must be > max processing time
	"MaxResponseBytes": 10485760    // ⚠️ 10MB default - adjust for your content
  },
  "Logging": {
	"LogLevel": {
	  "Default": "Information",     // ⚠️ Consider "Debug" for troubleshooting
	  "WebsiteCollector.Worker": "Information"
	}
  }
}
```

**Rule of thumb**:
- PollIntervalSeconds: 5-30 seconds
- MaxConcurrentItems: Number of CPUs × 1-2
- LeaseSeconds: (max HTTP timeout + 60 seconds)
- MaxResponseBytes: Match expected content size

---

## Summary of Fixes

| Priority | Issue | Fix |
|----------|-------|-----|
| **P1** | Empty selector → exception | Add try-catch + fail gracefully |
| **P2** | Lease release semantics unclear | Add logging or complete-on-release |
| **P2** | Double-processing possible | Add lease token validation + monitoring |
| **P3** | Connection pooling inefficient | Inject repositories via DI |
| **P3** | Logging insufficient | Add structured logging at key points |
| **P4** | Missing tests for failures | Write unit + integration tests |

---

**Next Steps**:
1. ✅ Fix empty selector handling (P1)
2. ✅ Add structured logging
3. ✅ Create test suite for failure paths
4. ✅ Document lease semantics in code comments
5. ✅ Review Beep.ServiceBus `WorkItemRepository.CompleteAsync` to confirm lease token validation
