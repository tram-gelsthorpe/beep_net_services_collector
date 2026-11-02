# File Structure - beep_net_services_collector

Complete file organization and descriptions for the project.

---

## Directory Tree

```
beep_net_services_collector/
│
├── 📄 README.md                              # Project architecture & design overview
├── 📄 FILE_STRUCTURE.md                      # This file - project file organization
├── 📄 beep_net_services_collector.slnx       # Visual Studio solution file (.NET 10)
├── 📄 .gitignore                             # Git ignore rules
│
├── 📁 src/
│   └── 📁 WebsiteCollector.Worker/           # Main executable worker service
│       ├── 📄 WebsiteCollector.Worker.csproj # Project file (OutputType=Exe)
│       ├── 📄 Program.cs                     # Dependency injection & app entry point (top-level statements)
│       ├── 📄 CollectorWorker.cs             # BackgroundService orchestrator (main polling loop)
│       ├── 📄 appsettings.json               # Configuration defaults (logging, collector settings)
│       ├── 📄 Dockerfile                     # Docker container image definition
│       │
│       ├── 📁 Features/                      # Domain-driven feature modules
│       │   ├── 📁 WebsiteCollection/         # Core collection orchestration feature
│       │   │   ├── 📁 Events/
│       │   │   │   └── 📄 CollectionCompletedEvent.cs     # Domain event after execution completes
│       │   │   │
│       │   │   ├── 📁 Models/
│       │   │   │   ├── 📄 WebsiteCollectionModels.cs      # DTOs: ClaimedWebsiteSelector, CollectionResult
│       │   │   │   └── 📄 WebhookModels.cs                # DTOs: WebhookSubscription, WebhookDelivery
│       │   │   │
│       │   │   ├── 📁 Repositories/
│       │   │   │   ├── 📄 IWebsiteCollectionRepository.cs  # Interface: contract for data access
│       │   │   │   ├── 📄 WebsiteCollectionRepository.cs   # Implementation: executes stored procedures
│       │   │   │   └── 📄 WebsiteSelectionWebhookRepository.cs # Webhook subscription persistence
│       │   │   │
│       │   │   ├── 📁 Services/
│       │   │   │   └── 📄 WebsiteCollectionService.cs      # Service: business logic for work claiming & completion
│       │   │   │
│       │   │   └── 📁 Subscribers/
│       │   │       ├── 📄 ISubscriber.cs                   # Interface: event subscriber contract
│       │   │       └── 📄 WebsiteSelectorsSubscriber.cs    # Listens to CollectionCompletedEvent
│       │   │
│       │   ├── 📁 LeaseManagement/          # Distributed lease coordination feature
│       │   │   ├── 📁 Models/
│       │   │   │   └── 📄 LeaseModels.cs                   # DTOs: LeaseOwner, LeaseToken, LeaseUntil
│       │   │   │
│       │   │   └── 📁 Services/
│       │   │       └── 📄 LeaseManagementService.cs        # Service: acquire, renew, release leases
│       │   │
│       │   └── 📁 SelectorManagement/      # Website selector configuration feature
│       │       ├── 📁 Models/
│       │       │   └── 📄 Selector.cs                      # DTO: ContentSelector, TableSelector, CollectionType, IntervalSeconds
│       │       │
│       │       └── 📁 Services/
│       │           └── 📄 SelectorManagementService.cs     # Service: query, enable/disable selectors
│       │
│       ├── 📁 Infrastructure/               # Cross-cutting infrastructure utilities
│       │   ├── 📁 Configuration/
│       │   │   └── 📄 CollectorOptions.cs                  # Configuration class: deserialized from appsettings
│       │   │                                               # Properties: Concurrency, LeaseSeconds, PollSeconds, MaxResponseBytes, AllowedHosts
│       │   │
│       │   ├── 📁 Database/
│       │   │   └── 📄 Database.cs                          # Database connection manager (SqlConnection pooling)
│       │   │
│       │   ├── 📁 Events/
│       │   │   ├── 📄 IEvent.cs                            # Marker interface for all events
│       │   │   ├── 📄 IDomainEvent.cs                      # Base interface for domain events
│       │   │   ├── 📄 DomainEvent.cs                       # Base class: id, timestamp, source
│       │   │   ├── 📄 IEventAggregator.cs                  # Interface: pub/sub broker contract
│       │   │   └── 📄 EventAggregator.cs                   # Implementation: in-memory pub/sub (Dictionary<Type, List<>)
│       │   │
│       │   └── 📁 Http/
│       │       └── 📄 HttpClientConfiguration.cs           # Extension: AddCollectorHttpClient()
│       │                                                   # Configures timeout, max response size, host validation
│       │
│       └── 📄 .editorconfig                 # EditorConfig for consistent formatting (spaces, line length, etc.)
│
├── 📁 sql/                                  # Database schema & stored procedures
│   ├── 📄 000_unified_schema.sql            # Master schema definition
│   │   ├── Websites table (master website records)
│   │   ├── WebsiteSelectors table (collection endpoints & selectors)
│   │   ├── WebsiteSelectorsMonitor table (monitoring state: leases, next run time)
│   │   ├── WebsiteExecutions table (audit log of all executions)
│   │   ├── CollectedData table (deduped response bodies via SHA256)
│   │   ├── Stored procedures:
│   │   │   ├── ClaimWebsiteSelectors (claim work with UPDLOCK, READPAST)
│   │   │   ├── CompleteWebsiteSelector (record result, schedule next, publish event)
│   │   │   ├── RenewWebsiteSelectorLease (extend claim for long tasks)
│   │   │   └── StartWebsiteSelectorExecution (log execution start)
│   │   └── Indexes:
│   │       ├── IX_WebsiteSelectors_Website (filtered, IsEnabled=1)
│   │       ├── IX_WebsiteSelectorsMonitor_Due (NextRunAt, Id where LeaseOwner IS NULL)
│   │       └── IX_Exec_WebsiteSelectorsTime (WebsiteSelectorsId, StartedAt DESC)
│   │
│   └── 📄 001_webhooks_table.sql            # Webhook subscription schema (optional future feature)
│       ├── WebsiteSelectionWebhooks table
│       ├── WebhookDeliveries table (audit log)
│       └── Indexes for efficient lookups
│
├── 📁 tests/
│   └── 📁 WebsiteCollector.Worker.Tests.Development/
│       ├── 📄 WebsiteCollector.Worker.Tests.Development.csproj
│       └── 📄 [Test files - Development/spike test fixtures]
│
├── 📁 docs/
│   ├── 📄 README.md                         # **EXISTING** - Deployment & operational guide
│   ├── 📄 ARCHITECTURE.md                   # **NEW** - Detailed architecture & design patterns
│   └── 📄 FILE_STRUCTURE.md                 # **NEW** - This file - project file organization
│
└── 📄 .git/                                 # Git repository directory (local)

```

---

## File Descriptions

### Root Level

| File | Purpose |
|------|---------|
| `README.md` | Architecture overview, design decisions, deployment guide |
| `FILE_STRUCTURE.md` | This file - complete project organization |
| `beep_net_services_collector.slnx` | Visual Studio solution file (.slnx format, lists all projects) |
| `.gitignore` | Git ignore patterns (bin/, obj/, .vs/, etc.) |

---

### `src/WebsiteCollector.Worker/` - Main Application

#### Root Files

| File | Purpose | Type |
|------|---------|------|
| `WebsiteCollector.Worker.csproj` | MSBuild project file; defines dependencies, framework, output type (Exe) | Config |
| `Program.cs` | Entry point (top-level statements); DI setup, host builder, event subscriber initialization | Code |
| `CollectorWorker.cs` | BackgroundService implementation; main polling loop, work claiming, execution orchestration | Code |
| `appsettings.json` | Configuration defaults; logging levels, Collector section with defaults | Config |
| `Dockerfile` | Docker image definition; FROM mcr.microsoft.com/dotnet:10-runtime-alpine | Config |
| `.editorconfig` | EditorConfig rules for code formatting consistency | Config |

---

### `Features/WebsiteCollection/` - Core Collection Logic

**Role:** Orchestrates website content fetching, extraction, and result persistence.

#### Events/
| File | Purpose |
|------|---------|
| `CollectionCompletedEvent.cs` | Domain event fired after execution completes (success or failure); consumed by subscribers |

#### Models/
| File | Purpose |
|------|---------|
| `WebsiteCollectionModels.cs` | DTOs: `ClaimedWebsiteSelector` (work item), `CollectionResult` (outcome) |
| `WebhookModels.cs` | DTOs: `WebhookSubscription`, `WebhookDelivery` (future webhook feature) |

#### Repositories/
| File | Purpose |
|------|---------|
| `IWebsiteCollectionRepository.cs` | Interface defining contract for data access (claim, start, renew, complete) |
| `WebsiteCollectionRepository.cs` | Dapper-based implementation; calls stored procedures `ClaimWebsiteSelectors`, `CompleteWebsiteSelector`, etc. |
| `WebsiteSelectionWebhookRepository.cs` | Webhook subscription persistence (future use) |

#### Services/
| File | Purpose |
|------|---------|
| `WebsiteCollectionService.cs` | Business logic: `ClaimSelectorsAsync()`, `StartExecutionAsync()`, `CompleteExecutionAsync()`, `RenewLeaseAsync()` |

#### Subscribers/
| File | Purpose |
|------|---------|
| `ISubscriber.cs` | Interface: `RegisterAsync(CancellationToken)` for event subscribers |
| `WebsiteSelectorsSubscriber.cs` | Implements `ISubscriber`; listens to `CollectionCompletedEvent` (future: webhook delivery, alerts) |

---

### `Features/LeaseManagement/` - Distributed Coordination

**Role:** Manages distributed leases for pessimistic locking across worker instances.

#### Models/
| File | Purpose |
|------|---------|
| `LeaseModels.cs` | DTOs: `LeaseOwner` (worker ID), `LeaseToken` (unique claim), `LeaseUntil` (expiry) |

#### Services/
| File | Purpose |
|------|---------|
| `LeaseManagementService.cs` | Service: `AcquireLease()`, `ValidateLease()`, `ReleaseLease()` |

---

### `Features/SelectorManagement/` - Selector Configuration

**Role:** Manages website selector lifecycle (query, enable, disable).

#### Models/
| File | Purpose |
|------|---------|
| `Selector.cs` | DTO: `ContentSelector`, `TableSelector`, `CollectionType` (HTML/JSON/XML/RAW), `IntervalSeconds` |

#### Services/
| File | Purpose |
|------|---------|
| `SelectorManagementService.cs` | Service: `GetSelectorsAsync()`, `EnableSelectorAsync()`, `DisableSelectorAsync()` |

---

### `Infrastructure/` - Cross-Cutting Utilities

**Role:** Shared infrastructure for database, configuration, events, HTTP clients.

#### Configuration/
| File | Purpose |
|------|---------|
| `CollectorOptions.cs` | Configuration class; properties: `Concurrency`, `LeaseSeconds`, `PollSeconds`, `MaxResponseBytes`, `AllowedHosts[]` |

#### Database/
| File | Purpose |
|------|---------|
| `Database.cs` | SqlConnection manager; pooling, connection string from config |

#### Events/
| File | Purpose |
|------|---------|
| `IEvent.cs` | Marker interface for all events |
| `IDomainEvent.cs` | Base interface for domain events |
| `DomainEvent.cs` | Base class for all domain events; properties: `Id`, `Timestamp`, `Source` |
| `IEventAggregator.cs` | Interface: `PublishAsync<T>(T @event)`, `SubscribeAsync<T>(...)` |
| `EventAggregator.cs` | In-memory pub/sub implementation; `Dictionary<Type, List<Delegate>>` |

#### Http/
| File | Purpose |
|------|---------|
| `HttpClientConfiguration.cs` | Extension method: `AddCollectorHttpClient()`; configures timeout, retry, host validation |

---

### `sql/` - Database Schema & Procedures

#### 000_unified_schema.sql (296 lines)
Master schema definition containing:

**Tables:**
```
Websites
├── Id (PK, uniqueidentifier)
├── Name, Url, IsEnabled, CreatedAt, UpdatedAt

WebsiteSelectors
├── Id (PK, uniqueidentifier)
├── WebsitesId (FK → Websites)
├── CollectionUrl, CollectionType, ContentSelector, TableSelector
├── IntervalSeconds (10s - 30d), IsEnabled
├── Unique constraint: (WebsitesId, CollectionUrl, CollectionType, ContentSelector, TableSelector)

WebsiteSelectorsMonitor (1:1 with WebsiteSelectors)
├── Id (PK/FK → WebsiteSelectors)
├── NextRunAt, TimeoutSeconds, MaxAttempts, AttemptCount
├── LeaseOwner, LeaseToken, LeaseUntil (lease state)
├── LastStartedAt, LastSucceededAt, LastError
├── CreatedAt, RowVer (rowversion for optimistic concurrency if needed)

WebsiteExecutions (audit log)
├── Id (PK, uniqueidentifier)
├── WebsiteSelectorsId (FK → WebsiteSelectors)
├── LeaseToken (Unique), WorkerId, StartedAt, FinishedAt
├── Status (Running|Succeeded|Failed|Abandoned), AttemptNumber
├── HttpStatus, ErrorMessage

CollectedData (deduplication)
├── Id (PK, uniqueidentifier)
├── WebsiteSelectorsId (FK → WebsiteSelectors)
├── DataHash (SHA256, Unique), RawBody (NVARCHAR(MAX))
├── ExtractedFields (JSON or XML)
├── CreatedAt, LastSeenAt
```

**Stored Procedures:**
```
ClaimWebsiteSelectors(@WorkerId, @Count, @LeaseSeconds, @AllowedHosts)
  → SELECT TOP(@Count) ... WHERE NextRunAt <= SYSUTCDATETIME() AND LeaseOwner IS NULL
  → UPDLOCK, READPAST (skip locked, exclusive lock acquired)
  → Update: LeaseOwner, LeaseToken, LeaseUntil in same transaction

CompleteWebsiteSelector(@WebsiteSelectorsId, @LeaseToken, @LeaseUntil, @Success, @HttpStatus, @Error, @Hash, @RawBody, @ExtractedFields)
  → Validate lease (LeaseToken, LeaseUntil)
  → INSERT WebsiteExecutions (audit)
  → Insert/update CollectedData (if hash changed)
  → UPDATE WebsiteSelectorsMonitor: NextRunAt, AttemptCount, LeaseOwner = NULL
  → Publish domain event (via application tier)

RenewWebsiteSelectorLease(@WebsiteSelectorsId, @LeaseToken, @LeaseSeconds)
  → Extend LeaseUntil for long-running tasks

StartWebsiteSelectorExecution(@WebsiteSelectorsId, @LeaseToken, @WorkerId)
  → Log execution start (optional, for debugging)
```

**Indexes:**
```
IX_WebsiteSelectors_Website
  ON dbo.WebsiteSelectors(WebsitesId)
  WHERE IsEnabled = 1

IX_WebsiteSelectorsMonitor_Due
  ON dbo.WebsiteSelectorsMonitor(NextRunAt, Id)
  WHERE LeaseOwner IS NULL  -- Filtered: only unclaimed items

IX_Exec_WebsiteSelectorsTime
  ON dbo.WebsiteExecutions(WebsiteSelectorsId, StartedAt DESC)
```

#### 001_webhooks_table.sql
Optional schema for webhook notifications:
```
WebsiteSelectionWebhooks
├── Id (PK)
├── WebsiteSelectorsId (FK)
├── WebhookUrl, Method, Headers, Payload, IsActive

WebhookDeliveries (audit log)
├── Id (PK)
├── WebhookId (FK)
├── ExecutionId (FK → WebsiteExecutions)
├── Status, StatusCode, Response, DeliveredAt
```

---

### `tests/` - Test Project

| File | Purpose |
|------|---------|
| `WebsiteCollector.Worker.Tests.Development.csproj` | Test project file (targets net10.0) |
| `[Test files]` | Spike tests / development fixtures (to be expanded) |

---

### `docs/` - Documentation

| File | Purpose |
|------|---------|
| `README.md` | **Existing** - Setup, deployment, operational behavior, security |
| `ARCHITECTURE.md` | **New** - Detailed design, data flow, concurrency patterns |
| `FILE_STRUCTURE.md` | **New** - This file - project organization |

---

## Key Relationships

```
Program.cs
  ├─ Registers CollectorOptions (appsettings)
  ├─ Registers Database (singleton)
  ├─ Registers IEventAggregator (singleton)
  ├─ Registers Repositories (scoped)
  │   ├─ IWebsiteCollectionRepository
  │   └─ IWebsiteSelectionWebhookRepository
  ├─ Registers Services (scoped)
  │   ├─ IWebsiteCollectionService
  │   ├─ ISelectorManagementService
  │   ├─ ILeaseManagementService
  │   └─ ISubscriber (WebsiteSelectorsSubscriber)
  ├─ Registers HttpClient (IHttpClientFactory)
  └─ Registers CollectorWorker (hosted service)
	   │
	   ├─ Uses: IWebsiteCollectionService
	   │   └─ Calls: ClaimSelectorsAsync() → WebsiteCollectionRepository → ClaimWebsiteSelectors (stored procedure)
	   ├─ Uses: IHttpClientFactory
	   │   └─ Fetches website content
	   ├─ Uses: IWebsiteCollectionService
	   │   └─ Calls: CompleteExecutionAsync() → WebsiteCollectionRepository → CompleteWebsiteSelector (stored procedure)
	   └─ Publishes: CollectionCompletedEvent
			└─ Consumed by: WebsiteSelectorsSubscriber (future webhook delivery)
```

---

## File Naming Conventions

| Pattern | Example | Purpose |
|---------|---------|---------|
| `I{Name}.cs` | `ISubscriber.cs`, `IWebsiteCollectionRepository.cs` | Interface definitions |
| `{Name}Service.cs` | `WebsiteCollectionService.cs`, `LeaseManagementService.cs` | Business logic services |
| `{Name}Repository.cs` | `WebsiteCollectionRepository.cs` | Data access layer |
| `{Name}Models.cs` | `WebsiteCollectionModels.cs`, `LeaseModels.cs` | DTOs / data contracts |
| `{Name}Event.cs` | `CollectionCompletedEvent.cs` | Domain events |
| `{Name}Subscriber.cs` | `WebsiteSelectorsSubscriber.cs` | Event subscribers |
| `{Name}Options.cs` | `CollectorOptions.cs` | Configuration classes |
| `000_*.sql` | `000_unified_schema.sql` | Master schema (numerical prefix for ordering) |
| `001_*.sql` | `001_webhooks_table.sql` | Feature/extension schema |

---

## File Size Reference

| File | Est. Lines | Purpose |
|------|-----------|---------|
| `CollectorWorker.cs` | ~140 | Main orchestration loop |
| `WebsiteCollectionRepository.cs` | ~100 | Stored procedure wrapper |
| `WebsiteCollectionService.cs` | ~65 | Business logic orchestrator |
| `Program.cs` | ~43 | Dependency injection setup |
| `000_unified_schema.sql` | ~296 | Master schema |
| `Database.cs` | ~30 | Connection manager |
| `EventAggregator.cs` | ~30 | In-memory pub/sub |
| `CollectorOptions.cs` | ~20 | Configuration class |

---

## Architecture-Level Dependencies

```
┌─────────────────────────────────────────┐
│ Application Layer                       │
├─────────────────────────────────────────┤
│ ▪ CollectorWorker (BackgroundService)   │
│ ▪ Program.cs (DI & host config)         │
└──────────────┬──────────────────────────┘
			   │ depends on
┌──────────────▼──────────────────────────┐
│ Feature Layer (domain logic)            │
├─────────────────────────────────────────┤
│ ▪ WebsiteCollection/                    │
│   ├─ Services (business logic)          │
│   ├─ Repositories (data access)         │
│   └─ Subscribers (event handlers)       │
│ ▪ LeaseManagement/                      │
│ ▪ SelectorManagement/                   │
└──────────────┬──────────────────────────┘
			   │ depends on
┌──────────────▼──────────────────────────┐
│ Infrastructure Layer (utilities)        │
├─────────────────────────────────────────┤
│ ▪ Database (connection mgmt)            │
│ ▪ Configuration (appsettings parsing)   │
│ ▪ Events (pub/sub broker)               │
│ ▪ Http (HttpClient factory)             │
└──────────────┬──────────────────────────┘
			   │ depends on
┌──────────────▼──────────────────────────┐
│ Data Layer (SQL Server)                 │
├─────────────────────────────────────────┤
│ ▪ Websites, WebsiteSelectors, ...       │
│ ▪ Stored procedures (ClaimWebsiteSelectors, etc.)
└─────────────────────────────────────────┘
```

---

## Configuration Cascade

```
appsettings.json (defaults)
  ↓
Environment Variables (override)
  ├─ ConnectionStrings__Default
  ├─ Collector__Concurrency
  ├─ Collector__LeaseSeconds
  ├─ Collector__PollSeconds
  ├─ Collector__MaxResponseBytes
  └─ Collector__AllowedHosts__0, __1, __2, ...
  ↓
IOptions<CollectorOptions> (injected)
  ↓
CollectorWorker (uses settings)
```

---

## Deployment Artifacts

| Artifact | Source | Destination | Purpose |
|----------|--------|-------------|---------|
| Executable | `src/WebsiteCollector.Worker/bin/Release/` | Production runner or container | Application runtime |
| Docker Image | `Dockerfile` + executable | Container registry | Cloud/k8s deployment |
| Database Schema | `sql/000_unified_schema.sql` | SQL Server | Data storage & procedures |
| Configuration | `appsettings.json` + env vars | Environment | Runtime behavior tuning |

---

## Summary Statistics

| Category | Count |
|----------|-------|
| **Projects** | 2 (main + tests) |
| **Features** | 3 (WebsiteCollection, LeaseManagement, SelectorManagement) |
| **Infrastructure Modules** | 4 (Database, Configuration, Events, Http) |
| **Core Tables** | 5 (Websites, WebsiteSelectors, Monitor, Executions, CollectedData) |
| **Stored Procedures** | 4 (Claim, Complete, Renew, Start) |
| **Indexes** | 3 (primary, due, executions) |
| **C# Files** | ~25-30 (including tests) |
| **SQL Files** | 2 (schema + webhooks) |
| **Total Lines of Code** | ~1,000-1,500 (est.) |

