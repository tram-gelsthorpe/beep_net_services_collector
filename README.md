# Beep Services Collector

A .NET 10 background worker that periodically collects content from websites, stores it in SQL Server,
and notifies subscribers. No message broker: queuing, leasing and scheduling are all done in SQL Server.

## Projects

| Project | Purpose |
| --- | --- |
| `src/Beep.ServiceBus` | Generic work queue: `WorkItems` table, claim/renew/complete stored procedures, `GenericWorkerBase<T>` polling worker. |
| `src/Beep.Scheduler` | Recurring schedules (`WorkerSchedules`) that call an `IScheduleHandler` to enqueue work. |
| `src/Beep.EventBus` | In-process event aggregator plus queued HTTP POST deliveries (`EventBus`, `EventBusDeliveries`). |
| `src/WebsiteCollector.Worker` | The host: website selectors, collection worker, schedule handler, subscriber. |
| `src/WebsiteCollector.Api` | ASP.NET Core API (API-key protected). Currently a stub. |
| `src/Beep.Database` | `SqlDatabaseInitializer`: creates/upgrades a database from the SQL scripts embedded in an assembly. |

## How it works

1. **Schedule** – `SchedulerService` polls `Scheduler.GetDueSchedules`. When the `WebsiteCollector` schedule is due,
   `WebsiteCollectorScheduleHandler` enqueues one work item per enabled `WebsiteSelectors` row.
2. **Collect** – `WebsiteCollectorWorker` claims work items with a lease, fetches the selector's `CollectionUrl`
   (HTTPS only, size-capped), extracts content with the CSS `ContentSelector` for HTML, and renews the lease while working.
3. **Complete** – the work item is completed (success, or retry with exponential backoff up to `MaxAttempts`) and the
   result is written to `WebsiteSelectorsExecutions` / `WebsiteSelectorsCollectedData`.
4. **Notify** – a `CollectionCompletedEvent` is published; `WebsiteSelectorsSubscriber` queues an EventBus delivery for
   each registered endpoint, which `EventBusWorker` POSTs as JSON.

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
