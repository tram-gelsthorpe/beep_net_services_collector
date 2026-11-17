# Yes! You Need to Register WorkItems - Here's How

## The Answer: YES ✅

Your WebsiteCollector worker won't have anything to process until you **register WorkItems** in the ServiceBus database.

---

## The 3-Database System (Reminder)

```
┌──────────────────────────────────────────────────────────────┐
│          YOUR BEEP_NET_SERVICES_COLLECTOR SYSTEM             │
├──────────────────────────────────────────────────────────────┤
│                                                               │
│  1. ServiceBus Database (Work Queue)                         │
│     └─ Stores: WorkItems, WorkItemStatuses, Attempts        │
│     └ Used by: GenericWorkerBase to claim/process items     │
│                                                               │
│  2. Collector Database (Domain Data)                         │
│     └─ Stores: WebsiteSelectors, WebsiteSelectorsData       │
│     └─ Used by: Repository to enrich work items             │
│                                                               │
│  3. EventBus Database (Event Notifications)                 │
│     └─ Stores: Events, Deliveries, Webhooks                │
│     └─ Used by: Subscriber to notify on completion          │
│                                                               │
└──────────────────────────────────────────────────────────────┘
```

---

## Simple Registration (3 Steps)

### Step 1: Create a WebsiteSelector (Collector DB)
```sql
-- In [Collector] database
INSERT INTO WebsiteSelectors (Id, WebsitesId, CollectionUrl, CollectionType, ContentSelector, CreatedAt, UpdatedAt)
VALUES (
	NEWID(),                    -- Id
	NEWID(),                    -- WebsitesId
	'https://example.com',      -- URL to collect
	'HTML',                     -- Type: HTML, JSON, or XML
	'.content',                 -- CSS selector for HTML
	GETUTCDATE(),               -- CreatedAt
	GETUTCDATE()                -- UpdatedAt
);

-- Save this ID
DECLARE @SelectorId = (SELECT TOP 1 Id FROM WebsiteSelectors ORDER BY CreatedAt DESC);
SELECT @SelectorId;  -- Copy this value
```

### Step 2: Register WorkItem (ServiceBus DB)
```sql
-- In [ServiceBus] database
DECLARE @SelectorId = 'PASTE-ID-FROM-ABOVE';  -- e.g., 'A1B2C3D4-...'
DECLARE @PendingStatusId = (SELECT Id FROM WorkItemStatuses WHERE Name = 'PENDING');

INSERT INTO WorkItems (
	Id, WorkerType, DomainId, DomainType, StatusId,
	AttemptCount, MaxAttempts, NextRunAt, CreatedAt, UpdatedAt
) VALUES (
	NEWID(),                    -- Unique work item ID
	'WebsiteCollector',         -- EXACT STRING - Case sensitive!
	@SelectorId,                -- Links to WebsiteSelector
	'WebsiteSelector',          -- Domain type
	@PendingStatusId,           -- Status = PENDING
	0,                          -- No attempts yet
	3,                          -- Retry up to 3 times
	GETUTCDATE(),               -- Ready now
	GETUTCDATE(),               -- Created
	GETUTCDATE()                -- Updated
);
```

### Step 3: Verify & Restart Worker
```sql
-- In [ServiceBus] database
SELECT TOP 1 
	Id as WorkItemId,
	WorkerType,
	(SELECT Name FROM WorkItemStatuses WHERE Id = StatusId) as Status,
	CreatedAt
FROM WorkItems
WHERE WorkerType = 'WebsiteCollector'
ORDER BY CreatedAt DESC;

-- If you see 1 row with Status = 'PENDING', you're good!
-- Restart your WebsiteCollectorWorker
```

---

## What This Does

```
BEFORE REGISTRATION:
┌─────────────────────────────────┐
│  Worker starts                  │
│  └─ Polls every 5 seconds       │
│  └─ Queries: "Any PENDING items │
│             for WebsiteCollector?"│
│  └─ Result: ❌ Returns 0 items   │
│     (because WorkItems table is  │
│      empty)                     │
│  └─ Worker idles                │
│                                 │
│ [Does nothing...]               │
│ [Does nothing...]               │
│ [Does nothing...]               │
└─────────────────────────────────┘

AFTER REGISTRATION (running script above):
┌─────────────────────────────────┐
│  Worker starts                  │
│  └─ Polls every 5 seconds       │
│  └─ Queries: "Any PENDING items │
│             for WebsiteCollector?"│
│  └─ Result: ✅ Returns 1 item    │
│     (your newly registered item) │
│  └─ Claims it with lease        │
│  └─ Makes HTTP request to URL   │
│  └─ Extracts content            │
│  └─ Marks complete              │
│  └─ Publishes event to EventBus │
│                                 │
│ [Repeats every 5 seconds...]    │
└─────────────────────────────────┘
```

---

## Why It Was Empty Before

```
ServiceBus.WorkItems table:
┌────────────────────┐
│ (empty table)      │
│                    │
│ No rows!           │
│                    │
│ So:                │
│ - Nothing to claim │
│ - Worker idles     │
│ - Logs show:       │
│   "returned 0"     │
└────────────────────┘
```

---

## Key Points (MUST GET THESE RIGHT)

| Field | Value | Wrong Examples |
|-------|-------|-----------------|
| **WorkerType** | `'WebsiteCollector'` | ❌ `'SelectorCollector'` ❌ `'websitecollector'` ❌ `'Collector'` |
| **DomainId** | ID from WebsiteSelectors | ❌ Random GUID ❌ NULL ❌ Wrong ID |
| **DomainType** | `'WebsiteSelector'` | ❌ `'WebsiteSelectors'` ❌ `'Selector'` |
| **StatusId** | `'PENDING'` status ID | ❌ Random GUID ❌ `'QUEUED'` (different system) |
| **AttemptCount** | `0` | ❌ `1` ❌ `NULL` |
| **MaxAttempts** | `3` or `5` | ❌ `999` ❌ `0` |
| **NextRunAt** | `GETUTCDATE()` | ❌ Future date ❌ NULL |

---

## One-Minute Test

1. **Run this** (in ServiceBus DB):
```sql
SELECT COUNT(*) as PendingWebsiteCollectorItems
FROM WorkItems
WHERE WorkerType = 'WebsiteCollector'
  AND (SELECT Name FROM WorkItemStatuses WHERE Id = StatusId) = 'PENDING';
```

2. **Result**: 
   - `0` → No work registered yet (run registration script)
   - `> 0` → Good! Worker should claim these

---

## Full Script (Copy-Paste Ready)

```sql
-- ===== STEP 1: Create Selector (in Collector DB) =====
USE [Collector];
GO

INSERT INTO WebsiteSelectors (
	Id, WebsitesId, CollectionUrl, CollectionType, 
	ContentSelector, TimeoutSeconds, MaxAttempts, CreatedAt, UpdatedAt
)
VALUES (
	NEWID(), NEWID(), 'https://example.com', 'HTML',
	'.content', 60, 3, GETUTCDATE(), GETUTCDATE()
);

-- Get the ID
DECLARE @SelectorId = (SELECT TOP 1 Id FROM WebsiteSelectors ORDER BY CreatedAt DESC);
SELECT 'Copy this ID:' as Note, @SelectorId as SelectorId;

-- ===== STEP 2: Register WorkItem (in ServiceBus DB) =====
USE [ServiceBus];
GO

DECLARE @SelectorId = '(PASTE-THE-ID-FROM-ABOVE)';  -- Replace with actual ID
DECLARE @PendingStatusId = (SELECT Id FROM WorkItemStatuses WHERE Name = 'PENDING');

INSERT INTO WorkItems (
	Id, WorkerType, DomainId, DomainType, StatusId,
	AttemptCount, MaxAttempts, NextRunAt, CreatedAt, UpdatedAt
)
VALUES (
	NEWID(),
	'WebsiteCollector',
	@SelectorId,
	'WebsiteSelector',
	@PendingStatusId,
	0, 3,
	GETUTCDATE(), GETUTCDATE(), GETUTCDATE()
);

-- ===== STEP 3: Verify =====
SELECT TOP 1
	'REGISTERED!' as Status,
	Id as WorkItemId,
	WorkerType,
	(SELECT Name FROM WorkItemStatuses WHERE Id = wi.StatusId) as StatusName,
	(SELECT CollectionUrl FROM [Collector].dbo.WebsiteSelectors WHERE Id = wi.DomainId) as URL
FROM WorkItems wi
WHERE WorkerType = 'WebsiteCollector'
ORDER BY CreatedAt DESC;

PRINT 'Done! Restart the worker to claim this item.';
```

---

## Next Steps

1. ✅ Run the script above (modify the URL if needed)
2. ✅ Verify you see 1 row in Step 3 output
3. ✅ Restart `WebsiteCollectorWorker`
4. ✅ Check logs for: `"WorkItemRepository returned 1 items"`
5. ✅ Worker should process the website URL!

---

## Support

For detailed guidance, see:
- **docs/REGISTER_WORKITEMS.md** - Complete registration guide
- **docs/WORKITEM_REGISTRATION_GUIDE.md** - Data flow & troubleshooting
- **docs/DIAGNOSTIC_GUIDE.md** - Debugging queue issues

---

## Summary

| Question | Answer |
|----------|--------|
| **Do I need to register WorkItems?** | ✅ YES |
| **Where do I insert them?** | ServiceBus database, WorkItems table |
| **What data do I need?** | WebsiteSelector ID (from Collector DB) |
| **What happens after?** | Worker claims & processes the item every 5 seconds |
| **Can I register multiple items?** | ✅ YES (bulk insert script in REGISTER_WORKITEMS.md) |
| **How do I test?** | Query for PENDING items, should see > 0 rows |

**Action**: Run the script above, restart worker, done! 🎉
