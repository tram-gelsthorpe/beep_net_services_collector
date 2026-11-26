# Beep Services Collector

A .NET 10 background worker that periodically collects content from websites, stores it in SQL Server,
and notifies subscribers. No message broker: queuing, leasing and scheduling are all done in SQL Server.

## Projects

| Project | Purpose |
| --- | --- |
| `src/Beep.ServiceBus` | Generic work queue: `WorkItems` table, claim/renew/complete stored procedures, `GenericWorkerBase<T>` polling worker. |
| `src/Beep.Scheduler` | Recurring schedules (`Schedules`) that call an `IScheduleHandler` to enqueue work. |
| `src/Beep.EventBus` | Publisher for HTTP POST deliveries to registered endpoints, delivered through ServiceBus work items; plus an in-process event aggregator. |
| `src/Beep.Collector` | The host: website selectors, collection worker, schedule handler, subscriber. |
| `src/Beep.Api` | ASP.NET Core API (API-key protected): records the bank rates the Collector delivers and serves them. |
| `src/Beep.Website.Currency` | Razor Pages website: latest bank rates by country, date and bank name or BIC, read through Beep.Api. |
| `src/Beep.Notification` | Email alerts for every website: subscribers (`dbo.User`) and subscriptions (`dbo.User_Notification`) in A_1_Beep_Global_Default, sent with Beep.Email.Core. Hosted by Beep.Api. |
| `src/Beep.Database` | `SqlDatabaseInitializer`: creates/upgrades a database from the SQL scripts embedded in an assembly. |
| `src/Beep.Configuration` | Optional Azure Key Vault source and startup checks for required settings and secrets. |

## How it works

The ServiceBus is the only place that coordinates workers: every job (a collection, a delivery) is a
`WorkItems` row that is claimed with a lease, renewed while it runs, retried with back-off up to
`MaxAttempts`, and reclaimed if its worker dies. Any number of worker instances can run.

1. **Schedule** – `SchedulerService` queues each due schedule as a ServiceBus work item, which `SchedulerWorker` runs (once;
   `StartTime` is in the schedule's `TimeZone`, by default the machine's). When the `WebsiteCollector` schedule is due, `WebsiteCollectorScheduleHandler` pages through
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

`src/Beep.Collector/appsettings.json` holds the non-secret settings: `WebsiteCollector` and
`EventBus` (worker concurrency, poll interval, lease length), `Scheduler` (frequency, start time, time
zone) and `BeepNLog`. Connection strings and API keys are **not** committed: their keys are there with
empty values, and each environment supplies them.

Settings are read in this order, later ones winning: `appsettings.json` → `appsettings.{Environment}.json`
→ user secrets (Development only) → environment variables → command line → Azure Key Vault (when
`KeyVault:Uri` is set).

### Required settings

| Host | Setting | Environment variable | Key Vault secret |
| --- | --- | --- | --- |
| Worker | `ConnectionStrings:Scheduler` / `ServiceBus` / `EventBus` / `Collector` | `ConnectionStrings__Collector`, ... | `ConnectionStrings--Collector`, ... |
| Worker | `BeepNLog:DatabaseConnection` | `BeepNLog__DatabaseConnection` | `BeepNLog--DatabaseConnection` |
| Api | `ConnectionStrings:Currency` / `Collector` / `Country` / `Default` | `ConnectionStrings__Currency`, ... | `ConnectionStrings--Currency`, ... |
| Api | `Notification:LicenceId`, `Notification:Websites:Currency:Url` | `Notification__LicenceId`, ... | `Notification--LicenceId`, ... |
| Api | `Audit:CreatedBy` | `Audit__CreatedBy` | `Audit--CreatedBy` |
| Api | `Authentication:ApiKeys` (one or more) | `Authentication__ApiKeys__0`, `__1`, ... | `Authentication--ApiKeys--0`, ... |
| Website.Currency | `BeepApi:BaseUrl`, `BeepApi:ApiKey` | `BeepApi__BaseUrl`, `BeepApi__ApiKey` | `BeepApi--BaseUrl`, `BeepApi--ApiKey` |

Optional: `EventBus:ApiKey` (`EventBus__ApiKey`, `EventBus--ApiKey`), sent as `X-API-Key` with
EventBus deliveries so they pass the Api's key check. It is sent only to the origins listed in
`EventBus:ApiKeyOrigins` (e.g. `https://api.example.com`), never to other DispatchUrls, and the
Worker does not start if a key is set without origins. Use one of the Api's `Authentication:ApiKeys`.

Both hosts check these at startup (`Beep.Configuration`) and stop with the names of what is missing.
Outside Development each API key must be at least 32 characters and not a placeholder. Generate one with:

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(36))
```

### Developer machine: user secrets

The Worker's launch profile runs it as `Development`, which loads user secrets (stored in your profile,
outside the repo):

```powershell
dotnet user-secrets set --project src/Beep.Collector "ConnectionStrings:Collector" "Server=.\SQLEXPRESS;Database=WebsiteCollector;Integrated Security=true;Encrypt=false;TrustServerCertificate=true;"
# ... Scheduler, ServiceBus, EventBus, BeepNLog:DatabaseConnection
dotnet user-secrets set --project src/Beep.Api "Authentication:ApiKeys:0" "dev-api-key-12345"
# Rate alerts: A_1_Beep_Global_Default, the licence subscribers are created under, and a folder for emails
# (without an SMTP host, emails are written there instead of being sent)
dotnet user-secrets set --project src/Beep.Api "ConnectionStrings:Default" "Server=.\SQLEXPRESS;Database=A_1_Beep_Global_Default;Integrated Security=true;Encrypt=false;TrustServerCertificate=true;"
dotnet user-secrets set --project src/Beep.Api "Notification:LicenceId" "<dbo.Licence.Id>"
dotnet user-secrets set --project src/Beep.Api "Notification:PickupDirectory" "C:\Tempeep-mail"
# Let the Worker's deliveries reach the local Api
dotnet user-secrets set --project src/Beep.Collector "EventBus:ApiKey" "dev-api-key-12345"
dotnet user-secrets set --project src/Beep.Collector "EventBus:ApiKeyOrigins:0" "https://localhost:55281"
# Let the currency website read rates from the local Api
dotnet user-secrets set --project src/Beep.Website.Currency "BeepApi:BaseUrl" "https://localhost:55281/"
dotnet user-secrets set --project src/Beep.Website.Currency "BeepApi:ApiKey" "dev-api-key-12345"
dotnet user-secrets list --project src/Beep.Collector
```

`dotnet run --no-launch-profile` (or a service) defaults to `Production`, which does not read user secrets.

### Azure: Key Vault with a managed identity

1. Store the settings as secrets, `:` written as `--` (e.g. `ConnectionStrings--Collector`).
2. Enable a managed identity on the App Service / VM and grant it **Key Vault Secrets User** on the vault.
3. Set `KeyVault__Uri=https://<vault>.vault.azure.net/` (and `KeyVault__ManagedIdentityClientId` for a
   user-assigned identity).

For Azure SQL, prefer `Authentication=Active Directory Managed Identity` in the connection string, so it
holds no password.

### Windows server

Either set machine environment variables for the service account:

```powershell
[Environment]::SetEnvironmentVariable("ConnectionStrings__Collector", "Server=...;Integrated Security=true;...", "Machine")
```

or use the same Key Vault: set `KeyVault__Uri` and give the server an identity, either Azure Arc's managed
identity or a service principal (`AZURE_TENANT_ID`, `AZURE_CLIENT_ID` and `AZURE_CLIENT_CERTIFICATE_PATH` or
`AZURE_CLIENT_SECRET`). Run the service as a dedicated account (or gMSA) with `Integrated Security=true`
so connection strings need no password.

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
(see `tests/Beep.Collector.Tests.Integration/appsettings.json`; override with
`ConnectionStrings__ServiceBus` / `ConnectionStrings__Collector`).
