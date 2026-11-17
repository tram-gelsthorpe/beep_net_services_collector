# ServiceBus Architecture - Visual Reference Guide

## Your Current Architecture

```
┌────────────────────────────────────────────────────────────────────┐
│                         YOUR WORKER                                │
│                   WebsiteCollectorWorker                            │
│                 (extends GenericWorkerBase<T>)                      │
├────────────────────────────────────────────────────────────────────┤
│ Responsibilities:                                                   │
│ 1. Poll database every 5 seconds for work items                     │
│ 2. Manage up to 10 concurrent HTTP collection tasks                 │
│ 3. Maintain lease via heartbeat + renewal                           │
│ 4. Orchestrate claim → init → process → complete lifecycle          │
└────────┬─────────────────────────────────────────────────────────┬─┘
		 │                                                           │
		 │                                              ┌────────────┘
		 │                                              │
		 ▼                                              ▼
	┌─────────────────┐                        ┌──────────────┐
	│ LeaseManager    │                        │ WorkProcessor│
	│ (claim/renew)   │                        │ (do work)    │
	│                 │                        │              │
	│ ✅ Calls repo   │                        │ ✅ Calls repo│
	│ ✅ Good retry   │                        │ ✅ Http req  │
	└────────┬────────┘                        └──────┬───────┘
			 │                                        │
			 └────────────┬─────────────────────────┘
						  │
						  ▼
			┌───────────────────────────────┐
			│ WebsiteSelectorsService       │
			│ (your orchestration layer)    │
			│                               │
			│ Responsibilities:             │
			│ • ClaimSelectorsAsync         │
			│ • StartExecutionAsync         │
			│ • RenewLeaseAsync             │
			│ • CompleteExecutionAsync      │
			│   └─ Publishes event via      │
			│     EventAggregator           │
			└───────────┬───────────────────┘
						│
						▼
		┌───────────────────────────────────┐
		│ WebsiteSelectorsRepository        │
		│ (Beep.ServiceBus adapter)         │
		│                                   │
		│ Translates:                       │
		│ Domain API → ServiceBus API       │
		│                                   │
		│ ⚠️ ISSUE: Empty selector crashes  │
		│ ⚠️ ISSUE: Release semantics vague │
		└───────────┬───────────────────────┘
					│
		┌───────────┴────────────┐
		│                        │
		▼                        ▼
	┌──────────────┐     ┌──────────────┐
	│ WorkItem     │     │ Selector     │
	│ Repository   │     │ Management   │
	│ (generic)    │     │ Repository   │
	│              │     │ (domain)     │
	│ • Claim      │     │              │
	│ • Renew      │     │ • GetAsync   │
	│ • Complete   │     │              │
	└──────┬───────┘     └──────┬───────┘
		   │                    │
		   └────────┬───────────┘
					│
					▼
		┌────────────────────────┐
		│   SQL Database         │
		│                        │
		│ ServiceBus (generic): │
		│ ├─ WorkItems table    │
		│ ├─ Lease tokens       │
		│ └─ Status tracking    │
		│                        │
		│ Domain (specific):    │
		│ ├─ WebsiteSelectors   │
		│ └─ Collection config  │
		└────────────────────────┘
```

---

## How Concurrency Works

```
					GenericWorkerBase.ExecuteAsync()

					Every 5 seconds (PollIntervalSeconds)
							  │
							  ▼
					Check running tasks
					└─ Remove completed ✓
					   Add new if capacity
							  │
							  ▼
					int available = MaxConcurrentItems - running.Count
					(Your MaxConcurrentItems = 10)
							  │
							  ▼
					ClaimAsync(available)
					└─ Get min(available, queue size)
					└─ Return claimed items + lease tokens
							  │
							  ▼
					foreach item in claimed:
						Start ProcessItemAsync(item, leaseToken)
						└─ Background task (not awaited)
							  │
							  ▼
					Go back to step 1 (after 5 sec delay)


Each ProcessItemAsync(item, leaseToken):
	┌─────────────────────────────────────┐
	│  [Parallel Background Task]         │
	│                                     │
	│  1. StartAsync (initialize)         │
	│  2. ProcessAsync (main work)        │
	│  3. CompleteAsync (finalize)        │
	│                                     │
	│  PLUS (parallel):                   │
	│  - Heartbeat (renew lease)          │
	│  - Listen for cancellation          │
	└─────────────────────────────────────┘

Timeout: Controlled by item.TimeoutSeconds (your HTTP timeout)
Lease: Renewed every max(10, LeaseSeconds/3) seconds
	   = max(10, 300/3) = 100 seconds
```

---

## State Transitions

```
┌─────────────┐
│   QUEUED    │ Item waiting for claiming
│ (in table)  │
└──────┬──────┘
	   │ LeaseManager.ClaimAsync()
	   │ └─ Mark with worker ID + lease token
	   ▼
┌─────────────┐
│   CLAIMED   │ Assigned to worker, not started yet
│ (leased)    │ Lease expires at: now + LeaseSeconds
└──────┬──────┘
	   │ WorkProcessor.InitializeAsync()
	   │ └─ RecordAttemptAsync("RUNNING")
	   ▼
┌─────────────┐
│   RUNNING   │ Actively processing, heartbeat renewing
│ (in-flight) │ Lease auto-renewed every 100 seconds
└──────┬──────┘
	   │
	   ├─────(success)──→ WorkProcessor.CompleteAsync(..., success=true)
	   │                 ├─ PublishAsync(CollectionCompletedEvent)
	   │                 └─ CompleteAsync(succeeded=true)
	   │                    └─ Remove from queue ✓
	   │
	   └─────(failure)──→ WorkProcessor.CompleteAsync(..., success=false)
						 ├─ PublishAsync(CollectionCompletedEvent)
						 └─ CompleteAsync(succeeded=false)
							├─ If MaxAttempts not reached:
							│  └─ Requeue with backoff
							└─ If MaxAttempts reached:
							   └─ Mark as FAILED ✗
```

---

## Lease Heartbeat Timeline

```
Lease Duration: 300 seconds
Heartbeat Interval: 100 seconds (300 / 3)

T=0s    [CLAIM] ◄────────────────────────► Expiry: T+300s
		Worker: A
		Token: abc123
		Status: CLAIMED

		[INIT] Start processing
		└─ Status: RUNNING

T=100s  [HEARTBEAT] ◄──────────────────────► Renewed
		Old expiry: T+300s
		New expiry: T+400s
		(Lease extended by 300s)

T=200s  [HEARTBEAT] ◄──────────────────────► Renewed
		Old expiry: T+400s
		New expiry: T+500s

T=240s  [COMPLETE] Processing finished
		├─ PublishEvent ✓
		└─ Mark status: COMPLETED
		   (Lease no longer needed)

T=300s  [WOULD EXPIRE] But already complete ✓
```

---

## Error Scenarios

### Scenario A: Network Timeout
```
ProcessAsync()
├─ Validate URL ✓
├─ Create HTTP request
├─ await SendAsync(..., timeout=60s)
└─ After 60s: Timeout fires
   ├─ Request aborted
   └─ Return ProcessingResult { Success=false, "timeout" }
	  └─ CompleteAsync(success=false)
		 └─ Requeue with backoff
```

### Scenario B: Empty Selector (CURRENT BUG)
```
ClaimAsync()
├─ WorkItemRepository.ClaimAsync() ✓ (gets item)
├─ GetWebsiteSelectorAsync(itemId)
└─ selectors.First() ✗ CRASH!
   └─ InvalidOperationException
	  └─ Entire claim batch fails
	  └─ Worker gets nothing
	  └─ Worker idle until next poll
```

### Scenario C: Lease Lost During Processing
```
ProcessAsync() running (at T+200s)
├─ Heartbeat attempts renewal
└─ RenewAsync() returns FALSE
   ├─ Lease collision detected
   ├─ Cancel processing
   └─ CompleteAsync(success=false)
	  └─ Item may be re-claimed by another worker
		 (Potential double-processing!)
```

### Scenario D: Worker Crashes
```
ProcessItemAsync() running
├─ Worker process dies
├─ Lease still has token (in DB)
└─ After LeaseSeconds (300s):
   ├─ Lease expires
   ├─ Another worker can claim it
   └─ Item retried (automatic recovery ✓)
```

---

## Key Contracts

### ILeaseManager<T> - Manage Leases
```csharp
interface ILeaseManager<T>
{
	// Claim items: mark as leased to this worker
	Task<IReadOnlyList<IClaimedItem<T>>> ClaimAsync(
		Guid workerIdent,      // Worker identity
		int maxItems,          // How many to claim
		int leaseSeconds,      // Duration of lease
		CancellationToken ct);

	// Renew lease: extend expiry before it expires
	Task<bool> RenewAsync(
		object item,
		Guid leaseToken,
		int leaseSeconds,
		CancellationToken ct);

	// Release lease: manually cancel (rare)
	Task ReleaseAsync(
		object item,
		Guid leaseToken,
		CancellationToken ct);
}
```

### IWorkProcessor<T> - Do the Work
```csharp
interface IWorkProcessor<T>
{
	// Initialize before processing
	Task InitializeAsync(
		T item,
		Guid leaseToken,
		Guid workerIdent,
		CancellationToken ct);

	// Main processing
	Task<ProcessingResult> ProcessAsync(
		T item,
		Guid leaseToken,
		CancellationToken ct);

	// Finalize after success/failure
	Task CompleteAsync(
		T item,
		Guid leaseToken,
		ProcessingResult result,
		CancellationToken ct);

	int HeartbeatIntervalSeconds { get; }
}
```

### Your Implementation
```csharp
// CollectorLeaseManager : ILeaseManager<ClaimedWebsiteSelector>
public class CollectorLeaseManager : ILeaseManager<ClaimedWebsiteSelector>
{
	private readonly IWebsiteSelectorsService _service;

	public async Task<IReadOnlyList<IClaimedItem<ClaimedWebsiteSelector>>> 
		ClaimAsync(Guid workerId, int maxItems, int leaseSeconds, CancellationToken ct)
	{
		// Delegates to service.ClaimSelectorsAsync()
		// ✅ Correct pattern
	}

	public async Task<bool> RenewAsync(object item, Guid leaseToken, 
		int leaseSeconds, CancellationToken ct)
	{
		// Delegates to service.RenewLeaseAsync()
		// ✅ Correct pattern
	}

	public async Task ReleaseAsync(object item, Guid leaseToken, 
		CancellationToken ct)
	{
		// No-op (should this fail the item?)
		// ⚠️ Semantic unclear
	}
}

// CollectorWorkProcessor : IWorkProcessor<ClaimedWebsiteSelector>
public class CollectorWorkProcessor : IWorkProcessor<ClaimedWebsiteSelector>
{
	private readonly IWebsiteSelectorsService _service;

	public async Task InitializeAsync(T item, Guid leaseToken, 
		Guid workerIdent, CancellationToken ct)
	{
		// Calls service.StartExecutionAsync()
		// ✅ Correct
	}

	public async Task<ProcessingResult> ProcessAsync(T item, 
		Guid leaseToken, CancellationToken ct)
	{
		// Makes HTTP request, extracts data
		// Returns success/failure result
		// ✅ Correct
	}

	public async Task CompleteAsync(T item, Guid leaseToken, 
		ProcessingResult result, CancellationToken ct)
	{
		// Publishes CollectionCompletedEvent
		// Marks item complete in DB
		// ✅ Correct pattern
	}

	public int HeartbeatIntervalSeconds => Math.Max(10, LeaseSeconds / 3);
	// ✅ Good defensive programming
}
```

---

## Performance Characteristics

```
Config: PollIntervalSeconds=5, MaxConcurrentItems=10

Throughput (items processed/minute):
├─ Best case: 10 items × (60/5) = 120 items/min
│  (when processing < 1 second per item)
├─ Realistic: 10 items × (60/240) = 2.5 items/min
│  (when processing 240 seconds per item)
└─ Queue backup: Processing slower than claim rate
   (backlog grows if throughput < arrival rate)

Concurrency:
├─ Max in-flight: 10 (configured)
├─ Each claims: 1-2 database connections
├─ Heartbeat: 1 connection per item every 100s
└─ Total DB connections: ~20-30 (with pool)

Latency (claim → processing start):
├─ Best case: 5 seconds (next poll cycle)
├─ Worst case: 5 seconds (poll interval is fixed)
└─ Average: 2.5 seconds (uniformly distributed)

Event publication latency:
├─ After CompleteAsync
├─ EventBus worker polls next
├─ Webhook delivery: depends on EventBus config
```

---

## Decision Trees

### "Should we increase MaxConcurrentItems?"

```
MaxConcurrentItems = ?

	↓
	Is database connection pool exhausted?
	├─ Yes → NO, fix connection pooling first
	└─ No
		↓
		Is CPU maxed out?
		├─ Yes → NO, adding more won't help
		└─ No
			↓
			Is I/O (network) the bottleneck?
			├─ Yes → Maybe (but check HTTP timeout)
			└─ No
				↓
				Is queue backing up?
				├─ Yes → Try 10→15, measure
				│        If better: OK
				│        If same: bottleneck elsewhere
				└─ No → Leave at 10 (current is good)
```

### "Should we increase PollIntervalSeconds?"

```
PollIntervalSeconds = 5

	↓
	Is database load from polling high?
	├─ Yes → Try 10 (less polling)
	│         If queue doesn't back up: Good
	│         If queue backs up: Too high
	└─ No → Keep at 5 (good responsiveness)

Decision: 5 is fine. Higher reduces responsiveness.
Increased from 5→30 seconds only if DB is overloaded.
```

### "Should we increase LeaseSeconds?"

```
LeaseSeconds = 300

	↓
	What's the longest item processing time?
	├─ < 100 seconds → 300 is fine (3× buffer)
	├─ 100-250 seconds → Consider 300-400
	└─ > 250 seconds → Must increase to P + 60s
					   (e.g., 350 seconds if max is 290)

Rule: LeaseSeconds > max_processing_time + 60
	  Current: 300 > ? + 60
			 ? < 240 seconds

If any item takes > 240s, increase LeaseSeconds.
```

---

## Testing Checklist

- [ ] Can process multiple items concurrently?
- [ ] Does heartbeat prevent timeout?
- [ ] Does timeout abort correctly?
- [ ] Does worker shut down gracefully?
- [ ] Do in-flight items complete?
- [ ] Can lease be renewed?
- [ ] Does expired lease release item?
- [ ] Are events published after complete?
- [ ] Does double-processing not occur?
- [ ] Does empty selector not crash?

---

## Monitoring Dashboard (Prometheus Metrics)

```
website_collector_items_claimed_total
  ├─ label: worker_id
  └─ Claim rate (items/sec)

website_collector_items_completed_total
  ├─ label: success=true|false
  └─ Completion rate

website_collector_processing_duration_seconds
  ├─ histogram: p50, p95, p99
  └─ How long does processing take?

website_collector_lease_renewals_total
  ├─ label: status=success|failed
  └─ Heartbeat health

website_collector_active_items
  ├─ Gauge: currently processing
  └─ Should be < MaxConcurrentItems (10)

website_collector_queue_depth
  ├─ Gauge: items waiting in DB
  └─ Alert if growing unbounded

eventbus_publications_total
  ├─ How many events published
  └─ Should match completions
```

---

## One-Liner Summary

**Your ServiceBus integration is a well-designed lease-based work queue with concurrent processing, automatic retry, and event-driven notifications. Fix empty selector handling and document release semantics, then ship with confidence.**
