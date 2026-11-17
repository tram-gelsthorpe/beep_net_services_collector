# How to Register WorkItems for WebsiteCollector

## Understanding the Flow

```
┌──────────────┐
│   Create     │
│ WebsiteSelector │──── (DomainId references this)
│   in Collector DB   │
└──────────────┘
	   │
	   │ Link to it
	   ▼
┌──────────────────────┐
│   Create WorkItem    │
│   in ServiceBus DB   │────── Worker claims this
│   (status = PENDING) │       and processes it
└──────────────────────┘
```

---

## Step 1: Ensure WebsiteSelector Exists

**Database**: Collector (Selector Domain)  
**Table**: `WebsiteSelectors`

```sql
-- Check existing selectors
SELECT TOP 10 
	Id, 
	WebsitesId,
	CollectionUrl,
	CollectionType,
	ContentSelector,
	CreatedAt
FROM WebsiteSelectors
ORDER BY CreatedAt DESC;

-- If empty, create test selector
INSERT INTO WebsiteSelectors (
	Id,
	WebsitesId, 
	CollectionUrl, 
	CollectionType, 
	ContentSelector, 
	TableSelector,
	TimeoutSeconds,
	MaxAttempts,
	CreatedAt,
	UpdatedAt
) VALUES (
	NEWID(),                           -- Id
	NEWID(),                           -- WebsitesId (any valid GUID)
	'https://example.com',             -- CollectionUrl
	'HTML',                            -- CollectionType (HTML, JSON, XML)
	'.content',                        -- ContentSelector (CSS selector)
	NULL,                              -- TableSelector (for HTML table extraction)
	60,                                -- TimeoutSeconds
	3,                                 -- MaxAttempts
	GETUTCDATE(),                      -- CreatedAt
	GETUTCDATE()                       -- UpdatedAt
);

-- Save this ID for the next step
DECLARE @SelectorId UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM WebsiteSelectors ORDER BY CreatedAt DESC);
SELECT @SelectorId AS SelectorIdForWorkItem;
```

---

## Step 2: Register WorkItem in ServiceBus

**Database**: ServiceBus  
**Table**: `WorkItems`

```sql
-- Get the WebsiteSelector ID from above
DECLARE @SelectorId UNIQUEIDENTIFIER = 'YOUR-SELECTOR-ID-HERE';

-- Get the PENDING status ID
DECLARE @PendingStatusId UNIQUEIDENTIFIER = (
	SELECT Id FROM dbo.WorkItemStatuses WHERE Name = 'PENDING'
);

-- Register the work item
INSERT INTO dbo.WorkItems (
	Id,
	WorkerType,
	DomainId,
	DomainType,
	StatusId,
	LeaseOwner,
	LeaseToken,
	LeaseUntil,
	AttemptCount,
	MaxAttempts,
	NextRunAt,
	CreatedAt
) VALUES (
	NEWID(),                           -- Id (unique work item identifier)
	'WebsiteCollector',                -- WorkerType (MUST be this exact value)
	@SelectorId,                       -- DomainId (links to WebsiteSelector)
	'WebsiteSelector',                 -- DomainType (indicates what kind of domain)
	@PendingStatusId,                  -- StatusId (starts as PENDING)
	NULL,                              -- LeaseOwner (empty until claimed)
	NULL,                              -- LeaseToken (generated when claimed)
	NULL,                              -- LeaseUntil (set when claimed)
	0,                                 -- AttemptCount (0 before first try)
	3,                                 -- MaxAttempts (retry up to 3 times)
	GETUTCDATE(),                      -- NextRunAt (ready to process now)
	GETUTCDATE()                       -- CreatedAt
);

-- Verify it was inserted
SELECT TOP 1 * FROM dbo.WorkItems 
WHERE WorkerType = 'WebsiteCollector'
ORDER BY CreatedAt DESC;
```

---

## Common Issues & Fixes

### Issue 1: "WorkItemStatuses table is empty"
```sql
-- Verify statuses exist
SELECT * FROM dbo.WorkItemStatuses;

-- If empty, run the initialization (from 0.sql)
INSERT INTO dbo.WorkItemStatuses (Id, Name, Description) VALUES
	(NEWID(), 'PENDING', 'Waiting to be claimed by a worker'),
	(NEWID(), 'CLAIMED', 'Actively leased by a worker'),
	(NEWID(), 'RUNNING', 'Currently being processed'),
	(NEWID(), 'SUCCEEDED', 'Completed successfully'),
	(NEWID(), 'FAILED', 'Attempt failed, will be retried'),
	(NEWID(), 'ABANDONED', 'Exceeded max retries'),
	(NEWID(), 'DEFERRED', 'Temporarily paused');
```

### Issue 2: "WorkerType must be 'WebsiteCollector'"
```sql
-- CORRECT
INSERT INTO WorkItems (WorkerType, ...) VALUES ('WebsiteCollector', ...);

-- WRONG - These won't be claimed
INSERT INTO WorkItems (WorkerType, ...) VALUES ('SelectorCollector', ...);
INSERT INTO WorkItems (WorkerType, ...) VALUES ('websitecollector', ...);  -- Case sensitive!
INSERT INTO WorkItems (WorkerType, ...) VALUES ('WebsiteSelector', ...);
```

### Issue 3: "DomainId doesn't link to selector"
```sql
-- WRONG - DomainId should point to WebsiteSelector.Id
INSERT INTO WorkItems (DomainId, ...) VALUES (NEWID(), ...);

-- CORRECT - DomainId MUST reference an existing selector
DECLARE @SelectorId = (SELECT Id FROM WebsiteSelectors WHERE CollectionUrl = 'https://example.com');
INSERT INTO WorkItems (DomainId, ...) VALUES (@SelectorId, ...);
```

### Issue 4: "Worker doesn't claim items"
```sql
-- Check the status
SELECT StatusId, Status = (SELECT Name FROM WorkItemStatuses WHERE Id = StatusId)
FROM WorkItems 
WHERE WorkerType = 'WebsiteCollector';

-- Must be 'PENDING' or 'FAILED' to be claimed
-- If you see 'RUNNING' or 'SUCCEEDED', worker already processed them
```

---

## Complete Script (Copy & Paste Ready)

```sql
-- =====================================================
-- Complete: Register WebsiteCollector WorkItems
-- Run this in ServiceBus database
-- =====================================================

-- Step 1: Verify statuses exist
IF NOT EXISTS (SELECT 1 FROM dbo.WorkItemStatuses WHERE Name = 'PENDING')
BEGIN
	INSERT INTO dbo.WorkItemStatuses (Id, Name, Description) VALUES
		(NEWID(), 'PENDING', 'Waiting to be claimed by a worker'),
		(NEWID(), 'CLAIMED', 'Actively leased by a worker'),
		(NEWID(), 'RUNNING', 'Currently being processed'),
		(NEWID(), 'SUCCEEDED', 'Completed successfully'),
		(NEWID(), 'FAILED', 'Attempt failed, will be retried'),
		(NEWID(), 'ABANDONED', 'Exceeded max retries'),
		(NEWID(), 'DEFERRED', 'Temporarily paused');
	PRINT 'Status table initialized';
END
ELSE
BEGIN
	PRINT 'Status table already initialized';
END;

-- Step 2: Get or create WebsiteSelector
-- For this example, assuming you have a selector in Collector DB
-- If not, you need to insert one there first

DECLARE @SelectorId UNIQUEIDENTIFIER;

-- Try to get existing selector
SELECT TOP 1 @SelectorId = Id FROM [Collector].[dbo].WebsiteSelectors;

IF @SelectorId IS NULL
BEGIN
	PRINT 'ERROR: No WebsiteSelectors found in Collector database!';
	PRINT 'Please insert a WebsiteSelector there first.';
END
ELSE
BEGIN
	-- Step 3: Register work item in ServiceBus
	DECLARE @PendingStatusId UNIQUEIDENTIFIER = (
		SELECT Id FROM dbo.WorkItemStatuses WHERE Name = 'PENDING'
	);

	IF @PendingStatusId IS NULL
	BEGIN
		PRINT 'ERROR: PENDING status not found!';
	END
	ELSE
	BEGIN
		INSERT INTO dbo.WorkItems (
			Id,
			WorkerType,
			DomainId,
			DomainType,
			StatusId,
			LeaseOwner,
			LeaseToken,
			LeaseUntil,
			AttemptCount,
			MaxAttempts,
			NextRunAt,
			CreatedAt,
			UpdatedAt
		) VALUES (
			NEWID(),
			'WebsiteCollector',
			@SelectorId,
			'WebsiteSelector',
			@PendingStatusId,
			NULL,
			NULL,
			NULL,
			0,
			3,
			GETUTCDATE(),
			GETUTCDATE(),
			GETUTCDATE()
		);

		PRINT 'WorkItem registered for WebsiteCollector';

		-- Verify
		SELECT TOP 1 
			Id as WorkItemId, 
			WorkerType,
			DomainId,
			(SELECT Name FROM WorkItemStatuses WHERE Id = StatusId) as Status,
			CreatedAt
		FROM dbo.WorkItems 
		WHERE WorkerType = 'WebsiteCollector'
		ORDER BY CreatedAt DESC;
	END;
END;
```

---

## Bulk Registration (Multiple Items)

```sql
-- Register 10 work items for different selectors
DECLARE @PendingStatusId UNIQUEIDENTIFIER = (
	SELECT Id FROM dbo.WorkItemStatuses WHERE Name = 'PENDING'
);

DECLARE @Selectors TABLE (Id UNIQUEIDENTIFIER);

-- Get all selectors from Collector DB
INSERT INTO @Selectors
SELECT Id FROM [Collector].[dbo].WebsiteSelectors;

-- Register a work item for each selector
INSERT INTO dbo.WorkItems (
	Id,
	WorkerType,
	DomainId,
	DomainType,
	StatusId,
	AttemptCount,
	MaxAttempts,
	NextRunAt,
	CreatedAt,
	UpdatedAt
)
SELECT 
	NEWID(),                    -- Unique work item ID
	'WebsiteCollector',         -- Worker type
	s.Id,                       -- Domain ID (selector)
	'WebsiteSelector',          -- Domain type
	@PendingStatusId,           -- Status
	0,                          -- Attempts
	3,                          -- Max attempts
	GETUTCDATE(),               -- Ready now
	GETUTCDATE(),               -- Created
	GETUTCDATE()                -- Updated
FROM @Selectors s
WHERE NOT EXISTS (
	SELECT 1 FROM dbo.WorkItems wi
	WHERE wi.DomainId = s.Id 
	AND wi.WorkerType = 'WebsiteCollector'
	AND wi.StatusId IN (
		SELECT Id FROM dbo.WorkItemStatuses 
		WHERE Name IN ('PENDING', 'CLAIMED', 'RUNNING')
	)
);

-- Count how many were inserted
PRINT 'Registered new WorkItems for WebsiteCollector';
SELECT COUNT(*) as TotalQueued 
FROM dbo.WorkItems 
WHERE WorkerType = 'WebsiteCollector'
AND (SELECT Name FROM dbo.WorkItemStatuses WHERE Id = StatusId) = 'PENDING';
```

---

## Monitor Registration

```sql
-- View all registered WebsiteCollector items
SELECT 
	COUNT(*) as TotalItems,
	(SELECT Name FROM WorkItemStatuses WHERE Id = wi.StatusId) as Status,
	COUNT(*) as Count
FROM dbo.WorkItems wi
WHERE WorkerType = 'WebsiteCollector'
GROUP BY StatusId
ORDER BY StatusId;

-- Example output:
-- TotalItems | Status      | Count
-- -----------+-------------+-------
--     0      | PENDING     |   5      ← Ready to be claimed
--     2      | CLAIMED     |   2      ← Currently being processed
--     3      | SUCCEEDED   |   3      ← Successfully completed
--     1      | FAILED      |   1      ← Will retry
```

---

## Key Points

✅ **WorkerType MUST be exactly**: `'WebsiteCollector'`  
✅ **DomainId MUST reference**: WebsiteSelector.Id  
✅ **DomainType MUST be**: `'WebsiteSelector'`  
✅ **StatusId MUST be**: The Id of 'PENDING' status  
✅ **AttemptCount starts at**: 0  
✅ **MaxAttempts typical**: 3 or 5  
✅ **NextRunAt ready**: GETUTCDATE() (now)

---

## Next Steps

1. ✅ Create WebsiteSelector(s) in Collector DB
2. ✅ Register WorkItem(s) in ServiceBus DB (this script)
3. ✅ Verify tables have data (run monitoring query above)
4. ✅ Restart worker
5. ✅ Check logs for: `"ClaimAsync: WorkItemRepository returned X items"`
6. ✅ Worker should start processing!

---

## Testing the Registration

After running the script:

```sql
-- 1. Verify WorkItem was created
SELECT * FROM dbo.WorkItems 
WHERE WorkerType = 'WebsiteCollector'
ORDER BY CreatedAt DESC;

-- 2. Verify it can be claimed
EXEC dbo.ClaimWorkItems 
	@WorkerType = 'WebsiteCollector',
	@WorkerId = NEWID(),
	@BatchSize = 10,
	@LeaseSeconds = 300;

-- 3. Expected output: One or more rows with LeaseToken populated
```

If you see rows in step 2, your registration is successful! ✅
