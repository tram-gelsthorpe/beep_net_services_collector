# Beep.Api

ASP.NET Core Web API for the Beep collector system. It receives collection results from the Beep.Collector
EventBus and records bank currency rates; see the [solution README](../../README.md) for the overall architecture.

## Endpoints

| Method | Route | Description |
| --- | --- | --- |
| `POST` | `/api/currencyrate/currency/vietcombank` | Record Vietcombank's rates (bank BIC `BFTVVNVX`) from a collection result. |

### Recording bank rates

The EventBus POSTs `{ "WebsiteSelectorsId", "CollectedDataId", "CollectedAt" }` to the selector's DispatchUrl.
The endpoint then:

1. reads `ExtractedJson` of `CollectedDataId` from the Collector database (`dbo.WebsiteSelectorsCollectedData`);
2. reads the bank's rate list from it. For Vietcombank: `ContentSelector.Date` is the day, `UpdatedDate`
   the time, and each `Data` item maps `cash` to `BuyingRateCash`, `transfer` to `BuyingRateTransfer` and
   `sell` to `SellingRate` (a rate of `0.00`, meaning not offered, is stored as NULL);
3. finds the bank in `dbo.Bank` by the endpoint's BIC and each currency in `dbo.Currency` by `CodeISO`;
4. inserts one `dbo.Currency_Rate_Bank` row per currency, with `Date`, `RateTime` (the bank's local time and
   offset), `Version` 1 and `CreatedBy` = `Audit:CreatedBy`.

A rate whose bank, currency, `Date` and `RateTime` are already recorded is skipped, so redeliveries are safe.
Earlier rates of a day are kept; the current rate of a day is the one with the highest `RateTime`.

| Response | When |
| --- | --- |
| `200` `{ "inserted", "duplicates", "unknownCurrencies" }` | Recorded (currency codes not in `dbo.Currency` are listed and skipped) |
| `404` | The collection result does not exist or has no extracted data |
| `422` | The extracted data is not the bank's rate list |

Before the first run, apply [`Resource/Currency_Rate_Bank_RateTime.sql`](Resource/Currency_Rate_Bank_RateTime.sql)
to A_1_Beep_Global_Currency: it adds `RateTime` and an index on `(Bank_Id, Currency_Id, Date, RateTime)`.

Swagger UI is available at `/swagger` in the Development environment.

## Authentication

Every endpoint except `/swagger`, `/api-docs` and `/health` requires an `X-API-Key` header matching one of
`Authentication:ApiKeys`. Keep real keys out of `appsettings.json` (use user secrets or environment variables).

## Configuration

| Key | Purpose |
| --- | --- |
| `ConnectionStrings:Currency` | A_1_Beep_Global_Currency, where rates are recorded |
| `ConnectionStrings:Collector` | Beep.Collector's database, where collection results are read |
| `Audit:CreatedBy` | GUID of the user recorded as `CreatedBy` on everything this API writes |
| `Authentication:ApiKeys` | Accepted API keys |

## Run

```bash
dotnet run --project src/Beep.Api
```
