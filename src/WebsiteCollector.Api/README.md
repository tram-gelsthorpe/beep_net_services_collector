# WebsiteCollector.Api

ASP.NET Core Web API for the WebsiteCollector system. It is currently a stub that receives collected currency
rates; see the [solution README](../../README.md) for the overall architecture.

## Endpoints

| Method | Route | Description |
| --- | --- | --- |
| `POST` | `/api/currencyrate/currency/vietcombank` | Receive a Vietcombank currency rate (not yet persisted). |

Swagger UI is available at `/swagger` in the Development environment.

## Authentication

Every endpoint except `/swagger`, `/api-docs` and `/health` requires an `X-API-Key` header matching one of
`Authentication:ApiKeys`. Keep real keys out of `appsettings.json` (use user secrets or environment variables).

## Configuration

| Key | Purpose |
| --- | --- |
| `ConnectionStrings:Currency` | Database for currency rates |
| `Authentication:ApiKeys` | Accepted API keys |

## Run

```bash
dotnet run --project src/WebsiteCollector.Api
```
