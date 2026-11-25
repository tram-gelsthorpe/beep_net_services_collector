# WorkItem Registration Flow Diagram

## The Data Flow

```
┌─────────────────────────────────────────────────────────────────────┐
│                    REGISTRATION PROCESS                             │
└─────────────────────────────────────────────────────────────────────┘

STEP 1: CREATE DOMAIN DATA (Collector DB)
┌───────────────────────────────┐
│  WebsiteSelectors Table       │
├───────────────────────────────┤
│ Id:               GUID        │
│ WebsitesId:       GUID        │
│ CollectionUrl:    URL         │ ← e.g., https://example.com
│ CollectionType:   String      │ ← e.g., "HTML", "JSON"
│ ContentSelector:  String      │ ← e.g., ".content", "$.data"
│ TimeoutSeconds:   int         │ ← e.g., 60
│ MaxAttempts:      int         │ ← e.g., 3
└───────────────────────────────┘
		 │
		 │ (Get this ID)
		 ▼
	 @SelectorId = [GUID]


STEP 2: REGISTER WORK ITEM (ServiceBus DB)
┌────────────────────────────────────────────────────────┐
│       WorkItems Table                                  │
├────────────────────────────────────────────────────────┤
│ Id:              NEWID()                               │
│ WorkerType:      'WebsiteCollector' ← EXACT STRING     │
│ DomainId:        @SelectorId        ← LINKS TO STEP 1  │
│ DomainType:      'WebsiteSelector'                     │
│ StatusId:        PENDING status Id                     │
│ AttemptCount:    0                                     │
│ MaxAttempts:     3 or 5                                │
│ NextRunAt:       GETUTCDATE()                          │
│ WorkerId:        NULL (until claimed)                  │
│ LeaseToken:      NULL (until claimed)                  │
└────────────────────────────────────────────────────────┘
		 │
		 │ (Worker polls for items)
		 ▼


STEP 3: WORKER CLAIMS ITEM
GenericWorkerBase<T>.ExecuteAsync()
		 │
		 ├─ Every 5 seconds (PollIntervalSeconds)
		 │
		 ├─ Call: LeaseManager.ClaimAsync()
		 │        └─ Call: WorkItemRepository.ClaimAsync()
		 │           └─ Execute: [ServiceBus].[dbo].[ClaimWorkItems]
		 │
		 ▼
claimWorkItems stored procedure
		 │
		 ├─ SELECT TOP @BatchSize items WHERE:
		 │  ├─ WorkerType = 'WebsiteCollector' ← MUST MATCH
		 │  ├─ StatusId = PENDING or FAILED
		 ├─ WorkerId IS NULL OR LeaseUntil < now
		 │  └─ NextRunAt <= now
		 │
		 ├─ UPDATE these items:
		 ├─ WorkerId = @WorkerId
		 │  ├─ LeaseToken = NEWID()
		 │  ├─ LeaseUntil = now + @LeaseSeconds (300s)
		 │  ├─ StatusId = CLAIMED
		 │  └─ AttemptCount = AttemptCount + 1
		 │
		 └─ RETURN items to worker
				 │
				 ▼
		Worker gets ClaimedWebsiteSelector
				 │
				 ├─ WorkItemId (from WorkItem)
				 ├─ DomainId (WebsiteSelector.Id)
				 ├─ LeaseToken (proof of ownership)
				 ├─ and enriched selector data
				 │  ├─ CollectionUrl
				 │  ├─ ContentSelector
				 │  ├─ TimeoutSeconds
				 │  └─ etc.
				 │
				 ▼
		ProcessAsync() starts
			 (HTTP request, parse, extract)
				 │
				 ▼
		CompleteAsync() finishes
			 (mark as SUCCEEDED or FAILED)
```

---

## Key Requirements

### ✅ MUST USE EXACT VALUE FOR WorkerType:
```
WorkerType = 'WebsiteCollector'

NOT:
'SelectorCollector'          ← Wrong
'websitecollector'           ← Case-sensitive! Wrong
'WebsiteSelector'            ← Wrong
'Collector'                  ← Wrong
```

### ✅ DomainId MUST REFERENCE EXISTING SELECTOR:
```
-- Get the ID first
DECLARE @SelectorId = (SELECT Id FROM WebsiteSelectors WHERE ...);

-- Use it when inserting work item
INSERT INTO WorkItems (DomainId, ...) VALUES (@SelectorId, ...);

NOT:
INSERT INTO WorkItems (DomainId, ...) VALUES (NEWID(), ...);  ← Random ID!
```

### ✅ StatusId MUST BE PENDING:
```sql
-- CORRECT
SELECT Id FROM WorkItemStatuses WHERE Name = 'PENDING'

-- Then use that Id when inserting
INSERT INTO WorkItems (StatusId, ...) VALUES (@PendingStatusId, ...);

NOT:
INSERT INTO WorkItems (StatusId, ...) VALUES (NEWID(), ...);  ← Random ID!
```

---

## Common Mistakes

### ❌ MISTAKE 1: Wrong WorkerType
```sql
INSERT INTO WorkItems (WorkerType, ...) VALUES ('SelectorCollector', ...);
-- Result: Worker never finds it (worker filters by 'WebsiteCollector')
```

### ❌ MISTAKE 2: Wrong DomainId
```sql
INSERT INTO WorkItems (DomainId, ...) VALUES (NEWID(), ...);
-- Result: Worker claims it, tries to find selector, crashes because doesn't exist
-- Your logs show: "No selectors found for work item..."
```

### ❌ MISTAKE 3: Wrong StatusId
```sql
INSERT INTO WorkItems (StatusId, ...) VALUES (NEWID(), ...);
-- Result: Status is not recognized, item never claimed
```

### ❌ MISTAKE 4: Not creating selector first
```sql
-- You do this:
INSERT INTO WorkItems (DomainId, ...) VALUES (...);

-- But this selector doesn't exist yet:
-- Result: Claims fail with "No selectors found"
```

---

## SQL Commands Quick Reference

### Check what statuses exist:
```sql
SELECT * FROM WorkItemStatuses;
```

### Get PENDING status ID (for copying):
```sql
SELECT Id FROM WorkItemStatuses WHERE Name = 'PENDING';
```

### Get all available selectors:
```sql
SELECT Id, CollectionUrl, CollectionType 
FROM [Collector].dbo.WebsiteSelectors;
```

### Register a work item:
```sql
DECLARE @SelectorId = [ID-FROM-ABOVE];
DECLARE @PendingStatusId = (SELECT Id FROM WorkItemStatuses WHERE Name = 'PENDING');

INSERT INTO WorkItems (
	Id, WorkerType, DomainId, DomainType, StatusId, 
	AttemptCount, MaxAttempts, NextRunAt, CreatedAt, UpdatedAt
) VALUES (
	NEWID(), 'WebsiteCollector', @SelectorId, 'WebsiteSelector',
	@PendingStatusId, 0, 3, GETUTCDATE(), GETUTCDATE(), GETUTCDATE()
);
```

### Check what's registered:
```sql
SELECT 
	wi.Id as WorkItemId,
	wi.WorkerType,
	wi.DomainId,
	(SELECT Name FROM WorkItemStatuses WHERE Id = wi.StatusId) as Status,
	wi.AttemptCount,
	wi.MaxAttempts,
	wi.CreatedAt
FROM WorkItems wi
WHERE wi.WorkerType = 'WebsiteCollector'
ORDER BY wi.CreatedAt DESC;
```

### See only items ready to be claimed:
```sql
SELECT TOP 10
	wi.Id,
	wi.WorkerType,
	ws.[CollectionUrl],           -- From selector
	(SELECT Name FROM WorkItemStatuses WHERE Id = wi.StatusId) as Status
FROM WorkItems wi
LEFT JOIN [Collector].dbo.WebsiteSelectors ws ON wi.DomainId = ws.Id
WHERE wi.WorkerType = 'WebsiteCollector'
  AND (SELECT Name FROM WorkItemStatuses WHERE Id = wi.StatusId) = 'PENDING'
ORDER BY wi.CreatedAt DESC;
```

---

## Verification Checklist

After registration, verify:

- [ ] WebsiteSelectors table has at least 1 row
- [ ] WorkItems table has at least 1 row with WorkerType = 'WebsiteCollector'
- [ ] WorkItem's DomainId matches a WebsiteSelector.Id
- [ ] WorkItem's StatusId matches 'PENDING' status
- [ ] NextRunAt is <= GETUTCDATE()
- [ ] WorkerId IS NULL
- [ ] LeaseToken IS NULL
- [ ] Worker starts claiming items (check logs)
- [ ] Logs show: "WorkItemRepository returned X items" where X > 0

---

## Troubleshooting

### Q: Worker logs show "returned 0 items"
A: Check that you have PENDING WorkItems:
```sql
SELECT COUNT(*) FROM WorkItems 
WHERE WorkerType = 'WebsiteCollector'
AND (SELECT Name FROM WorkItemStatuses WHERE Id = StatusId) = 'PENDING';
```
If 0, run the registration script.

### Q: Worker logs show "No selectors found"
A: The DomainId in WorkItem doesn't match any WebsiteSelector.Id:
```sql
-- Find the orphaned work item
SELECT wi.Id, wi.DomainId 
FROM WorkItems wi
WHERE wi.WorkerType = 'WebsiteCollector'
AND NOT EXISTS (SELECT 1 FROM [Collector].dbo.WebsiteSelectors ws WHERE ws.Id = wi.DomainId);

-- Create the missing selector or fix the DomainId
```

### Q: Stored procedure throws error
A: Most common: Invalid parameters. Check:
```sql
-- Verify parameters passed to ClaimWorkItems
-- @WorkerType must be exactly 'WebsiteCollector'
-- @BatchSize must be 1-500
-- @LeaseSeconds must be 60-3600

-- Test manually:
EXEC ClaimWorkItems 
	@WorkerType = 'WebsiteCollector',
	@WorkerId = NEWID(),
	@BatchSize = 10,
	@LeaseSeconds = 300;
```

---

## Summary

| Step | Database | Table | Action |
|------|----------|-------|--------|
| 1 | Collector | WebsiteSelectors | INSERT selector data |
| 2 | ServiceBus | WorkItems | INSERT work item (links to selector) |
| 3 | — | — | Worker claims and processes |

**Your current status**: Queue is empty because WorkItems table has no QUEUED items.  
**Solution**: Run the registration script from `docs/REGISTER_WORKITEMS.md`  
**Result**: Worker will claim and process items! ✅
