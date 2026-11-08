# WebsiteCollector.Worker Resource - Database Migration Scripts

This folder contains SQL migration scripts for setting up the WebsiteCollector.Worker database schema.

## Architecture Overview

The WebsiteCollector.Worker uses a **Generic Webhook Schema Strategy** with semantic scoping:

```
Beep.WebhookBus (Generic, Reusable, Single Source of Truth)
		 ↓
WebsiteCollector.Worker
   └── 0_website_selectors_schema.sql (Domain tables only, no webhook duplication)
```

**Key Principle:** All webhook tables come from Beep.WebhookBus. WebsiteCollector.Worker only creates domain-specific tables (Websites, WebsiteSelectors, etc.).

## Migration Files

### 0_website_selectors_schema.sql
**Purpose:** Creates the WebsiteSelectors domain schema (core business entities)

**Tables Created:**
- `dbo.Websites` - Website configurations
- `dbo.WebsiteSelectors` - Collection endpoints with selectors (CSS, XPath, JSON paths)
- `dbo.WebsiteSelectorsMonitor` - Execution scheduling, leasing, and retry state
- `dbo.WebsiteSelectorsExecutions` - Individual execution records with status and errors
- `dbo.WebsiteSelectorsCollectedData` - Collected content, hash tracking, and extracted data

**Execution Order:** Run first

**Dependencies:** None - This is the foundation

**Does NOT Create:**
- ❌ Webhook tables (use Beep.WebhookBus instead)
- ❌ Webhook delivery tables (use Beep.WebhookBus instead)
- ❌ Webhook monitor tables (use Beep.WebhookBus instead)

---

## Webhook Integration Pattern

Webhooks are managed entirely through **Beep.WebhookBus generic tables**:

```sql
-- Generic tables (from Beep.WebhookBus)
dbo.Webhooks                  -- Contains WebhooksOwnerId (references WebsiteSelectors.Id)
dbo.WebhookMonitor            -- Health tracking and circuit breaker state
dbo.WebhookDeliveries         -- Contains WebhooksOwnerCollectedDataId (references WebsiteSelectorsCollectedData.Id)
```

**Data Flow:**
```
WebsiteSelector (Id)
	↓ (referenced as)
dbo.Webhooks.WebhooksOwnerId
	↓
dbo.WebhookDeliveries
	↓ (references)
WebsiteSelectorsCollectedData.Id (via WebhooksOwnerCollectedDataId)
```

**Benefits:**
- ✅ Zero table duplication across projects
- ✅ Single webhook infrastructure
- ✅ Reusable across WebsiteCollector.Worker, Order Service, and other projects
- ✅ Easy to reason about and maintain

---

## Schema Relationships

### Data Flow: Collection to Delivery

```
Website (root)
  ↓
WebsiteSelector
  ├→ WebsiteSelectorsMonitor (scheduling via worker framework)
  ├→ WebsiteSelectorsExecutions (execution attempts)
  │   └→ WebsiteSelectorsCollectedData (collected content)
  │       └→ dbo.WebhookDeliveries* (delivery attempts via Beep.WebhookBus)
  │           ├→ dbo.Webhooks* (endpoint configs via Beep.WebhookBus)
  │           └→ dbo.WebhookMonitor* (delivery health via Beep.WebhookBus)
  └→ Webhooks via dbo.Webhooks.WebhooksOwnerId* (Beep.WebhookBus)

* Provided by Beep.WebhookBus (generic schema)
```

### Key Foreign Keys

| Table | References | Purpose |
|-------|-----------|---------|
| WebsiteSelectors | Websites | Selectors belong to websites |
| WebsiteSelectorsMonitor | WebsiteSelectors | Scheduling for each selector |
| WebsiteSelectorsExecutions | WebsiteSelectors | Execution history |
| WebsiteSelectorsCollectedData | WebsiteSelectorsExecutions | Data collected during execution |
| dbo.Webhooks* | (none) | Generic webhook storing WebhooksOwnerId |
| dbo.WebhookDeliveries* | dbo.Webhooks, WebsiteSelectorsCollectedData (via WebhooksOwnerCollectedDataId) | Delivery tracking |

*From Beep.WebhookBus

---

## Generic Schema with Semantic Scoping

This architecture uses **Generic Schema with Semantic Scoping**:

1. **Generic Tables** (Beep.WebhookBus):
   - `dbo.Webhooks` with generic `WebhooksOwnerId` and `WebhookUrl` columns
   - `dbo.WebhookDeliveries` with generic `WebhooksId` and `WebhooksOwnerCollectedDataId` columns
   - `dbo.WebhookMonitor` with generic monitoring state

2. **Semantic Scoping** (WebhookDeliveryRepository):
   ```csharp
   // Repository knows the context:
   // - WebhooksOwnerId always references WebsiteSelectors.Id
   // - WebhooksOwnerCollectedDataId always references WebsiteSelectorsCollectedData.Id

   WHERE d.WebhooksId = @WebhookId
	 AND w.IsEnabled = 1
	 AND d.Status IN ('PENDING', 'IN_PROGRESS')
   ```

3. **Reusability**:
   - Order Service uses the same Beep.WebhookBus tables
   - WebhooksOwnerId in Order Service references Orders.Id
   - WebhooksOwnerCollectedDataId in Order Service references OrderData.Id
   - No schema duplication needed

---

## Deployment Sequence

```sql
-- Step 1: Deploy Beep.WebhookBus generic schema
-- (handled by Beep.WebhookBus package/migrations)

-- Step 2: Deploy WebsiteCollector.Worker domain schema
EXEC sp_executesql N'
<-- contents of 0_website_selectors_schema.sql -->
'
```

That's it! No separate webhook migration needed in WebsiteCollector.Worker.

---

## Related Code Components

- **WebhookDeliveryRepository:** `Features/WebhookDelivery/Repositories/WebhookDeliveryRepository.cs`
  - Uses generic dbo.Webhooks and dbo.WebhookDeliveries from Beep.WebhookBus
  - Scopes queries to WebsiteSelectors context (WHERE WebhooksId = @WebhookId)
  - Reference implementation for other projects

- **WebhookDeliveryService:** `Features/WebhookDelivery/Services/WebhookDeliveryService.cs`
  - Business logic for webhook HTTP delivery and retry handling

- **DeliveryWorkProcessor:** `Features/WebhookDelivery/Adapters/DeliveryWorkProcessor.cs`
  - Handles webhook execution pipeline (claim, deliver, update state)

---

## Implementation Notes

- All tables use `uniqueidentifier` with `DEFAULT NEWID()` for primary keys
- Timestamps use `datetimeoffset(7)` with `DEFAULT SYSUTCDATETIME()` for timezone awareness
- Foreign key constraints cascade on delete for referential integrity
- `dbo.Webhooks.WebhooksOwnerId` is nullable to support both scoped and global webhooks
- `dbo.WebhookDeliveries.WebhooksOwnerCollectedDataId` is NOT NULL (always references collected data)
- Delivery status is constrained to valid values: `PENDING`, `IN_PROGRESS`, `SUCCESS`, `FAILED`, `ABANDONED`
- Leasing and work management uses row versioning for concurrency control
- Indexes are optimized for common query patterns (pending deliveries, webhook history, etc.)

---

## Extending to Other Projects

### Adding webhooks to Order Service:

1. **Create Order Service domain schema** (similar structure):
   ```sql
   -- Orders, OrderMonitor, OrderExecutions, OrderData
   ```

2. **No additional webhook tables needed!**
   - Reuse `dbo.Webhooks` from Beep.WebhookBus
   - Reuse `dbo.WebhookDeliveries` from Beep.WebhookBus
   - Set `WebhooksOwnerId = Orders.Id`
   - Set `WebhooksOwnerCollectedDataId = OrderData.Id`

3. **Update Order Service repositories**:
   ```csharp
   // Similar to WebsiteCollector.Worker
   WHERE d.WebhooksId = @OrderWebhookId
	 AND w.IsEnabled = 1
   ```

This ensures **zero code duplication** while maintaining project-specific customization and semantic clarity.

