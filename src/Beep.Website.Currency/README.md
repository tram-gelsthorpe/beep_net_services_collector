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

## Pages

| Page | Route | What it shows |
| --- | --- | --- |
| Bank rates | `/?country=&date=&bank=` | Each matching bank's latest rates; USD/GBP/EUR cards when one bank matches |
| Compare banks | `/Compare?scope=one\|all&country=&currency=&amount=&date=&side=buy\|sell&sort=&only=` | One currency across banks. One country: cheapest to buy from (lowest sell), best to sell to (highest transfer buy), best vs. worst for the amount, "you pay" = amount × sell rate; sort by sell, buy or spread. All countries: each bank quotes in its own currency, so banks are compared by spread (lowest spread, best country by average spread, coverage); `only` filters by country, sort by spread or country |
| Converter | `/Converter?country=&bank=&currency=` | Converts at the latest rates of the bank chosen with the Country and Bank pickers: foreign → local uses the buy rate (transfer or cash), local → foreign the sell rate. The page sends the rates once; `wwwroot/js/converter.js` converts in the browser |

Spread = (sell − buy transfer) ÷ buy transfer. The design is in [`design/`](../../design/DESIGN_HANDOFF.md).

## Languages

The language menu (top right) offers English, Tiếng Việt, ไทย, 中文, 日本語, 한국어 and Français. The choice is
kept for a year in the `.AspNetCore.Culture` cookie (`/language/{code}?returnUrl=...` sets it); without it, the
browser's language is used, else English. The site's texts and their translations are in
[`Localization/UiText.cs`](Localization/UiText.cs): each text takes all seven languages, so a missing
translation does not build. Numbers and dates keep one format in every language, and bank and currency names
are shown as Beep.Api returns them.

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
