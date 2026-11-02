# BEEP .NET Services Collector - Project Architecture

A multi-instance, distributed website collector built on .NET 10, SQL Server, and Dapper. Designed for horizontal scaling with lease-based distributed locking and no external message brokers.

---

## Project Overview

**beep_net_services_collector** is a background worker service that:
- Fetches website content from configured HTTPS URLs at scheduled intervals
- Extracts structured data via CSS selectors (HTML), JSONPath, or XPath (XML)
- Tracks collection executions with fault tolerance and automatic recovery
- Scales horizontally across multiple instances using database-level coordination
- Deduplicates unchanged responses via SHA-256 hashing

**Tech Stack:**
- .NET 10, C# 13
- SQL Server 2019+ with READ_COMMITTED_SNAPSHOT
- Dapper ORM (micro-ORM, no Entity Framework)
- AngleSharp for HTML parsing
- Microsoft.Extensions.Hosting for background service hosting

---

## Solution Structure

```
beep_net_services_collector/
├── src/
│   └── WebsiteCollector.Worker/           # Main executable worker service
│       ├── Program.cs                      # Dependency injection & app setup
│       ├── CollectorWorker.cs              # BackgroundService orchestrator
│       ├── appsettings.json                # Configuration defaults
│       ├── Dockerfile                      # Container image definition
│       ├── Features/                       # Domain-driven feature modules
│       │   ├── LeaseManagement/            # Distributed lease coordination
│       │   ├── SelectorManagement/         # Website selector lifecycle
│       │   └── WebsiteCollection/          # Core collection orchestration
│       └── Infrastructure/                 # Cross-cutting utilities
│           ├── Configuration/              # CollectorOptions settings
│           ├── Database/                   # DB connection management
│           ├── Events/                     # Domain event pub/sub
│           └── Http/                       # HttpClient factory & policies
├── sql/
│   ├── 000_unified_schema.sql             # Master schema with all tables & procedures
│   └── 001_webhooks_table.sql             # Webhook subscription schema
├── tests/
│   └── WebsiteCollector.Worker.Tests.Development/
│       └── Development test fixtures
├── docs/
│   └── README.md                           # Deployment & operational guide
└── beep_net_services_collector.slnx       # Solution file

```

---

## Architectural Layers

### 1. **Application Layer** (`CollectorWorker.cs`)
- **BackgroundService** that runs continuously within the host
- Manages concurrent collection tasks (configurable 1-256 workers)
- Polls database for due website selectors at configured interval
- Orchestrates the full lifecycle: claim → execute → complete
- Implements graceful shutdown via CancellationToken

**Key Responsibilities:**
- Work claiming via `LeaseManagementService`
- Content fetching with configurable timeouts and response size limits
- Calls `WebsiteCollectionService` to persist execution results
- Maps HTTP responses to CSS selector extractions

---

### 2. **Feature Modules** (`Features/`)

#### **2a. WebsiteCollection**
Core collection workflow:

```
Controllers:
  └── WebsiteCollectionService (IWebsiteCollectionService)
	  ├── ClaimSelectorsAsync()     → Reserve due work items
	  ├── StartExecutionAsync()     → Log execution start
	  ├── RenewLeaseAsync()         → Extend work claim (for long tasks)
	  └── CompleteExecutionAsync()  → Record result, schedule next

Repositories:
  ├── WebsiteCollectionRepository
  │   └── Executes stored procedures for work claiming & completion
  ├── WebsiteSelectionWebhookRepository
  │   └── Manages webhook subscriptions (future webhook notifications)
  └── IWebsiteCollectionRepository (interface)

Models:
  ├── WebsiteCollectionModels
  │   ├── ClaimedWebsiteSelector  → DTO for claimed work
  │   └── CollectionResult → Execution outcome
  ├── WebhookModels              → Webhook delivery contracts
  └── CollectionCompletedEvent   → Domain event on completion

Subscribers:
  └── WebsiteSelectorsSubscriber
	  └── Listens to CollectionCompletedEvent for side effects
```

**Data Flow:**
1. CollectorWorker → ClaimSelectorsAsync (via repository + stored procedure)
2. Worker fetches from URL, extracts via selector
3. Worker → CompleteExecutionAsync (via repository + stored procedure)
4. Completion event published via IEventAggregator
5. Subscribers notified asynchronously

#### **2b. LeaseManagement**
Distributed coordination for multi-instance safety:

```
Services:
  └── LeaseManagementService
	  ├── AcquireLease()    → Lock a website selector
	  ├── ValidateLease()   → Verify claim still valid
	  └── ReleaseLease()    → Release lock on completion

Models:
  └── LeaseModels
	  ├── LeaseOwner        → Worker ID
	  ├── LeaseToken        → Unique claim identifier
	  └── LeaseUntil        → Expiry timestamp (UTC)
```

**Mechanism:**
- SQL Server stored procedure `ClaimWebsiteSelectors` uses `UPDLOCK` and `READPAST` hints
- Each claim is unique: (LeaseOwner, LeaseToken, ExpiryTime)
- Lease expiry enables automatic recovery on worker crash
- All state modifications fenced by lease validity check

#### **2c. SelectorManagement**
Website selector configuration and lifecycle:

```
Services:
  └── SelectorManagementService
	  ├── GetSelectorsAsync()     → Query configured selectors
	  ├── EnableSelectorAsync()   → Activate monitoring
	  └── DisableSelectorAsync()  → Pause collection

Models:
  └── Selector
	  ├── ContentSelector  → CSS/XPath expression for extraction
	  ├── TableSelector    → (Future) XPath for table rows
	  ├── IntervalSeconds  → Collection frequency (10s - 30d)
	  ├── CollectionType   → HTML | JSON | XML | RAW
	  └── CollectionUrl    → Fetch endpoint
```

---

### 3. **Infrastructure Layer** (`Infrastructure/`)

#### **3a. Database**
```
Database.cs
├── Manages SqlConnection pooling
├── Executes raw SQL via Dapper
└── No ORM, direct query control for performance
```

#### **3b. Configuration**
```
CollectorOptions
├── Concurrency              → Parallel tasks (1-256)
├── LeaseSeconds             → Claim expiry (60-3600s)
├── PollSeconds              → Check interval (≥1s)
├── MaxResponseBytes         → Response limit
└── AllowedHosts[]           → Whitelist for HTTPS fetches
```

#### **3c. Events**
```
Interfaces:
  ├── IEvent              → Marker for domain events
  ├── IDomainEvent        → Domain event base
  └── IEventAggregator    → Pub/sub broker

Implementations:
  ├── DomainEvent         → Base for all domain events
  ├── EventAggregator     → In-memory pub/sub
  └── CollectionCompletedEvent
	  └── Fired after successful/failed execution
```

#### **3d. Http**
```
HttpClientConfiguration
├── AddCollectorHttpClient()
├── Configures timeout policies
├── Enforces max response size
└── Handles HTTPS host validation
```

---

## Database Schema

### Core Tables

| Table | Purpose |
|-------|---------|
| `Websites` | Master website records (URL, enabled flag) |
| `WebsiteSelectors` | Collection endpoints & extraction rules (1:N with Websites) |
| `WebsiteSelectorsMonitor` | Monitoring state (1:1 with WebsiteSelectors) |
| `WebsiteExecutions` | Execution audit log (1:N with WebsiteSelectors) |
| `CollectedData` | Deduped response bodies via SHA-256 |

### Key Indexes

- **IX_WebsiteSelectors_Website**: Filter on enabled, cluster by website
- **IX_WebsiteSelectorsMonitor_Due**: Unclaimed items sorted by NextRunAt
- **IX_Exec_WebsiteSelectorsTime**: Search executions by selector + timestamp

### Critical Stored Procedures

| Procedure | Purpose |
|-----------|---------|
| `ClaimWebsiteSelectors` | Acquire due work with isolation hints |
| `CompleteWebsiteSelector` | Record result, schedule next run, publish event |
| `RenewWebsiteSelectorLease` | Extend claim for long tasks |
| `StartWebsiteSelectorExecution` | Log execution start |

---

## Data Flow Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│ CollectorWorker (BackgroundService)                             │
├─────────────────────────────────────────────────────────────────┤
│ 1. START LOOP                                                   │
│    ↓                                                             │
│ 2. ClaimSelectorsAsync()                                        │
│    ├→ IWebsiteCollectionRepository.ClaimAsync()                 │
│    └→ SQL: ClaimWebsiteSelectors (with UPDLOCK, READPAST)      │
│       Returns: List<ClaimedWebsiteSelector>                     │
│    ↓                                                             │
│ 3. FOR EACH claimed selector                                    │
│    ├→ HttpClient.GetAsync(selector.CollectionUrl)              │
│    ├→ Parse response per CollectionType (HTML/JSON/XML)        │
│    ├→ Extract via ContentSelector (CSS/XPath)                  │
│    ├→ Compute SHA256 hash of raw                               │
│    ↓                                                             │
│ 4. CompleteExecutionAsync()                                     │
│    ├→ IWebsiteCollectionRepository.CompleteAsync()              │
│    └→ SQL: CompleteWebsiteSelector                             │
│       ├ Insert to WebsiteExecutions (audit)                     │
│       ├ Insert/update CollectedData (if changed)                │
│       ├ Update WebsiteSelectorsMonitor.NextRunAt                │
│       ├ Publish CollectionCompletedEvent                        │
│       └ Validate LeaseToken & LeaseUntil                        │
│    ↓                                                             │
│ 5. IEventAggregator.PublishAsync()                              │
│    └→ WebsiteSelectorsSubscriber receives event                │
│       (future: deliver webhooks, trigger alerts)                │
│    ↓                                                             │
│ 6. WAIT PollSeconds, REPEAT or GRACEFUL SHUTDOWN                │
└─────────────────────────────────────────────────────────────────┘
```

---

## Concurrency & Distributed Locking

**Problem:** Multiple worker instances fetching the same website simultaneously.

**Solution:** Database-level pessimistic locking via SQL Server stored procedures.

**Mechanism:**
```sql
-- Simplified claim logic
SELECT TOP(@count)
	Id, LeaseToken, ...
FROM dbo.WebsiteSelectorsMonitor
WITH (UPDLOCK, READPAST)  -- Skip locked rows, exclusive lock acquired rows
WHERE NextRunAt <= SYSUTCDATETIME()
	  AND LeaseOwner IS NULL  -- Not currently leased
ORDER BY NextRunAt;
-- Update LeaseOwner, LeaseToken, LeaseUntil in same transaction
```

**Key Benefits:**
1. **No external broker** (Kafka, RabbitMQ) needed
2. **Atomic claim & update** in single transaction
3. **READPAST** skips in-flight claims across workers
4. **Lease expiry** auto-recovers from crashed workers
5. **Conflict-free** via UPDLOCK + unique LeaseToken

---

## Error Handling & Retry Logic

**Execution Lifecycle:**
```
RUNNING
  ├─ Success (2xx HTTP)      → NextRunAt += IntervalSeconds
  ├─ Transient Error (5xx)   → Exponential backoff, AttemptCount++
  │  └─ After MaxAttempts    → Reset, schedule normal interval
  └─ Permanent Error (4xx)   → Log, NextRunAt += IntervalSeconds
	 (or connection error)

FAILED
  └─ Max retries exceeded    → Mark as failed, schedule reset

ABANDONED
  └─ Lease expired while running → New worker claims & retries
```

**Bounded Exponential Backoff:**
```
Delay = Min(BaseDelay × 2^AttempptCount, MaxDelay)
```

---

## Configuration & Environment

**Required Environment Variables:**
```powershell
# SQL Server connection
$env:ConnectionStrings__Default = "Server=.;Initial Catalog=WebsiteCollector;Integrated Security=true"

# Worker configuration
$env:Collector__AllowedHosts__0 = "example.com"
$env:Collector__AllowedHosts__1 = "api.example.com"
$env:Collector__Concurrency = "10"      # Parallel tasks
$env:Collector__LeaseSeconds = "300"    # 5min locks
$env:Collector__PollSeconds = "5"       # Poll every 5s
$env:Collector__MaxResponseBytes = "10485760"  # 10 MB
```

**appsettings.json Defaults:**
```json
{
  "Logging": {
	"LogLevel": { "Default": "Information" }
  },
  "Collector": {
	"Concurrency": 10,
	"LeaseSeconds": 300,
	"PollSeconds": 5,
	"MaxResponseBytes": 10485760,
	"AllowedHosts": []
  }
}
```

---

## Deployment & Scaling

### Single Instance
```bash
cd src/WebsiteCollector.Worker
dotnet restore
dotnet build -c Release
dotnet run
```

### Multi-Instance (Same Database)
```
Host A: dotnet run
Host B: dotnet run
Host C: dotnet run
```
Each worker gets a unique `Guid.NewGuid()` ID. Database coordinate work via lease mechanism.

### Containerized (Kubernetes)
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: collector
spec:
  replicas: 3
  template:
	spec:
	  containers:
	  - name: collector
		image: myrepo/collector:latest
		env:
		- name: ConnectionStrings__Default
		  valueFrom:
			secretKeyRef:
			  name: db-secret
			  key: connection-string
		- name: Collector__AllowedHosts__0
		  value: "example.com"
```

---

## Resilience Patterns

| Pattern | Mechanism |
|---------|-----------|
| **Lease Expiry** | Crashed workers auto-recovered when lease expires |
| **Exponential Backoff** | Transient failures retry with increasing delays |
| **Idempotency** | Lease token gates all state mutations |
| **Deduplication** | SHA-256 prevents duplicate storage of unchanged bodies |
| **Graceful Shutdown** | CancellationToken cancels in-flight work, allows cleanup |
| **Isolation Hints** | UPDLOCK + READPAST prevent reader/writer conflicts |

---

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| **No Entity Framework** | Dapper provides direct SQL control, essential for custom locking logic |
| **No Message Broker** | SQL Server (exists anyway) handles orchestration; simpler ops |
| **Pessimistic Locking** | Guarantees conflict-free distribution; database is SSOT |
| **In-Memory EventAggregator** | Publish-subscribe within process; webhooks for external integrations |
| **HTTPS Only** | Prevents MITM and SSRF; configurable host allowlist for further security |
| **MaxResponseBytes** | Protects memory; unbounded responses can cause OOM |
| **Deduplication by Hash** | Reduces storage for stable websites; audit trail via ExecutionLog |

---

## Testing

**Tests Project:** `tests/WebsiteCollector.Worker.Tests.Development/`

**Test Scope (future enhancements):**
- Distributed claim logic under concurrent load
- Lease expiry recovery scenarios
- Extraction accuracy for HTML/JSON/XML
- Duplicate detection (SHA-256 collisions)
- Graceful shutdown with in-flight work

---

## Security Considerations

1. **HTTPS Only**: Redirects disabled, host whitelist required
2. **Secrets Management**: Connection strings & credentials in environment, never source
3. **Rate Limiting**: Respect upstream limits via Concurrency tuning
4. **Input Validation**: CSS selectors, URLs validated before execution
5. **SQL Injection Prevention**: Parameterized queries via Dapper
6. **Egress Filtering**: Private/loopback IPs blocked at firewall for multi-tenant setups

---

## Operational Monitoring

**Key Metrics to Track:**
- **Claim Success Rate**: % selectors claimed vs. due
- **Execution Duration**: P50/P95/P99 collection time
- **Failure Rate**: % executions failed by reason (timeout, 4xx, 5xx)
- **Lease Expiry**: Count of auto-recovered abandoned executions
- **Data Deduplication Ratio**: % unchanged responses
- **Database Lock Contention**: sp_who2 / sys.dm_exec_requests

**Logs to Monitor:**
- Worker startup/shutdown events
- Collection errors (HTTP status, network timeouts)
- Lease disputes (conflicts during claim)
- Graceful shutdown progress (outstanding tasks)

---

## Future Enhancements

- [ ] JSONPath extraction (beyond JSON validation)
- [ ] XPath for XML documents
- [ ] Multi-value CSS selectors (table row extraction)
- [ ] Webhook delivery on completion
- [ ] Scheduled executions (cron-like)
- [ ] Response body retention policies (archive old data)
- [ ] Distributed tracing (OpenTelemetry)
- [ ] Circuit breaker for consistently failing hosts
- [ ] Authentication support (Basic, Bearer tokens)
- [ ] Proxy server support

---

## References

- [Microsoft.Extensions.Hosting](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.hosting)
- [Dapper ORM](https://github.com/DapperLib/Dapper)
- [AngleSharp HTML Parser](https://anglesharp.github.io/)
- [SQL Server Isolation Levels](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-transaction-isolation-level-transact-sql)
- [Optimistic vs. Pessimistic Locking](https://en.wikipedia.org/wiki/Optimistic_concurrency_control)

---

## License & Repository

**Repository:** https://github.com/tram-gelsthorpe/beep_net_services_collector

**Branch:** main

**Latest Build Status:** ✅ Compiling against .NET 10
