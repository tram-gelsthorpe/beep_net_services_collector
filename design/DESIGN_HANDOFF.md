# Beep Currency — "Latest bank rates" redesign handoff

Files in this folder:
- `beep-redesign.html` — working reference page (plain HTML/CSS/JS). Open it in a browser; resize below 640px to see mobile.
- `background.png` — full-page background image.
- `DESIGN_HANDOFF.md` — this spec.

## Task for the implementer
Restyle the existing **Latest bank rates** page to match `beep-redesign.html`.
- Keep the existing data source, routes, query params (`country`, `date`, `bank`) and form behaviour. Only the look changes.
- Replace the green theme entirely. No green anywhere.
- Rates in the reference are sample data copied from the live page; wire everything to real data.

## Design tokens
| Token | Value | Use |
|---|---|---|
| ink | `#0F172A` | main text |
| ink-2 | `#344054` | currency names |
| muted | `#475467` | labels, secondary text |
| field | `#F4F6FB` | form field bg, Reset button, active nav pill, language button |
| accent | `#2547D0` | links, globe icon, VCB badge text |
| accent-tint | `#E0E7FF` | bank badge / BIC chip bg |
| cta | `#F97316` | "Show rates" button, spread bars |
| sell | `#1E3A8A` | sell column numbers |
| page fallback | `#2A3BB0` | shown while background image loads |
| card radius | 16px | white boxes |
| field/button radius | 10px | fields, Reset/Show rates |

Fonts (Google Fonts): **Space Grotesk** (logo, headings, currency codes), **Inter** (body), **IBM Plex Mono** (all numbers, tabular).

## Layout
- Content width: 1180px max, 32px side gutter (16px on mobile). Header, filter bar, cards and table all share this exact width — left/right edges line up.
- Background: `background.png`, `cover`, anchored top, over the whole page.
- Spacing: ~26px above header buttons, 18px between header and filter bar, 32px to the cards, 24px between sections.

## Components
1. **Header** (on the background, no bar behind it): round dark-grey `#4B5563` "B" logo + white "Beep Currency"; nav links white, active link = pill with `field` bg + dark text, all 36px tall; language button on the right, same pill style, 36px.
2. **Language dropdown**: globe + current language + chevron. Opens a white menu (240px, 16px radius) listing EN, VI, TH, ZH, JA, KO, FR; selected row tinted `#EEF2FF` (list to be confirmed by product).
3. **Filter bar**: white card, 8px padding; Country / Date / Bank name fields with `field` bg; Reset (secondary, `field` bg) and Show rates (orange) buttons stretch to the same height as the fields. Country select has its native styling removed so text lines up with its label.
4. **Highlight cards**: USD, GBP, EUR. Each has its own colour (USD sky `#0369A1`, GBP violet `#6D28D9`, EUR orange `#C2410C`), symbol circle, spread badge, "We buy" (transfer) and "We sell". 4px coloured top border.
5. **Bank card**: round VCB badge, short bank name as title, full legal name below, chips for BIC, rate date, publish time.
6. **Rates table**: currency code in coloured pill (5 colours cycling), Buy cash / Buy transfer / Sell / Spread. Missing values shown as "—" in light grey. Spread bar = orange.
7. **Footnote**: white text with soft shadow under the table.

## Mobile (≤ 640px)
- Nav links hidden behind a round menu button; language button stays.
- Filter: Country + Date side by side, Bank full width, Reset + Show rates as two equal buttons.
- Highlight cards become one white list (code, sell large, buy small).
- Table can scroll horizontally, or render as a list (see the mobile artboard in the design).

## Open items
- **USD rates** are placeholders (`[USD buy]`, `[USD sell]`) — use real data.
- Spread = (sell − buy transfer) ÷ buy transfer. Confirm this is the definition you want.
- "Compare banks" and "Converter" links and the language list are proposals; remove if not needed.
