# Beep.Website.Currency

Razor Pages website that shows the latest bank exchange rates recorded by [Beep.Platform.Api](../Beep.Platform.Api/README.md).

On the home page, choose any combination of:

- **Country**: the countries that have a bank with rates;
- **Date**: rates on or before that day (empty: the latest recorded);
- **Bank name or BIC**: a BIC (`BFTVVNVX`) or part of a name (`Vietcombank`), with suggestions for the
  selected country.

Each matching bank is shown with its rate per currency: buy (cash), buy (transfer) and sell, with the day
and the time the bank published them. The filters are in the URL (`/?country=VN&date=2024-05-01&bank=BFTVVNVX`),
so a search can be bookmarked or shared.

The site reads everything from Beep.Platform.Api's `GET /api/currencyrate/countries`, `/banks` and `/latest`. The calls
are made from the server, so the API key never reaches the browser.

## Pages

| Page | Route | What it shows |
| --- | --- | --- |
| Bank rates | `/?country=&date=&bank=` | Each matching bank's latest rates; USD/GBP/EUR cards when one bank matches |
| Compare banks | `/Compare?countries=VN&countries=TH&currency=&amount=&date=&side=buy\|sell&sort=&only=` | One currency across the banks of the countries chosen in the Countries picker. One country: cheapest to buy from (lowest sell), best to sell to (highest transfer buy), best vs. worst for the amount, "you pay" = amount × sell rate; sort by sell, buy or spread. Two or more: each bank quotes in its own currency, so banks are compared by spread (lowest spread, best country by average spread, coverage); `only` filters by country, sort by spread or country. Older `scope=all` and `country=` links still work |
| Rate alerts | `/alerts/confirm?token=`, `/alerts/unsubscribe?token=` | The links in alert emails: confirm a subscription; unsubscribe (asks first, then a button posts) |
| Converter | `/Converter?country=&bank=&currency=` | Converts at the latest rates of the bank chosen with the Country and Bank pickers: foreign → local uses the buy rate (transfer or cash), local → foreign the sell rate. The page sends the rates once; `wwwroot/js/converter.js` converts in the browser |

Spread = (sell − buy transfer) ÷ buy transfer. The design is in [`design/`](../../design/DESIGN_HANDOFF.md).

## Rate alerts (Subscribe)

The header's **Subscribe** button opens the "Get rate alerts" dialog (`Pages/Shared/_Subscribe.cshtml` and
`wwwroot/js/subscribe.js`). It asks for an email address, a country, and banks and currencies, each in a
checklist with a search field. Open any page with `#subscribe` to open the dialog straight away.

- `GET /alerts/options?country=` gives the countries, and the banks and currencies of one country.
- `POST /alerts/subscribe` (JSON, with the antiforgery token in a `RequestVerificationToken` header) sends
  the choices to Beep.Platform.Api's `/api/notification/subscribe`, with the page's language.

Both go through the server, so the API key never reaches the browser. Beep.Platform.Api sends the confirmation email
and the alerts (see [Beep.Notification](../Beep.Notification/README.md)).

## Languages

The language menu (top right) offers English, Tiếng Việt, ไทย, 中文, 日本語, 한국어, Français, Deutsch, Español,
Bahasa Indonesia, Bahasa Melayu, Nederlands and العربية. The choice is
kept for a year in the `.AspNetCore.Culture` cookie (`/language/{code}?returnUrl=...` sets it); without it, the
browser's language is used, else English. The site's texts and their translations are in
[`Localization/UiText.cs`](Localization/UiText.cs): each text takes all thirteen languages, so a missing
translation does not build. Numbers and dates keep one format in every language, and bank and currency names
are shown as Beep.Platform.Api returns them.

Arabic is written right to left: the page gets `dir="rtl"` (from the language's culture) and the
layout mirrors. The CSS uses start/end properties (`padding-inline-start`, `text-align: end`, …) rather
than left/right, so keep to those; the few rules that can't (select chevrons, arrows) are in the
"Right to left" block at the end of `site.css`.

## Configuration

| Key | Purpose |
| --- | --- |
| `BeepApi:BaseUrl` | Beep.Platform.Api's address, e.g. `https://localhost:55281/` |
| `BeepApi:ApiKey` | One of Beep.Platform.Api's `Authentication:ApiKeys` |

Set them in user secrets on a developer machine, or in environment variables or Key Vault (`KeyVault:Uri`).
The site does not start without them.

## Run

Start Beep.Platform.Api, then:

```bash
dotnet run --project src/Beep.Website.Currency
```

It listens on `https://localhost:55291`.
