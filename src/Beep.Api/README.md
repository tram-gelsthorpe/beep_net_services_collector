# Beep.Api

ASP.NET Core Web API for the Beep collector system. It receives collection results from the Beep.Collector
EventBus and records bank currency rates, and serves the recorded rates (to Beep.Website.Currency); see the
[solution README](../../README.md) for the overall architecture.

## Endpoints

| Method | Route | Description |
| --- | --- | --- |
| `POST` | `/api/currencyrate/currency/vietcombank` | Record Vietcombank's rates (bank BIC `BFTVVNVX`) from a collection result. |
| `GET` | `/api/currencyrate/countries` | Countries that have a bank with rates: `[{ "code", "name" }]`. |
| `GET` | `/api/currencyrate/banks?country=&bank=` | Banks with rates and their latest date. |
| `GET` | `/api/currencyrate/latest?date=&country=&bank=` | The latest rate of each bank and currency. |

### Reading the latest rates

All filters are optional and combine:

- `date` (`yyyy-MM-dd`): rates on or before that day. Without it, the latest recorded.
- `country`: a country code from A_1_Beep_Global_Country's `dbo.Country` (e.g. `VN`).
- `bank`: a BIC (`BFTVVNVX`) or part of a bank name (`vietcombank`), matched literally.

For each bank and currency, `latest` returns the row with the highest `Date`, then the highest `RateTime`:
`{ "bic", "bankName", "countryCode", "countryName", "currencyCode", "currencyName", "date", "rateTime",
"buyingRateCash", "buyingRateTransfer", "sellingRate" }`. A currency a bank stopped publishing keeps its last
rate, so dates can differ within a bank. An unknown country or a malformed date gives `400`.

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

## Backfill past Vietcombank rates

Vietcombank publishes each day's end-of-day rates (from 2020-02-01) at
`https://www.vietcombank.com.vn/api/exchangerates?date=yyyy-MM-dd`. The backfill command records the days that
have no Vietcombank rate in `dbo.Currency_Rate_Bank` yet, with the same rules as deliveries:

```bash
dotnet run --project src/Beep.Api -- backfill vietcombank [--from yyyy-MM-dd] [--to yyyy-MM-dd] [--delay-ms 1000]
```

| Option | Default |
| --- | --- |
| `--from` | 2020-02-01 (the earliest available) |
| `--to` | yesterday in Vietnam (today's rates are not final) |
| `--delay-ms` | 1000: pause between requests to Vietcombank |

It uses the Api's settings (`ConnectionStrings:Currency`, `Audit:CreatedBy`) and does not start the web server.
A failed request is retried twice; a day that still fails is listed at the end. Ctrl+C stops after the current
day. Running it again only fetches days that are still missing, so it can be stopped and resumed. Exit code: 0
when every missing day was filled, 1 when some failed, 2 for a usage error. The full history (about 2,500 days)
takes about 45 minutes.

## Authentication

Every endpoint except `/swagger`, `/api-docs` and `/health` requires an `X-API-Key` header matching one of
`Authentication:ApiKeys`. Keep real keys out of `appsettings.json` (use user secrets or environment variables).

## Configuration

| Key | Purpose |
| --- | --- |
| `ConnectionStrings:Currency` | A_1_Beep_Global_Currency, where rates are recorded |
| `ConnectionStrings:Collector` | Beep.Collector's database, where collection results are read |
| `ConnectionStrings:Country` | A_1_Beep_Global_Country, for country codes and names (cached for an hour) |
| `Audit:CreatedBy` | GUID of the user recorded as `CreatedBy` on everything this API writes |
| `Authentication:ApiKeys` | Accepted API keys |

## Run

```bash
dotnet run --project src/Beep.Api
```
