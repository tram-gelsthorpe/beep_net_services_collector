# Beep.Api

ASP.NET Core Web API for the Beep collector system. It receives collection results from the Beep.Collector
EventBus and records bank currency rates, and serves the recorded rates (to Beep.Website.Currency); see the
[solution README](../../README.md) for the overall architecture.

## Endpoints

| Method | Route | Description |
| --- | --- | --- |
| `POST` | `/api/currencyrate/currency/{bank}` | Record a bank's rates from a collection result of its rate page: `vietcombank` (BIC `BFTVVNVX`) or one of the [other banks](#other-banks). |
| `GET` | `/api/currencyrate/countries` | Countries that have a bank with rates: `[{ "code", "name" }]`. |
| `GET` | `/api/currencyrate/banks?country=&bank=` | Banks with rates and their latest date. |
| `GET` | `/api/currencyrate/latest?date=&country=&bank=` | The latest rate of each bank and currency. |
| `POST` | `/api/notification/subscribe` | Subscribe an email address to a website's alerts (see [Rate alerts](#rate-alerts)). |
| `POST` | `/api/notification/confirm` | `{ "token" }` from a confirmation link: `200`, or `404` for an unknown link. |
| `POST` | `/api/notification/unsubscribe` | `{ "token" }` from an unsubscribe link: `200`, or `404` for an unknown link. |

### Reading the latest rates

All filters are optional and combine:

- `date` (`yyyy-MM-dd`): rates on or before that day. Without it, the latest recorded.
- `country`: a country code from A_1_Beep_Global_Country's `dbo.Country` (e.g. `VN`).
- `bank`: a BIC (`BFTVVNVX`) or part of a bank name (`vietcombank`), matched literally.

For each bank and currency, `latest` returns the row with the highest `Date`, then the highest `RateTime`:
`{ "bic", "bankName", "countryCode", "countryName", "currencyCode", "currencyName", "date", "rateTime",
"buyingRateCash", "buyingRateTransfer", "sellingRate", "currencySymbol", "quoteCurrencyCode" }`, where
`quoteCurrencyCode` is the currency the rates are in (the bank country's `Currency_Id`, e.g. `VND`). A currency a bank stopped publishing keeps its last
rate, so dates can differ within a bank. An unknown country or a malformed date gives `400`.

### Recording bank rates

The EventBus POSTs `{ "WebsiteSelectorsId", "CollectedDataId", "CollectedAt" }` to the selector's DispatchUrl.
The endpoint then:

1. reads `ExtractedJson` of `CollectedDataId` from the Collector database (`dbo.WebsiteSelectorsCollectedData`);
2. reads the bank's rate list from it. For Vietcombank: `ContentSelector.Date` is the day, `UpdatedDate`
   the time, and each `Data` item maps `cash` to `BuyingRateCash`, `transfer` to `BuyingRateTransfer` and
   `sell` to `SellingRate` (a rate of `0.00`, meaning not offered, is stored as NULL). For the other banks,
   see [Other banks](#other-banks);
3. finds the bank in `dbo.Bank` by the endpoint's BIC and each currency in `dbo.Currency` by `CodeISO`;
4. inserts one `dbo.Currency_Rate_Bank` row per currency, with `Date`, `RateTime` (the bank's local time and
   offset), `Version` 1 and `CreatedBy` = `Audit:CreatedBy`.

A rate whose bank, currency, `Date` and `RateTime` are already recorded is skipped, so redeliveries are safe.
Earlier rates of a day are kept; the current rate of a day is the one with the highest `RateTime`.

| Response | When |
| --- | --- |
| `200` `{ "inserted", "duplicates", "unknownCurrencies" }` | Recorded (currency codes not in `dbo.Currency` are listed and skipped) |
| `404` | The collection result does not exist or has no extracted data |
| `400` | No `CollectedDataId`; or, for a bank recorded at its collection time, no `CollectedAt` |
| `404` | No bank of that name |
| `422` | The extracted data is not the bank's rate list |

### Other banks

`{bank}` is one of these. [`Collector/Seed/Vietnam_Banks.sql`](../Beep.Collector/Seed/Vietnam_Banks.sql) sets up
their WebsiteSelectors with the matching ContentSelector and DispatchUrl.

| `{bank}` | BIC | Page | Columns after the currency |
| --- | --- | --- | --- |
| `hsbc` | `HSBCVNVX` | table cells; `25.716`, `158,61` | buy cash, buy transfer, sell cash, sell transfer |
| `woori` | `HVBKVNVX` | table cells; JPY and KRW per 100 | buy cash, buy transfer, sell cash, sell transfer |
| `ocb` | `ORCOVNVX` | table cells; `USD100` (the $100 notes' row) | name, buy cash, buy transfer, sell cash, sell transfer |
| `agribank` | `VBAAVNVX` | table cells | buy cash, buy transfer, sell |
| `bvbank` | `VCBCVNVX` | table cells | buy cash, buy transfer, sell cash, sell transfer |
| `vietabank` | `VNACVNVX` | table cells | name, buy cash, buy transfer, sell cash, sell transfer |
| `vietinbank` | `ICBVVNVX` | table cells; `# 25.470` | buy cash, buy transfer, sell |
| `namabank` | `NAMAVNVX` | table cells; `25.610,00` | buy cash, buy transfer, sell |
| `ncb` | `NVBAVNVX` | table cells | buy cash, buy transfer, sell transfer, sell cash |
| `seabank` | `SEAVVNVX` | the script with `listExchangeRate` | `buy`, `transferBuy`, `transferSell` (or `sell`) |

A table's cell holding a currency code (`USD`, `USD100`, `Đô la Mỹ (USD)`) starts its row. Only a currency's
first row is read (the largest notes), gold (`XAU`) is left out, and `0`, `-` or an empty cell is stored as NULL.
`SellingRate` is the transfer selling rate where the bank gives one. These pages give no publication time, so a
rate is recorded at `CollectedAt` (to the minute, Vietnam time), and is skipped when it equals the bank's
previous rate of that currency and day: collecting an unchanged page again records nothing and sends no alert.

SeABank's page lists each of the day's updates with its `updateTime`, which is used as `RateTime`.

Each bank must be in A_1_Beep_Global_Currency's `dbo.Bank` with its BIC, or the endpoint answers `500`.

Before the first run, apply [`Resource/Currency_Rate_Bank_RateTime.sql`](Resource/Currency_Rate_Bank_RateTime.sql)
to A_1_Beep_Global_Currency: it adds `RateTime` and an index on `(Bank_Id, Currency_Id, Date, RateTime)`.

## Rate alerts

Beep.Api hosts [Beep.Notification](../Beep.Notification/README.md). `POST /api/notification/subscribe` takes:

```json
{ "email": "ana@example.com", "website": "Currency", "notificationType": "BankRates",
  "filter": { "country": "VN", "banks": ["BFTVVNVX"], "currencies": ["USD", "EUR"] }, "culture": "vi" }
```

It answers `200 { "status": "confirmationSent" }` for a new or unconfirmed address, or `"updated"` for a
confirmed one. Invalid input gives `400 { "errors": { field: message } }`.

For Beep Currency (`Currency`, `BankRates`, in `Notifications/BankRatesNotification.cs`):
- **Filter:** the banks must be banks of the country that have rates, and the currencies must be ones those
  banks publish. Anything else is dropped.
- **Alerts:** when a delivery records new rates of a bank (`inserted > 0`), every confirmed subscriber who
  chose that bank gets one email after the response. It has the bank's latest rates of the currencies they
  chose, in their language. Redeliveries and the backfill send nothing.

Swagger UI is available at `/swagger` in the Development environment.

## Backfill past rates

The backfill command records a bank's past end-of-day rates, for the days that have no rate of that bank in
`dbo.Currency_Rate_Bank` yet, with the same rules as deliveries:

```bash
dotnet run --project src/Beep.Api -- backfill <bank>|all [--from yyyy-MM-dd] [--to yyyy-MM-dd] [--delay-ms 1000]
```

| `<bank>` | Past days from | Where they come from |
| --- | --- | --- |
| `vietcombank` | 2020-02-01 | `GET https://www.vietcombank.com.vn/api/exchangerates?date=yyyy-MM-dd` |
| `agribank` | 2020-02-01 | `GET https://www.agribank.com.vn/wcm/connect/ttkhac/ty-gia/yyyy/dd-MM-yyyy?...` (404: no rates that day) |
| `woori` | 2020-02-01 | `POST https://woori.com.vn/wp-admin/admin-ajax.php`, `action=exchange_rate&date_search=yyyyMMdd` |
| `bvbank` | 2022-11-14 | `POST https://bvbank.net.vn/wp-admin/admin-ajax.php`, `action=exchange_rate_by_date&date=yyyy-MM-dd` |
| `ncb` | 2020-02-01 | `POST https://www.ncb-bank.vn/vi/ty-gia-tien-te`, `op=ajax&date=dd-MM-yyyy` |

`all` runs each bank in turn. These are what the banks' rate pages request when a date is picked. The other banks'
pages show only today's rates (HSBC, VietABank, VietinBank, SeABank), need a sign-in token (OCB) or a page
postback (Nam A Bank), so they have no backfill.

A past day's rates are recorded at 23:00 Vietnam time, read with the bank's [parser](#other-banks). A day the bank
has no rates for (a weekend, a holiday) is skipped: Agribank answers 404, Woori nothing, NCB the table of the last
day with rates (with that day's date), and BVBank the last day's table without a date, so a BVBank day whose rates
are the same as the day before's is taken as having none.

| Option | Default |
| --- | --- |
| `--from` | the bank's first day above (also the earliest) |
| `--to` | yesterday in Vietnam (today's rates are not final) |
| `--delay-ms` | 1000: pause between requests to the bank |

It uses the Api's settings (`ConnectionStrings:Currency`, `Audit:CreatedBy`) and does not start the web server.
A failed request is retried twice; a day that still fails is listed at the end. Ctrl+C stops after the current
day. Running it again only fetches days that are still missing, so it can be stopped and resumed. Exit code: 0
when every missing day was filled, 1 when some failed, 2 for a usage error. A bank's full history (about 2,500
days from 2020) takes about 45 minutes.

## Authentication

Every endpoint except `/swagger`, `/api-docs` and `/health` requires an `X-API-Key` header matching one of
`Authentication:ApiKeys`. Keep real keys out of `appsettings.json` (use user secrets or environment variables).

## Configuration

| Key | Purpose |
| --- | --- |
| `ConnectionStrings:Currency` | A_1_Beep_Global_Currency, where rates are recorded |
| `ConnectionStrings:Collector` | Beep.Collector's database, where collection results are read |
| `ConnectionStrings:Country` | A_1_Beep_Global_Country, for country codes and names (cached for an hour) |
| `ConnectionStrings:Default` | A_1_Beep_Global_Default: alert subscribers (`dbo.User`) and subscriptions (`dbo.User_Notification`) |
| `Notification:LicenceId` | `dbo.Licence.Id` that new subscribers are created under |
| `Notification:Websites:Currency:Url` | Beep Currency's address; links in emails start with it (`appsettings.Development.json`: `https://localhost:55291`) |
| `Notification:PickupDirectory` | Optional, local: without an SMTP host, emails are written here as .html files |
| `Mail:Smtp` | Sender address and SMTP server (`From`, `Network:Host`, `Port`, `EnableSsl`, `Username`, `Password`) |
| `Audit:CreatedBy` | GUID of the user recorded as `CreatedBy` on everything this API writes |
| `Authentication:ApiKeys` | Accepted API keys |

## Run

```bash
dotnet run --project src/Beep.Api
```
