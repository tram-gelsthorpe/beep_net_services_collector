# ServiceBus Data Flow & Sequence Diagram

## 1. Complete Processing Lifecycle

### Claim → Process → Complete Cycle

```
					WebsiteCollectorWorker
				   (GenericWorkerBase<T>)
							│
				┌───────────┼───────────┐
				│           │           │
		 (every 5s)    Loop until  (manages
		   Polls        capacity    concurrency)
				│           │           │
				▼           ▼           ▼
		┌──────────────────────────────┐
		│ ProcessItemAsync()            │
		│ (for each claimed item)       │
		└─────────────┬────────────────┘
					  │
		┌─────────────┴──────────┬──────────────┐
		│                        │              │
		▼                        ▼              ▼
	INIT PHASE            PROCESS PHASE    HEARTBEAT PHASE
	─────────             ──────────────    ───────────────

1. Start Heartbeat
   ├─ Every ~100s        4. Do Work          └─ Every ~100s
   │  (LeaseSeconds/3)      ├─ Validate URL     (parallel task)
   │                        ├─ HTTP GET            │
2. InitializeAsync         ├─ Parse HTML/JSON     ├─ LeaseManager.RenewAsync()
   │                        ├─ Extract content      │  Calls WorkItemRepository
   └─ StartExecutionAsync   └─ Return result        ├─ If renewal fails
	  ├─ RecordAttemptAsync                        │  ├─ Cancel processing
	  └─ Mark RUNNING                              │  └─ Log warning

3. Listen for cancel      5. CompleteAsync      6. On Cancel/Shutdown
   (check token)             ├─ Publish Event     Cleanup
   └─ Let processor run       ├─ Call service     ├─ Cancel heartbeat
							 └─ Finish in DB    └─ Await final result

										(All 3 phases run CONCURRENTLY)
```

---

## 2. State Transitions in Database

### WorkItem States over Time

```
┌──────────┐
│  CLAIMED │  Initial state - sent to worker
│ (timeout╱│  Lease: workerID + token
│ renewal) │  Status: null or "QUEUED"
└────┬─────┘
	 │ WorkProcessor.InitializeAsync()
	 │ → RecordAttemptAsync("RUNNING")
	 ▼
┌──────────┐
│ RUNNING  │  Actively processing
│ (heartbeat│  Heartbeat renewed every ~100s
│ renewing) │  If no renewal: marked as FAILED
└────┬─────┘
	 │ 
	 ├──(success)──→ WorkProcessor.CompleteAsync(success=true)
	 │               → WorkItemRepository.CompleteAsync()
	 │               → State: COMPLETED (removed from queue)
	 │
	 └──(failure)──→ WorkProcessor.CompleteAsync(success=false)
					 → WorkItemRepository.CompleteAsync()
					 → Backoff strategy determines retry
					 → State: QUEUED (retry later)
							  or FAILED (max attempts)
```

---

## 3. Event Publishing Sequence

### From Completion to Webhook Delivery

```
					CollectorWorkProcessor
						  │
						  │ ProcessAsync completes
						  ▼
					CompleteAsync(result)
						  │
				┌─────────┴──────────┐
				│                    │
		   (if success)         (if failure)
				│                    │
				▼                    ▼
		CompleteExecutionAsync
		(WebsiteSelectorsService)
				│
		┌───────┴────────┐
		│                │
		▼                ▼
	A. CompleteAsync      B. PublishAsync<CollectionCompletedEvent>
	   (Repository)           (EventAggregator to EventBus)
	   │
	   └─ WorkItemRepository.CompleteAsync()
		  ├─ Mark COMPLETED or QUEUED
		  └─ Update database

	   ↓ (Response sent back to caller)

	RETURN result (0 if success, -1 if lease lost)


Meanwhile (asynchronously):

	   C. EventBusWorker (separate hosted service)
		  └─ Polls pending events
			 ├─ ClaimAsync (pending delivery)
			 ├─ HTTP POST to webhook URL
			 └─ CompleteAsync (mark delivered)


	   D. WebsiteSelectorsSubscriber
		  (event handler registered at startup)
		  │
		  └─ OnCollectionCompletedAsync(CollectionCompletedEvent)
			 │
			 ├─ Get registered EventBus webhook URLs
			 │  (for this selector)
			 │
			 └─ For each webhook:
				└─ QueueDeliveryAsync(eventBusId, collectedDataId)
				   (insert into EventBus queue)
```

---

## 4. Concurrent Task Execution Timeline

### Multiple items claimed simultaneously

```
Time    Work Item 1         Work Item 2         Work Item 3
────    ───────────         ───────────         ───────────

0s      ┌─ CLAIM            
		│
		│ Init              ┌─ CLAIM
		│                   │
		│ Process           │ Init
		│ ░░░░░             │
		│ 45s               │ Process
10s     │                   │ ░░░░░
		│                   │ 20s
		├─ Heartbeat ✓      │
		│ (renew lease)     ├─ Heartbeat ✓
		│                   │
20s     │ Process           │                   ┌─ CLAIM
		│ continues...      │                   │
		│                   │ Complete ✓        │ Init
30s     │                   │ Publish Event     │ Process
		│                   │                   │ ░░░░░
		├─ Heartbeat ✓      ├─ Clear from list  │
		│ (renew lease)     │                   │
		│                   │                   ├─ Heartbeat...
40s     │ Complete ✓        │ Task over, ready  │
		│ Publish Event     │ for new work      │ (continues)
		├─ Clear from list  │                   │
		│ Task over         │                   │
50s     │ Task over         │                   │
		│                   │                   │ Complete ✓
		│                   │                   │ (publishes event)

Total running: 3 concurrent items max (configured in WebsiteCollectorOptions)
```

---

## 5. Lease Renewal Heartbeat Diagram

### How Lease Doesn't Expire During Long Processing

```
Claim Time = T
Lease Duration = 300 seconds (5 minutes)
Heartbeat Interval = 100 seconds (LeaseSeconds / 3)
Processing Time = 240 seconds ✅ (within lease)

					Heartbeat Timeline

T + 0s    ┌─────────────────────────────────┐
		  │ CLAIMED (lease started)         │
		  │ Lease expires at: T + 300s      │
		  └─────────────────────────────────┘

T + 100s  ├─ RenewAsync() called
		  │ Heartbeat: Lease renewed
		  │ New expiry: T + 400s

T + 200s  ├─ RenewAsync() called
		  │ Heartbeat: Lease renewed
		  │ New expiry: T + 500s

T + 240s  ├─ Processing completes
		  │ CompleteAsync() called
		  │ Work item marked COMPLETED
		  │ Heartbeat task cancelled

T + 300s  ├─ (Would have been expiry, but already complete)
		  │ Database cleanup complete
```

### If Lease Is Lost:

```
T + 0s    ┌─────────────────────────────────┐
		  │ CLAIMED (lease started)         │
		  │ Lease expires at: T + 300s      │
		  └─────────────────────────────────┘

T + 100s  ├─ RenewAsync() ✓ (OK)
		  │ Lease renewed

T + 200s  ├─ RenewAsync() ✗ FAILS
		  │ └─ leaseLostSource.CancelAsync()
		  │ └─ Processing token cancelled
		  │ └─ Log: "Lease renewal rejected or lost"
		  │
		  │ ProcessAsync gets OperationCanceledException
		  │ └─ Heartbeat stops
		  │ └─ CompleteAsync still called (best effort)
		  │ └─ Item may be re-claimed by another worker

T + 300s  ├─ (Lease would have expired anyway)
		  │ Item possibly re-queued for retry
```

---

## 6. Error Scenarios & Handling

### Scenario A: Network Timeout During HTTP Call

```
ProcessAsync (CollectorWorkProcessor)
│
├─ Validate URL ✓
├─ Create linked cancellation token 
│  (global cancel + local timeout)
├─ await SendAsync(..., timeout.Token)
│  └─ Timeout.CancelAfter(item.TimeoutSeconds)
│     ├─ After 60s: CancellationTokenSource fires
│     └─ HttpClient aborted
│
└─ await timeout.Token  TIMEOUT ✗
   ├─ Catch TaskCanceledException
   └─ Return ProcessingResult 
	  { Success = false, 
		ErrorMessage = "timeout" }

	  ▼
	  CompleteAsync(success=false)
	  │
	  └─ CompleteExecutionAsync(...)
		 ├─ Publish CollectionCompletedEvent (error info)
		 ├─ Call repository.FinishAsync(success=false)
		 └─ WorkItemRepository marks for RETRY
			(respects MaxAttempts, applies backoff)
```

### Scenario B: Heartbeat Renewal Fails

```
Processing In Progress
│
├─ HeartbeatAsync running
├─ WorkProcessor.ProcessAsync() in progress
│
└─ T + 100s: Heartbeat fires
   │
   ├─ RenewAsync() → WorkItemRepository fails
   │  (database connection error)
   │
   ├─ RenewAsync returns FALSE
   │  
   ├─ Log: "Lease renewal rejected or lost"
   │
   └─ await leaseLostSource.CancelAsync()
	  ├─ processingToken cancelled
	  ├─ WorkProcessor.ProcessAsync receives cancellation
	  ├─ Cleanup (close HTTP connection, etc.)
	  └─ CompleteAsync called with best-effort error

		 Another worker may now claim this item
		 (old worker still has in-flight processing)
		 → Potential double-processing! ⚠️
```

### Scenario C: Complete Fails / Lease Already Lost

```
ProcessAsync completes successfully
│
└─ CompleteAsync(success=true)
   │
   ├─ CompleteExecutionAsync
   │
   └─ PublishAsync(CollectionCompletedEvent) ✓
	  │
	  └─ Call repository.FinishAsync(...)
		 │
		 └─ WorkItemRepository.CompleteAsync
			│
			└─ Database Error: Lease Token mismatch
			   (another worker already claimed it)
			   │
			   └─ CompleteAsync returns FALSE

				  ▼
				  CollectorWorkProcessor.CompleteAsync:
				  └─ Log warning: "Lease lost before commit"
				  └─ Result = -1
				  └─ Event already published though!
					 (may cause duplicate webhook deliveries)
```

---

## 7. Key Dependencies & Data Flow

```
┌─────────────────────────────────────────────────────────────┐
│                  WebsiteCollectorWorker                      │
│              (GenericWorkerBase<T> driver)                   │
└──────────────────┬──────────────────────────────────────────┘
				   │
	   ┌───────────┼───────────┐
	   │           │           │
	   ▼           ▼           ▼
   LEASE        WORK         COORDINATION

┌────────────────────┐  ┌─────────────────┐  ┌──────────────────┐
│ CollectorLease     │  │ CollectorWork   │  │ GenericWorkerBase│
│ Manager            │  │ Processor       │  │ (orchestrator)   │
│ (ILeaseManager)    │  │ (IWorkProcessor)│  │                  │
└────────┬───────────┘  └────────┬────────┘  └──────────────────┘
		 │                       │
		 │    ClaimAsync         │
		 │    RenewAsync         │    CompleteAsync(result)
		 │    ReleaseAsync       │    → Publish Event
		 │                       │    → Call FinishAsync
		 │                       │
		 └───────────┬───────────┘
					 │
		 ┌───────────▼────────────┐
		 │ WebsiteSelectorsService│
		 │ (Orchestration Layer)  │
		 └───────────┬────────────┘
					 │
		 ┌───────────▼────────────────────────┐
		 │ WebsiteSelectorsRepository         │
		 │ (Adapter: Domain ↔ ServiceBus)     │
		 └───────────┬────────────────────────┘
					 │
		   ┌─────────┴─────────┐
		   │                   │
		   ▼                   ▼
	  ┌──────────────────┐  ┌──────────────────┐
	  │ WorkItemRepository│ │ SelectorManagement│
	  │ (Beep.ServiceBus)│ │ Repository       │
	  │                  │ │                  │
	  │ ├─ ClaimAsync    │ │ ├─ GetAsync      │
	  │ ├─ RenewLeaseAsync│ │ └─ ...          │
	  │ └─ CompleteAsync │ │                  │
	  └────────┬─────────┘ └────────┬─────────┘
			   │                    │
			   └────────┬───────────┘
						│
			  ┌─────────▼──────────┐
			  │   SQL Database     │
			  │ ┌─────────────────┐│
			  │ │ ServiceBus      ││
			  │ │ (WorkItems)     ││
			  │ ├─ Id, WorkerType ││
			  │ ├─ LeaseToken     ││
			  │ ├─ Status         ││
			  │ └─ AttemptNumber  ││
			  │ ┌─────────────────┐│
			  │ │ Selector Domain ││
			  │ ├─ WebsiteSelectorId││
			  │ ├─ CollectionUrl  ││
			  │ └─ Selectors      ││
			  │ ┌─────────────────┐│
			  │ │ EventBus        ││
			  │ ├─ PendingEvents  ││
			  │ ├─ Deliveries     ││
			  │ └─ Status         ││
			  └────────────────────┘
```

---

## 8. Configuration Impact on Flow

```yaml
WebsiteCollectorOptions:
  PollIntervalSeconds: 5          # How often GenericWorkerBase.ExecuteAsync loops
  MaxConcurrentItems: 10          # Max parallel ProcessItemAsync tasks
  LeaseSeconds: 300               # Heartbeat renewal interval: 300/3 = 100s
  MaxResponseBytes: 10485760      # 10MB: limits HTTP response size
```

### Example Execution with Multiple Items:

```
Time  Action                     Queue Size  Running Count
────  ──────                     ──────────  ─────────────
0s    Start worker                   ?            0
2s    Poll for work
	  ├─ ClaimAsync(max=10)
	  ├─ Got 8 items                 0→8          0→8
	  └─ Start 8 ProcessItemAsync
5s    Poll for work
	  ├─ ClaimAsync(max=2)   [only 2 slots]
	  ├─ Got 2 items              2              8→10
	  └─ Start 2 ProcessItemAsync
7s    First item completes
	  ├─ CompleteAsync
	  ├─ Remove from running           0          10→9
10s   Poll for work
	  ├─ ClaimAsync(max=1)   [only 1 slot]
	  ├─ Got 1 item               1              9→10
	  └─ Start 1 ProcessItemAsync
15s   3 more items complete
	  ├─ Remove from running          0          10→7
20s   Poll for work
	  ├─ ClaimAsync(max=3)
	  ├─ Queue empty!              0             7→7
	  └─ No new work to claim
25s   Remaining items complete
	  ├─ 7 items done          0              7→0
30s   Poll for work
	  ├─ ClaimAsync(max=10)
	  ├─ Got 5 items              5              0→5
	  └─ Start 5 ProcessItemAsync
```

---

## Summary

Your architecture creates a **safe, scalable, self-healing work queue system**:

1. ✅ **Lease safety**: Heartbeat prevents zombie workers
2. ✅ **Graceful degradation**: Failed items retry with backoff
3. ✅ **Decoupled notification**: Events published BEFORE state persisted (eventual consistency)
4. ✅ **Concurrent processing**: Multiple workers/items can process in parallel
5. ⚠️ **Potential double-processing**: If lease lost + another worker claims same item

**Recommendation**: Log all lease-loss events and monitor for duplicate executions via the `CollectionCompletedEvent` aggregation.
