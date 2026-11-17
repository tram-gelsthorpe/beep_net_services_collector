# Complete Solution: Empty Queue → Working Worker

## 🚀 Quick Start (5 Minutes)

### Your Question: "Do I need to register WorkItems?"
**Answer: YES ✅** 

The worker can't process anything if there are no items in the queue. Here's what to do:

---

## Step-by-Step Solution

### 1️⃣ Create a WebsiteSelector (Collector Database)
```sql
USE [Collector];

INSERT INTO WebsiteSelectors (
	Id, WebsitesId, CollectionUrl, CollectionType, 
	ContentSelector, TimeoutSeconds, MaxAttempts, CreatedAt, UpdatedAt
)
VALUES (
	NEWID(),                      -- Unique ID (save this!)
	NEWID(),                      -- WebsitesId
	'https://example.com',        -- URL to collect
	'HTML',                       -- Type: HTML, JSON, XML
	'.content',                   -- CSS selector for HTML
	60,                           -- Timeout in seconds
	3,                            -- Max retry attempts
	GETUTCDATE(),
	GETUTCDATE()
);

-- Get the ID for next step
SELECT TOP 1 Id FROM WebsiteSelectors ORDER BY CreatedAt DESC;
-- Copy this ID ↑
```

### 2️⃣ Register WorkItem (ServiceBus Database)
```sql
USE [ServiceBus];

DECLARE @SelectorId = 'PASTE-ID-FROM-ABOVE';  -- e.g., 'A1B2C3D4-5E6F-...'
DECLARE @PendingStatusId = (SELECT Id FROM WorkItemStatuses WHERE Name = 'PENDING');

INSERT INTO WorkItems (
	Id, WorkerType, DomainId, DomainType, StatusId,
	AttemptCount, MaxAttempts, NextRunAt, CreatedAt, UpdatedAt
)
VALUES (
	NEWID(),
	'WebsiteCollector',           -- EXACT: Case-sensitive!
	@SelectorId,                  -- Links to WebsiteSelector
	'WebsiteSelector',
	@PendingStatusId,             -- Status: PENDING
	0,
	3,
	GETUTCDATE(),
	GETUTCDATE(),
	GETUTCDATE()
);
```

### 3️⃣ Verify & Restart Worker
```sql
-- Verify it was registered
SELECT TOP 1 * FROM WorkItems 
WHERE WorkerType = 'WebsiteCollector' 
ORDER BY CreatedAt DESC;

-- Should see 1 row with Status = 'PENDING'
```

**Then**: Restart your WebsiteCollectorWorker application.

**Result**: Worker claims the item and processes it! ✅

---

## What Just Happened

```
Before:
├─ ServiceBus.WorkItems table = EMPTY
└─ Worker finds nothing to claim → idles

After:
├─ ServiceBus.WorkItems table = 1 PENDING item
├─ Worker claims it
├─ Fetches selector data from Collector DB
├─ Makes HTTP request to https://example.com
├─ Extracts .content selector
├─ Marks complete
└─ Publishes event to EventBus
```

---

## Documentation Files (In Order of Usefulness)

| Document | Purpose | When to Read |
|----------|---------|--------------|
| **YES_REGISTER_WORKITEMS.md** | Quick answer with copy-paste script | First - Get the answer |
| **REGISTER_WORKITEMS.md** | Detailed registration guide + SQL examples | Setup - Follow this guide |
| **WORKITEM_REGISTRATION_GUIDE.md** | Flow diagrams + troubleshooting | Understanding - How it works |
| **EMPTY_QUEUE_FIX.md** | Why queue was empty + logging fix | Background - What happened |
| **DIAGNOSTIC_GUIDE.md** | SQL queries to investigate issues | Debugging - If something breaks |
| **IMPLEMENTATION_SUMMARY.md** | Code changes made to repository | Reference - What was changed |
| **ARCHITECTURE_REVIEW.md** | System design overview | Deep dive - Full architecture |
| **SERVICEBUS_FLOW_ANALYSIS.md** | Data flow + state machines | Understanding - How data flows |
| **QUICK_FIX_GUIDE.md** | 1-page reference card | Cheat sheet - Quick lookup |
| **VISUAL_GUIDE.md** | ASCII diagrams + decision trees | Visual - Understand the flow |

---

## The Three-Database System Explained

### 1. ServiceBus Database (Work Queue)
- **Stores**: WorkItems, WorkItemStatuses, Attempts, Heartbeats
- **Tables**: 
  - `WorkItems` ← **You insert work here**
  - `WorkItemStatuses` ← Predefined (PENDING, CLAIMED, RUNNING, etc.)
  - `WorkItemAttempts` ← Auto-updated (retry tracking)
  - `WorkItemHeartbeats` ← Auto-updated (lease renewals)
- **Purpose**: Generic work queue for any worker type

### 2. Collector Database (Domain Data)
- **Stores**: WebsiteSelectors, CollectedData, Executions
- **Tables**:
  - `WebsiteSelectors` ← **You insert selectors here**
  - Other domain-specific tables
- **Purpose**: Store website collection configuration

### 3. EventBus Database (Notifications)
- **Stores**: Events, EventDeliveries, Webhooks
- **Purpose**: Notify downstream systems when collection completes

---

## Common Issues & Quick Fixes

### Issue 1: "Worker logs show returned 0 items"
**Root Cause**: No PENDING WorkItems in ServiceBus

**Fix**:
```sql
-- Check count
SELECT COUNT(*) FROM WorkItems 
WHERE WorkerType = 'WebsiteCollector'
AND (SELECT Name FROM WorkItemStatuses WHERE Id = StatusId) = 'PENDING';

-- If 0, run registration script above
```

### Issue 2: "No selectors found for work item"
**Root Cause**: DomainId doesn't reference existing WebsiteSelector

**Fix**:
```sql
-- Check for orphaned items
SELECT wi.Id, wi.DomainId 
FROM WorkItems wi
WHERE wi.WorkerType = 'WebsiteCollector'
AND NOT EXISTS (
	SELECT 1 FROM [Collector].dbo.WebsiteSelectors ws 
	WHERE ws.Id = wi.DomainId
);

-- Either delete orphaned items or create missing selectors
```

### Issue 3: "Wrong WorkerType" (not claimed)
**Root Cause**: WorkerType isn't exactly 'WebsiteCollector'

**Fix**:
```sql
-- Check all work items
SELECT DISTINCT WorkerType FROM WorkItems;

-- Must see: 'WebsiteCollector'
-- If not: Delete and re-register with correct value
```

---

## SQL Reference Card

### Get Status Constants
```sql
SELECT * FROM WorkItemStatuses;
```

### Get PENDING ID (for copying)
```sql
SELECT Id FROM WorkItemStatuses WHERE Name = 'PENDING';
```

### View All WebsiteCollector Items
```sql
SELECT TOP 10 
	Id, WorkerType, 
	(SELECT Name FROM WorkItemStatuses WHERE Id = StatusId) as Status,
	CreatedAt
FROM WorkItems
WHERE WorkerType = 'WebsiteCollector'
ORDER BY CreatedAt DESC;
```

### Count by Status
```sql
SELECT 
	(SELECT Name FROM WorkItemStatuses WHERE Id = StatusId) as Status,
	COUNT(*) as Count
FROM WorkItems
WHERE WorkerType = 'WebsiteCollector'
GROUP BY StatusId;
```

### Register Single Item
```sql
DECLARE @SelectorId = (SELECT TOP 1 Id FROM [Collector].dbo.WebsiteSelectors);
DECLARE @PendingId = (SELECT Id FROM WorkItemStatuses WHERE Name = 'PENDING');

INSERT INTO WorkItems (
	Id, WorkerType, DomainId, DomainType, StatusId,
	AttemptCount, MaxAttempts, NextRunAt, CreatedAt, UpdatedAt
) VALUES (
	NEWID(), 'WebsiteCollector', @SelectorId, 'WebsiteSelector',
	@PendingId, 0, 3, GETUTCDATE(), GETUTCDATE(), GETUTCDATE()
);
```

### Register Bulk Items
```sql
-- See REGISTER_WORKITEMS.md for bulk script
```

---

## Expected Behavior After Registration

```
Timeline:
T+0s   - Worker starts, begins polling
T+5s   - First poll: ClaimAsync → finds 1 PENDING item → claims it
		 Logs: "WorkItemRepository returned 1 items"
		 Status changes: PENDING → CLAIMED
	   - InitializeAsync runs
	   - ProcessAsync runs (HTTP request)
T+10s  
through - Worker processes (extracts content, etc.)
T+65s  - Heartbeat renewals every ~100 seconds (LeaseSeconds/3)

T+70s  - ProcessAsync completes
	   - CompleteAsync runs
	   - Status changes: CLAIMED → SUCCEEDED
	   - Event published to EventBus
	   - Logs: "Item processed successfully"

T+75s  - Next poll: ClaimAsync → looks for more PENDING items
		 Returns 0 (because queue is empty again)
		 Worker idles
```

---

## Testing Checklist

- [ ] WebsiteSelector created in Collector DB
- [ ] WorkItem created in ServiceBus DB
- [ ] WorkItem has WorkerType = 'WebsiteCollector' (exact case)
- [ ] WorkItem has Status = 'PENDING'
- [ ] WorkItem's DomainId matches WebsiteSelector.Id
- [ ] Worker restarted
- [ ] Logs show: "WorkItemRepository returned X items" where X > 0
- [ ] Logs show: "Processing claimed item..."
- [ ] HTTP request made to CollectionUrl
- [ ] Event published (check EventBus logs)

---

## FAQ

**Q: Do I need to insert data manually into WorkItems?**  
A: YES. The framework provides the queue infrastructure, you supply the work.

**Q: Can I bulk-register multiple items?**  
A: YES. See `docs/REGISTER_WORKITEMS.md` for a bulk insert script.

**Q: What happens if I insert an item with a non-existent selector?**  
A: Worker claims it, then skips it with error logging (thanks to our fix).

**Q: Can multiple workers claim the same item?**  
A: NO. The database uses SERIALIZABLE isolation and UPDLOCK to prevent this.

**Q: What if a worker crashes?**  
A: Its lease expires (LeaseSeconds), then another worker can claim it and retry.

**Q: How many times will it retry?**  
A: Up to MaxAttempts (usually 3). After that, it's ABANDONED.

---

## Summary Table

| Component | Status | Action |
|-----------|--------|--------|
| **Code** | ✅ Fixed | Added logging + error handling |
| **Database Schema** | ✅ Ready | ServiceBus tables exist |
| **WorkItem Registration** | ❌ **TODO** | **Run SQL script above** |
| **Worker** | ✅ Ready | Will claim items once registered |

---

## Next Steps

### Immediate (Now)
1. ✅ Copy SQL script from "Step-by-Step Solution" above
2. ✅ Replace URL and selectors as needed
3. ✅ Run in Collector + ServiceBus databases
4. ✅ Restart WebsiteCollectorWorker

### Later (Optional)
- ✅ Create an API endpoint to register items automatically
- ✅ Bulk-import selectors from external source
- ✅ Set up monitoring dashboards for queue health
- ✅ Create scheduled jobs to generate work items

---

## Where to Find Everything

```
docs/
├── YES_REGISTER_WORKITEMS.md        ← START HERE
├── REGISTER_WORKITEMS.md            ← Detailed guide
├── WORKITEM_REGISTRATION_GUIDE.md   ← Diagrams
├── EMPTY_QUEUE_FIX.md               ← What was fixed
├── DIAGNOSTIC_GUIDE.md              ← Troubleshooting
├── IMPLEMENTATION_SUMMARY.md        ← Code changes
├── ARCHITECTURE_REVIEW.md           ← Full system
├── SERVICEBUS_FLOW_ANALYSIS.md      ← Data flow
├── QUICK_FIX_GUIDE.md               ← Cheat sheet
└── VISUAL_GUIDE.md                  ← ASCII art
```

---

## Success Criteria

After following this guide, you should see:

✅ WebsiteCollector in logs claiming items  
✅ HTTP requests being made to your CollectionUrl  
✅ Content being extracted and parsed  
✅ Events published when collection completes  
✅ No "returned 0 items" warnings in logs  

**You're done!** 🎉

---

**Questions?** Check the docs above or review the architecture diagrams in VISUAL_GUIDE.md
