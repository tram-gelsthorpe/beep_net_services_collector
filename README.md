# Beep Services Collector

A .NET 10 background worker that periodically collects content from websites, stores it in SQL Server,
and notifies subscribers. No message broker: queuing, leasing and scheduling are all done in SQL Server.

## Projects

| Project | Purpose |
| --- | --- |
| `src/Beep.ServiceBus` | Generic work queue: `WorkItems` table, claim/renew/complete stored procedures, `GenericWorkerBase<T>` polling worker. |
| `src/Beep.Scheduler` | Recurring schedules (`WorkerSchedules`) that call an `IScheduleHandler` to enqueue work. |
| `src/Beep.EventBus` | Publisher for HTTP POST deliveries to registered endpoints, delivered through ServiceBus work items; plus an in-process event aggregator. |
| `src/WebsiteCollector.Worker` | The host: website selectors, collection worker, schedule handler, subscriber. |
| `src/WebsiteCollector.Api` | ASP.NET Core API (API-key protected). Currently a stub. |
| `src/Beep.Database` | `SqlDatabaseInitializer`: creates/upgrades a database from the SQL scripts embedded in an assembly. |

## How it works

The ServiceBus is the only place that coordinates workers: every job (a collection, a delivery) is a
`WorkItems` row that is claimed with a lease, renewed while it runs, retried with back-off up to
`MaxAttempts`, and reclaimed if its worker dies. Any number of worker instances can run.

1. **Schedule** – `SchedulerService` claims due schedules with `Scheduler.ClaimDueSchedules` (one instance per run;
   `StartTime` is UTC). When the `WebsiteCollector` schedule is due, `WebsiteCollectorScheduleHandler` pages through
   the enabled `WebsiteSelectors` rows (5,000 at a time). For each page it registers the selectors' `DispatchUrl`s as
   EventBus endpoints, then enqueues a collection work item per selector, skipping selectors that still have an
   unfinished work item.
2. **Collect** – `WebsiteCollectorWorker` claims work items with a lease, fetches the selector's `CollectionUrl`
   (HTTPS only, size-capped), extracts content with the CSS `ContentSelector` for HTML, and renews the lease while working.
3. **Complete** – the work item is completed (success, or retry with exponential backoff up to `MaxAttempts`) and the
   result is written to `WebsiteSelectorsExecutions` / `WebsiteSelectorsCollectedData`.
4. **Publish** – a `CollectionCompletedEvent` is raised in-process; `WebsiteSelectorsSubscriber` publishes it with
   `IEventBusPublisher` (event id = the collected data Id, so each result is delivered once).
5. **Deliver** – the publisher stores one `EventBusDeliveries` row per enabled endpoint and enqueues a ServiceBus work
   item for it; `EventBusWorker` claims those items and POSTs the JSON payload. Retries and abandoning come from the
   ServiceBus.

### Publishing from other code

Anything can deliver through the EventBus: register endpoints for an owner, then publish events for it.

```csharp
await eventBusRepository.RegisterEndpointsAsync([new EventBusEndpoint(ownerId, "https://example.com/hook")], ct);
await publisher.PublishAsync(ownerId, eventId, new { ownerId, eventId, /* ... */ }, ct);
```

## Databases

Each library owns its schema as embedded scripts in `Resource/`. At startup the worker (`CreationDatabaseService`) uses
`Beep.Database` to create any missing database
from `Resource/0.sql` and applies newer upgrade scripts (`1.sql`, `2.sql`, ...), tracking the version in
`<schema>.DatabaseVersion`. To change a schema, add the next numbered script — never edit a released one.

| Connection string | Schema | Contents |
| --- | --- | --- |
| `Scheduler` | `Scheduler` | Schedules and execution log |
| `ServiceBus` | `ServiceBus` | Work items, attempts, heartbeats |
| `EventBus` | `EventBus` | Endpoints and deliveries |
| `Collector` | `dbo` | Websites, selectors, executions, collected data |

## Configuration

`src/WebsiteCollector.Worker/appsettings.json` holds the connection strings above plus `BeepNLog`,
`WebsiteCollector` and `EventBus` (worker concurrency, poll interval, lease length) and `Scheduler`
(frequency and start time). Override any value with environment variables, e.g. `ConnectionStrings__Collector`.

## Adding a website

```sql
DECLARE @websiteId uniqueidentifier = NEWID();
INSERT INTO dbo.Websites (Id, Name, Url) VALUES (@websiteId, 'Example', 'https://example.com');
INSERT INTO dbo.WebsiteSelectors (WebsitesId, CollectionUrl, CollectionType, ContentSelector)
VALUES (@websiteId, 'https://example.com/rates', 'HTML', 'table.rates td');
```

It is picked up the next time the schedule runs.

## Build and test

```bash
dotnet build beep_net_services_collector.slnx
dotnet test --solution beep_net_services_collector.slnx
```

Build through the solution file: the shared Beep pipeline build targets need `$(SolutionDir)`.

Integration tests create and drop their own databases on `(localdb)\MSSQLLocalDB`
(see `tests/WebsiteCollector.Worker.Tests.Integration/appsettings.json`; override with
`ConnectionStrings__ServiceBus` / `ConnectionStrings__Collector`).
