# WebsiteCollector.Worker Resource - Database Migration Scripts

This folder contains SQL migration scripts for setting up the WebsiteCollector.Worker database schema.

## Architecture Overview

The WebsiteCollector.Worker uses a **No-Code-Duplication Strategy**:

```
Beep.WebhookBus (Generic, Reusable)
	↓ Used by
WebsiteCollector.Worker
	├── 0_website_selectors_schema.sql     (Core WebsiteSelectors tables)
	└── 1_webhook_integration.sql          (Project-specific webhook mapping)
```

## Migration Files

### 0_website_selectors_schema.sql
**Purpose:** Creates the core WebsiteSelectors base schema

**Tables Created:**
- `dbo.Websites` - Website configurations
- `dbo.WebsiteSelectors` - Collection endpoints with selectors
- `dbo.WebsiteSelectorsMonitor` - Execution scheduling and leasing
- `dbo.WebsiteSelectorsExecutions` - Individual execution records
- `dbo.WebsiteSelectorsCollectedData` - Collected content and responses

**Execution Order:** Run first (no dependencies on other migrations)

**Dependencies:** None - This is the foundation

---

### 1_webhook_integration.sql
**Purpose:** Integrates Beep.WebhookBus webhooks with WebsiteSelectors

**Tables Created:**
- `dbo.WebsiteSelectorsWebhooks` - Links webhooks to WebsiteSelectors
- `dbo.WebsiteSelectorsWebhookMonitor` - Webhook delivery health tracking
- `dbo.WebsiteSelectorsWebhookDeliveries` - Records webhook delivery attempts

**Execution Order:** Run second

**Dependencies:**
- Requires Beep.WebhookBus base schema
- Requires `0_website_selectors_schema.sql` to be executed first

**Key Design Decisions:**
- Does NOT duplicate Beep.WebhookBus tables
- Creates project-specific tables that follow Beep.WebhookBus patterns
- Foreign keys to WebsiteSelectors (not to generic Webhooks table)
- Webhook data flows: `WebsiteSelectors` → `WebsiteSelectorsWebhooks` → `WebsiteSelectorsWebhookDeliveries`

---

## Deployment Sequence

```sql
-- Step 1: Deploy Beep.WebhookBus generic schema
-- (handled by Beep.WebhookBus package/migrations)

-- Step 2: Deploy WebsiteCollector.Worker core schema
EXEC sp_executesql N'
<-- contents of 0_website_selectors_schema.sql -->
'

-- Step 3: Deploy webhook integration
EXEC sp_executesql N'
<-- contents of 1_webhook_integration.sql -->
'
```

## Schema Relationships

### Data Flow: Collection to Delivery

```
Website
  ↓
WebsiteSelector
  ├→ WebsiteSelectorsMonitor (scheduling)
  └→ WebsiteSelectorsExecutions
	   └→ WebsiteSelectorsCollectedData
			└→ WebsiteSelectorsWebhookDeliveries
				 ├→ WebsiteSelectorsWebhooks
				 └→ WebsiteSelectorsWebhookMonitor (delivery health)
```

### Key Foreign Keys

| Table | References | Purpose |
|-------|-----------|---------|
| WebsiteSelectors | Websites | Selectors belong to websites |
| WebsiteSelectorsMonitor | WebsiteSelectors | Scheduling for each selector |
| WebsiteSelectorsExecutions | WebsiteSelectors | Execution history |
| WebsiteSelectorsCollectedData | WebsiteSelectorsExecutions | Data collected during execution |
| WebsiteSelectorsWebhooks | WebsiteSelectors | Webhooks for each selector |
| WebsiteSelectorsWebhookDeliveries | WebsiteSelectorsWebhooks, WebsiteSelectorsCollectedData | Delivery tracking |

---

## No Code Duplication Strategy

This project implements a **No-Code-Duplication Strategy** where:

1. **Beep.WebhookBus** provides the generic webhook architecture
   - Generic table structure
   - Reusable patterns for retry logic, status tracking, monitoring
   - Can be used by ANY project that needs webhooks

2. **WebsiteCollector.Worker** implements project-specific tables
   - Never duplicates Beep.WebhookBus code
   - Follows Beep.WebhookBus patterns and conventions
   - Uses foreign keys to tie webhooks to WebsiteSelectors
   - Can be replicated as a template for other projects (Order Service, etc.)

3. **Benefits:**
   - ✅ Webhook logic in one place (Beep.WebhookBus)
   - ✅ Project-specific customization without duplication
   - ✅ Easy to extend to other projects
   - ✅ Reduces maintenance burden

---

## Related Code Components

- **WebhookDeliveryRepository:** `Features/WebhookDelivery/Repositories/WebhookDeliveryRepository.cs`
  - Uses WebsiteSelectorsWebhookDeliveries table
  - Reference implementation for other projects

- **WebhookDeliveryService:** `Features/WebhookDelivery/Services/WebhookDeliveryService.cs`
  - Business logic for webhook processing

- **DeliveryWorkProcessor:** `Features/WebhookDelivery/Adapters/DeliveryWorkProcessor.cs`
  - Handles webhook execution pipeline

---

## Implementation Notes

- All tables use `uniqueidentifier` with `DEFAULT NEWID()` for primary keys
- Timestamps use `datetimeoffset(7)` with `DEFAULT SYSUTCDATETIME()` for timezone awareness
- Foreign key constraints cascade on delete for referential integrity
- Delivery status is constrained to valid values: `PENDING`, `IN_PROGRESS`, `SUCCESS`, `FAILED`, `ABANDONED`
- Leasing and work management uses row versioning for concurrency control
- Indexes are optimized for common query patterns (pending deliveries, webhook history, etc.)

---

## Extending to Other Projects

To use webhooks in another project (e.g., Order Service):

1. Deploy Beep.WebhookBus generic schema
2. Create your project's core tables (similar to `0_website_selectors_schema.sql`)
3. Create webhook integration (similar to `1_webhook_integration.sql`)
4. Update your repository classes to reference your project-specific webhook tables
5. Reuse the webhook delivery logic from WebsiteCollector.Worker as a template

This ensures **zero code duplication** while maintaining project-specific customization.
