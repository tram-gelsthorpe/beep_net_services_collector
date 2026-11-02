# WebsiteCollector (.NET 10, SQL Server, Dapper)

Multi-instance interval-scheduled website collector. No Entity Framework and no message broker.

## Prerequisites
- .NET 10 SDK/runtime
- SQL Server 2019+ recommended, with READ_COMMITTED_SNAPSHOT enabled for reduced reader contention
- Database login with EXECUTE on dbo.ClaimWebsites and dbo.CompleteWebsite, plus needed SELECT/INSERT/UPDATE permissions

## Setup
1. Create an empty `WebsiteCollector` database.
2. Execute `sql/001_schema.sql` in that database (SSMS or `sqlcmd` supporting GO).
3. Set `ConnectionStrings__Default` environment variable to the actual connection string (avoid checking credentials into source).
4. Set `Collector__AllowedHosts__0` (and further indexed entries) to approved HTTPS hostnames.
5. `cd src/WebsiteCollector.Worker && dotnet restore && dotnet build -c Release && dotnet run`
6. Run the same executable on additional hosts with the same SQL Server database. Each instance gets a distinct worker ID.

## Add a website
```sql
INSERT dbo.Websites(Name,Url,CollectionType,IntervalSeconds)
VALUES ('Example','https://example.com/','HTML',300);
INSERT dbo.WebsiteSelectors(WebsiteId,FieldName,CssSelector)
SELECT Id,'Title','title' FROM dbo.Websites WHERE Name='Example';
```

## Operational behavior
- Claim procedure uses `UPDLOCK, READPAST` to claim different rows across workers. Each claim gets a unique token and lease.
- Successful writes and schedule updates are performed in a single transaction and fenced by lease token and expiry.
- Failures use bounded exponential retry delay; after MaxAttempts, retry counter resets and the next normal interval begins.
- Lease expiry enables recovery after crashes. The previous running execution is marked Abandoned when a new owner starts.
- `CollectedData` deduplicates unchanged response bodies per website by SHA-256.
- The service stores the whole response as UTF-8 text and optionally extracts simple CSS fields for HTML.
- If the response is unchanged, the new successful execution is logged without inserting a duplicate response.

## Deployment / scaling
- Use Windows Service, systemd, or orchestrated containers with restart-on-failure.
- Size `Concurrency` per instance to respect upstream rate limits, DB capacity and network throughput.
- For very large fleets, partition scheduling by tenant/host, archive executions, and monitor due-job lag, failures, lease expiry and SQL waits.
- Keep clocks synchronized. SQL Server UTC time is the source of truth for scheduling.

## Security / limits
- Only HTTPS URLs on configured allowed hosts are fetched; redirects are disabled to limit SSRF.
- In a hostile multi-tenant environment, also validate DNS-resolved IP ranges and enforce egress firewall rules to block private, loopback and metadata networks. Host allowlists alone are not sufficient when DNS is untrusted.
- Store secrets in environment variables or a secrets manager, not in website configuration.
- Respect robots/access policies, site rate limits, and applicable terms.
- `MaxResponseBytes` limits memory per response; content is decoded as UTF-8, so add charset-aware decoding if needed.
- This starter supports GET requests and one HTML field per CSS selector. JavaScript-rendered pages, authenticated sessions, JSONPath extraction, pagination, and multi-item HTML records require additional adapters.
- Large bodies in NVARCHAR(MAX) increase SQL storage; use retention and compression/object storage for large-scale production.

## Validation before production
- Build and run integration tests against your SQL Server version and websites; the code was generated without a .NET SDK available to execute compilation or tests here.
- Load test with at least two instances, kill one during collection, confirm recovery after lease expiry, verify no duplicate stored content and tune indexes.
- Configure alerts on long due-job lag, repeated failures, SQL connection errors and stale leases.

## Files
- `src/WebsiteCollector.Worker`: worker source, Dapper access, HTTP collector and options.
- `sql/001_schema.sql`: schema and claim/complete procedures.
- `sql/002_seed_example.sql`: sample website and CSS selector.
- `sql/003_operations.sql`: operational queries.
- `Dockerfile`: multi-stage Linux image build.

## Docker build
```bash
docker build -t website-collector:local .
docker run --rm \
  -e ConnectionStrings__Default='Server=YOUR_SQL_SERVER;Database=WebsiteCollector;User Id=YOUR_USER;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=False' \
  -e Collector__AllowedHosts__0=example.com \
  website-collector:local
```
Provide production secrets through your deployment platform, not command-line history.

## Scope and caveats
This is a **working-source starter, not a certified production release**. No .NET SDK or live SQL Server was available to compile and integration-test it. Verify package compatibility and exercise SQL claim/lease behavior in staging. HTML extraction is intentionally limited to simple CSS selectors; JSON/XML extraction is not implemented (validation and raw storage only). Only HTTPS GET and allowlisted hosts are supported. No management API or dashboard is included.
