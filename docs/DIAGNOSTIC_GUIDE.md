# Debugging Empty Queue - Diagnostic Guide

## The Problem: Line 28 Returns Empty

When `_workItemRepository.ClaimAsync()` returns 0 items (line 35-40), the foreach loop never executes, and an empty list is returned to the worker.

This is **not a code bug** — it means the **ServiceBus database queue is empty** or has no available items to claim.

---

## Diagnostic Steps (In Order)

### Step 1: Check Worker Logs
Your code now logs at line 31-33 and 42-44:

```
ClaimAsync: Requesting 10 work items for worker [worker-id] with 300s lease
ClaimAsync: WorkItemRepository returned 0 items
ClaimAsync: No items claimed. Queue may be empty or all items already leased.
```

If you see this message repeatedly, proceed to Step 2.

**To Enable Debug Logging**, update `appsettings.json`:
```json
{
  "Logging": {
	"LogLevel": {
	  "Default": "Information",
	  "WebsiteCollector.Worker": "Debug"  // ← Add this
	}
  }
}
```

---

### Step 2: Check the ServiceBus Database

Open SQL Management Studio and run these queries:

#### Query 1: See All Items
```sql
SELECT 
	COUNT(*) as Total,
	Status,
	WorkerType
FROM WorkItems
GROUP BY Status, WorkerType
ORDER BY Status;
```

**Expected Output** (if working):
```
Total  | Status  | WorkerType
-------|---------|------------------
  10   | QUEUED  | WebsiteCollector
   2   | RUNNING | WebsiteCollector
   1   | COMPLETED | WebsiteCollector
```

**Problem Signs**:
- No rows = Queue is empty (data wasn't inserted)
- Only COMPLETED items = All work is done
- Only RUNNING items = All items are currently leased

---

#### Query 2: Check WebsiteCollector Items Specifically
```sql
SELECT TOP 20
	Id,
	DomainId,
	Status,
	WorkerType,
	LeaseToken,
	LeaseExpiredAt,
	AttemptNumber,
	MaxAttempts,
	CreatedAt,
	UpdatedAt
FROM WorkItems
WHERE WorkerType = 'WebsiteCollector'
ORDER BY CreatedAt DESC;
```

**What to look for**:
- ✅ Items with Status = 'QUEUED' → These should be claimed
- ⚠️ Items with Status = 'RUNNING' → Already leased to another worker
- ❌ Status = 'COMPLETED' or 'FAILED' → No longer available
- ⚠️ LeaseExpiredAt < GETUTCDATE() → Expired lease (should be reclaimed)

---

#### Query 3: Check for Expired Leases (Should Be Reclaimed)
```sql
SELECT TOP 20
	Id,
	DomainId,
	Status,
	WorkerType,
	LeaseToken,
	LeaseExpiredAt,
	DATEDIFF(SECOND, LeaseExpiredAt, GETUTCDATE()) as SecondsSinceExpiry
FROM WorkItems
WHERE WorkerType = 'WebsiteCollector'
	AND Status = 'RUNNING'
	AND LeaseExpiredAt < GETUTCDATE()
ORDER BY LeaseExpiredAt ASC;
```

**If these exist**: ⚠️ Leases are not being auto-reclaimed. The database should reclaim them automatically, but they're stuck.

---

#### Query 4: Check for Lease Collisions (Same Item Claimed Twice)
```sql
SELECT 
	DomainId,
	COUNT(*) as Count,
	STRING_AGG(CAST(Id AS VARCHAR(MAX)), ', ') as AllIds
FROM WorkItems
WHERE WorkerType = 'WebsiteCollector'
	AND Status IN ('RUNNING', 'QUEUED')
GROUP BY DomainId
HAVING COUNT(*) > 1
ORDER BY Count DESC;
```

**If results appear**: 🚨 Same item exists multiple times. Data integrity issue.

---

### Step 3: Check the Selector Domain Database

The selectors need to exist too. Verify in your **Collector**  database:

```sql
-- Query 1: Are there any website selectors?
SELECT COUNT(*) as TotalSelectors
FROM WebsiteSelectors;

-- Query 2: Are they linked to work items?
SELECT 
	ws.Id as SelectorId,
	COUNT(wi.Id) as LinkedWorkItems
FROM WebsiteSelectors ws
LEFT JOIN WorkItems wi ON ws.Id = wi.DomainId 
		   AND wi.WorkerType = 'WebsiteCollector'
GROUP BY ws.Id
ORDER BY LinkedWorkItems DESC;

-- Query 3: Check for orphaned selectors (no work item)
SELECT TOP 10
	Id as SelectorId,
	CollectionUrl,
	CreatedAt
FROM WebsiteSelectors
WHERE Id NOT IN (
	SELECT DISTINCT DomainId 
	FROM WorkItems 
	WHERE WorkerType = 'WebsiteCollector'
)
ORDER BY CreatedAt DESC;
```

---

### Step 4: Trace the Complete Flow

```
┌─────────────────────────────────────────────────────┐
│ Is there data in WorkItems table?                   │
├─────────────────────────────────────────────────────┤
│ NO → Data hasn't been inserted yet                  │
│      Action: Insert test data or check API/upload   │
│                                                      │
│ YES → Check work item status                        │
│      ↓                                               │
│      Status = QUEUED?                               │
│      ├─ NO  → Items are RUNNING/COMPLETED           │
│      │        Action: Wait for completion or check  │
│      │        why they're not QUEUED                │
│      └─ YES → Move to Step 5                        │
└─────────────────────────────────────────────────────┘
```

---

### Step 5: Check Worker Configuration

Verify your worker is configured correctly:

**appsettings.json** should have:
```json
{
  "WebsiteCollectorOptions": {
	"WorkerId": "worker-1",          // ← Unique per instance
	"PollIntervalSeconds": 5,
	"MaxConcurrentItems": 10,
	"LeaseSeconds": 300,
	"MaxResponseBytes": 10485760
  },
  "ConnectionStrings": {
	"ServiceBus": "Server=...;Database=ServiceBus;...",
	"Collector": "Server=...;Database=Collector;...",
	"EventBus": "Server=...;Database=EventBus;..."
  }
}
```

**Check in Program.cs logs**:
```
Looking for connection string: "ServiceBus"
Looking for connection string: "Collector"
Worker ID: [your-worker-id]
Max concurrent items: 10
Poll interval: 5 seconds
```

---

### Step 6: Insert Test Data

If the queue is empty, insert test data:

```sql
-- 1. Create a test website selector
INSERT INTO WebsiteSelectors (Id, WebsitesId, CollectionUrl, CollectionType, ContentSelector, CreatedAt)
VALUES 
(
	NEWID(),
	NEWID(),
	'https://example.com',
	'HTML',
	'.content',
	GETUTCDATE()
);

-- Get the ID from previous insert
DECLARE @SelectorId UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM WebsiteSelectors ORDER BY CreatedAt DESC);

-- 2. Create a work item pointing to it
INSERT INTO WorkItems (Id, WorkerType, DomainId, Status, MaxAttempts, AttemptNumber, CreatedAt, UpdatedAt)
VALUES
(
	NEWID(),
	'WebsiteCollector',
	@SelectorId,
	'QUEUED',
	3,
	0,
	GETUTCDATE(),
	GETUTCDATE()
);

-- 3. Verify it was inserted
SELECT TOP 5 * FROM WorkItems WHERE WorkerType = 'WebsiteCollector' ORDER BY CreatedAt DESC;
```

---

## Common Causes & Fixes

| Symptom | Cause | Fix |
|---------|-------|-----|
| **Empty queue** | No data inserted | Insert test data or verify API/upload |
| **All items RUNNING** | All leased, waiting for completion | Wait or manually mark some as QUEUED |
| **All items COMPLETED** | Processing finished | Insert more work items |
| **Expired leases stuck** | DB issue with auto-reclaim | Run reclaim stored procedure manually |
| **Selector not found** | DomainId doesn't match | Verify FK relationship in data |
| **Multiple workers, no items claimed** | Worker ID collision | Ensure each worker has unique ID |

---

## What the New Logging Shows

Your updated code now logs:

```
[INFO] ClaimAsync: Requesting 10 work items for worker abc123 with 300s lease
[INFO] ClaimAsync: WorkItemRepository returned 5 items                          ← Key: How many claimed
[DEBUG] ClaimAsync: Processing claimed item id1, DomainId=domain1
[DEBUG] ClaimAsync: Successfully enriched item id1 with selector data
[DEBUG] ClaimAsync: Processing claimed item id2, DomainId=domain2
[DEBUG] ClaimAsync: Successfully enriched item id2 with selector data
[INFO] ClaimAsync: Returning 5 enriched items (claimed 5, skipped 0)

--- Next poll after 5 seconds ---

[INFO] ClaimAsync: Requesting 10 work items for worker abc123 with 300s lease
[WARNING] ClaimAsync: No items claimed. Queue may be empty or all items already leased.
[INFO] ClaimAsync: Returning 0 enriched items (claimed 0, skipped 0)
```

---

## Running the Diagnostic

1. **Enable Debug logging** in `appsettings.json`
2. **Start the worker** and let it run for 2-3 poll cycles (15 seconds)
3. **Check logs** - you'll see exactly where items are lost
4. **Run SQL queries** above to understand the database state
5. **Insert test data** if queue is empty
6. **Restart worker** and watch for items being claimed

---

## Example Scenario: Queue is Empty

```
Logs show:
[WARNING] No items claimed. Queue may be empty or all items already leased.

SQL Query Result:
QUEUED count: 0
RUNNING count: 0
COMPLETED count: 10

Root cause: All work is done, queue is empty by design.
Fix: Either:
  - Insert more work items
  - Check if this is the expected state
```

---

## Example Scenario: Items Stuck RUNNING

```
Logs show:
[INFO] WorkItemRepository returned 0 items

SQL Query Result:
QUEUED count: 0
RUNNING count: 5
COMPLETED count: 0

Root cause: 5 items are locked to another worker with active lease

Solutions:
  1. Wait for processing to finish (5-10 minutes)
  2. Check other worker instance logs
  3. If worker crashed: Manually mark RUNNING items as QUEUED
	 UPDATE WorkItems SET Status = 'QUEUED', LeaseToken = NULL 
	 WHERE Status = 'RUNNING'
```

---

## Next Actions

After running diagnostics:

✅ **If queue is empty**: Insert test data and restart  
✅ **If items exist but not claimed**: Check logs for error details  
✅ **If items RUNNING**: Wait or kill the processing worker  
✅ **If selectors missing**: Link work items to existing selectors  

**Then**: Run worker again and verify logs show items being claimed and processed!
