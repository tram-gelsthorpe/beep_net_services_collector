# Beep Currency — redesign handoff (v5)

Three pages, each responsive (desktop and mobile ≤ 640px), sharing one stylesheet.

| File | What it is |
|---|---|
| `rates.html` | **Bank rates**: filter bar, USD/GBP/EUR highlight cards, bank card and rates table |
| `compare.html` | **Compare banks**: one currency across several banks |
| `converter.html` | **Converter**: working currency converter |
| `styles.css` | All styles and design tokens, shared by the three pages |
| `lang.js` | Language dropdown, shared by all pages |
| `subscribe.js` | **Subscribe** button and the "Get rate alerts" dialog, shared by all pages (open any page with `#subscribe` to see it straight away) |
| `background.png` | Full-page background image |

Open any page in a browser to see it working. Make the window narrower than 640px to see the mobile layout.

## Task for the implementer
Restyle the existing Beep Currency site to match these pages.
- **Keep what works.** Keep the existing data source, routes, query parameters (`country`, `date`, `bank`) and form behaviour. Change the look only, and add the two new pages.
- **Remove the old theme.** Take out the old green theme completely.
- **Use real data.** All numbers in the reference files are samples copied from the live site for Vietcombank on 26/11/2026. Wire every number to real data, including the converter rates.
- **Placeholders.** Values in square brackets, such as `[rate]`, `[Bank]`, `[USD buy]` and `[total]`, have no data behind them. Replace them with real data.

## Design tokens (in `:root` of `styles.css`)
| Token | Value | Use |
|---|---|---|
| `--ink` | `#0F172A` | main text, active toggle background |
| `--muted` | `#475467` | labels and secondary text |
| `--field` | `#F4F6FB` | form fields, Reset button, active nav pill, language button |
| `--accent` | `#2547D0` | links, globe icon, selected states |
| `--cta` | `#F97316` | main buttons (Show rates, Compare, swap), spread bars |
| `--sell` | `#1E3A8A` | sell-rate numbers |
| `--page-fallback` | `#1E2A78` | colour shown while the background image loads |
| radius | 16px cards, 10px fields and buttons, 999px pills | |

**Fonts** (from Google Fonts):
- **Space Grotesk:** logo, headings and currency symbols.
- **Inter:** body text.
- **IBM Plex Mono:** every number, so the columns line up.

**Currency colours:**

| Currency | Colour | Light tint |
|---|---|---|
| USD | `#0369A1` | `#E0F2FE` |
| GBP | `#6D28D9` | `#EDE9FE` |
| EUR | `#C2410C` | `#FFEDD5` |

The currency code pills in tables cycle through five tints: sky, violet, orange, pink and yellow.

## Shared layout
- **Dropdown width rule.** Every dropdown list is exactly as wide as the control that opens it (Countries list = Countries field; language menu = language button). On desktop the language button is at least 150px wide and the menu shows just the language names. The mobile language menu is the one exception: the mobile button only shows "EN", so its menu stays 250px wide.
- **Width.** Content is at most 1180px wide, with a 32px gutter (16px on mobile). The header, filter bar, cards and tables share this exact width, so their left and right edges line up.
- **Background.** `background.png` is set to `cover`, anchored to the top, on the whole page. Text that sits directly on the background is white.
- **Header.** It has no bar behind it. It contains:
  - a round dark-grey `#4B5563` "B" logo with the white "Beep Currency" name;
  - white nav links, all 36px tall, where the active page is a pill with the `--field` background and dark text;
  - the language button, using the same 36px pill style.
- **Mobile header.** The nav links hide behind a round menu button, the language button shows only the language code (e.g. "EN"), and each page shows a white page title under the header.
- **Language menu.** Open the menu to see a white list of 240–250px with a "Language" heading and seven languages: EN, VI, TH, ZH, JA, KO, FR. The selected language is tinted and has a checkmark. On mobile, the rest of the page dims behind it. Escape, clicking outside or picking a language closes it. The choice is saved in `localStorage`.
- **Buttons.** Reset and Show rates are as tall as the fields beside them, and never shorter than 48px.

## Pages
1. **Bank rates.** Desktop and mobile layouts:
   - **Filter bar:** Country, Date and Bank name or BIC fields, then the Reset and Show rates buttons.
   - **Highlight cards:** USD, GBP and EUR, each with its own colour and a 4px coloured top border.
   - **Bank card:** a round VCB badge, chips for BIC, rate date and publish time, a currency filter, and the rates table.
   - **Rates table:** five columns, with the spread shown as an orange bar.
   - **Mobile:** the highlight cards become a single list. The table shows only Currency, Buy transfer and Sell.
2. **Compare banks:**
   - **Countries:** a multi-select dropdown checklist. When closed, it shows the chosen countries ("Vietnam", "Vietnam, Thailand", "Vietnam +3 more" or "All countries") and a count. When open, it shows a search field, an "All countries" row (a dash when only some are ticked), a list that shows 3 rows and scrolls, and a footer with a hint and a Done button.
     - *One country:* banks are compared on price in that country's currency, including "You pay for <amount>".
     - *Two or more countries:* each bank quotes in its own currency, so banks are compared by **spread %**. The table gains a Country column with a local-currency label, shows only the chosen countries, and has "+ Add a country". The summary cards change to lowest spread anywhere, best country (average spread) and coverage.
   - **Filter layout:** two rows of three equal columns. Row 1 is Countries, Currency and Date. Row 2 is I want to, Amount and the Compare button, which fills its column. Amount is shown only when one country is chosen; then the Compare button widens to fill the gap. The box has 20px × 22px padding and 16px gaps, the same as the summary cards.
   - **Filter bar:** Currency, Amount and Date, plus a "Buy / Sell" toggle and the Compare button.
   - **Summary cards:** cheapest bank to buy from, best bank to sell to, and the difference between the best and worst bank.
   - **Table:** sortable by sell rate, buy rate or spread, with a "You pay for <amount>" column (amount × sell rate) and an "+ Add a bank" button.
   - **Mobile:** each bank becomes a card.
3. **Converter:**
   - **Country and Bank:** two pickers at the top choose which bank's rates the converter uses. Changing either one reloads the rates.
   - **Direction:** foreign → VND uses the bank's buy rate; VND → foreign uses the sell rate. Users can switch direction, and the orange button swaps it.
   - **Rate choice:** Transfer or Cash. Cash is disabled when the bank has no cash rate.
   - **Quick amounts:** preset amount chips.
   - **Bank card:** shows the chosen currency's three rates.
   - **Currency tiles:** "1 unit in VND today". Clicking a tile switches the converter to that currency.

4. **Subscribe (rate alerts)**, on every page:
   - **Header button:** a green (`#15803D`) pill with a bell icon and "Subscribe". On mobile it shows only the round green bell icon.
   - **Dialog:** a centred pop-up (max 600px) over a darkened page on desktop; a sheet that slides up from the bottom on mobile. Close with ✕, Escape or clicking outside.
   - **Fields:**
     - Email address.
     - Country: changing it loads that country's banks and clears the bank selection.
     - Banks and Currencies, each a **checklist box**. From top to bottom: a search field, a "Select all" row (shows a dash when only some are selected), then a list that shows **3 rows** and scrolls for more. A count above each box says, for example, "3 of 30 selected". This works for lists of 50+ banks.
   - **Validation:** the email must be valid, and at least one bank and one currency must be ticked. Otherwise a red message shows under that field.
   - **On submit:** POST `{ email, country, banks[], currencies[] }` to the backend (see the TODO in `subscribe.js`). Then show "You're subscribed", repeating the choices, with a note to confirm by email and **Edit alerts** / **Done** buttons.
   - **Backend still needed:** storing subscriptions, double opt-in confirmation email, sending one email per rate update, and an unsubscribe link in every email.

## Formulas
- Spread = (sell − buy transfer) ÷ buy transfer. Confirm this is the definition you want.
- Compare, "You pay" = amount × sell rate.
- Converter, foreign → VND = amount × buy rate (transfer or cash).
- Converter, VND → foreign = amount ÷ sell rate.

## Open items
- **USD.** The USD rates are placeholders. The live site has them.
- **Banks and countries.** The banks on Compare (BIDV, VietinBank, Techcombank, ACB) and the countries in All-countries mode (Thailand, Singapore, Malaysia, Philippines) are examples. Use the ones the site really covers.
- **Subscribe data.** The 30 Vietnamese banks in the Subscribe form are real names used to show a long list; the other countries' banks are placeholders. Load both from the site's data.
- **Languages.** The language list is a proposal. Remove any languages the site will not support.
- **Accessibility.** The 36px header buttons are smaller than the 44px recommended for touch on mobile. Increase them there if needed.
