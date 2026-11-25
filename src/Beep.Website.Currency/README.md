# Beep.Website.Currency

Razor Pages website that shows the latest bank exchange rates recorded by [Beep.Api](../Beep.Api/README.md).

On the home page, choose any combination of:

- **Country**: the countries that have a bank with rates;
- **Date**: rates on or before that day (empty: the latest recorded);
- **Bank name or BIC**: a BIC (`BFTVVNVX`) or part of a name (`Vietcombank`), with suggestions for the
  selected country.

Each matching bank is shown with its rate per currency: buy (cash), buy (transfer) and sell, with the day
and the time the bank published them. The filters are in the URL (`/?country=VN&date=2024-05-01&bank=BFTVVNVX`),
so a search can be bookmarked or shared.

The site reads everything from Beep.Api's `GET /api/currencyrate/countries`, `/banks` and `/latest`. The calls
are made from the server, so the API key never reaches the browser.

## Configuration

| Key | Purpose |
| --- | --- |
| `BeepApi:BaseUrl` | Beep.Api's address, e.g. `https://localhost:55281/` |
| `BeepApi:ApiKey` | One of Beep.Api's `Authentication:ApiKeys` |

Set them in user secrets on a developer machine, or in environment variables or Key Vault (`KeyVault:Uri`).
The site does not start without them.

## Run

Start Beep.Api, then:

```bash
dotnet run --project src/Beep.Website.Currency
```

It listens on `https://localhost:55291`.
