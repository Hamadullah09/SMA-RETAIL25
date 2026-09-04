# 08 — Frontend & UX

## Design stance

**Legible before it is dense.** The people using this are retail staff, often older, often on a
cheap panel at arm's length, often under pressure with somebody waiting — and mostly they did not
choose this software, they were handed it. The reference point is a well-made kiosk: obvious,
large, hard to get wrong. It is not a trading terminal, and it is not a marketing dashboard either.

> **This section was rewritten, and the previous version is worth knowing about.** It read
> *"professional enterprise minimalism — no gradients, colour is semantic never decorative, density
> over air, grid row height 32px"*, with a trading terminal as the model. That produced a screen
> that was uniformly grey, uniformly 14px, and navigable only by reading every label — fine for
> somebody who lives in it eight hours a day, and measurably not fine for the owner who opens the
> back office twice a week. The rules below are the deliberate replacement, not drift.

Hard rules (these are review-blocking):

- **No card-in-card-in-card.** Panels are delimited by a 1px border and whitespace. A panel does
  not nest inside another panel.
- **Colour carries two jobs, and they never share a surface.**
  - *Semantic* — what is happening: `positive`, `warning`, `negative`, `live`, `special`. These
    live in data, badges, statuses and totals.
  - *Domain tone* — where you are: the seven `tone-*` hues in `lib/tone.ts`. These live in
    navigation chrome only — a rail icon, the mark beside a page title, an index card's tile.

  The separation is the whole safety property. Because a domain tone never appears in a row, a
  badge or a total, amber-for-Stock and amber-for-warning are never read in the same context, and
  red goes on meaning exactly one thing. Tint a table row by section and colour stops being an
  alarm everywhere in the application; there is no partial version of that failure. Domain tones
  are also held at lower chroma than the semantics, enforced by a test, so a real alarm still
  out-shouts the furniture.
- **Colour is never the only carrier.** Every semantic appears as hue *and* glyph *and* word — see
  `StatusBadge`. Every domain tone sits beside a label. Nothing in the interface is lost by
  somebody who cannot separate two hues.
- **An icon appears when it is faster to parse than its label, or when it gives a label a
  landmark.** The second clause is new. A glyph on a tinted tile beside a rail row is not
  decoration: it is what lets an infrequent user go to *the amber one* instead of reading eleven
  words, and it is the same tile on the rail, the index card and the page header so that it is
  learned once.
- **Type is the hierarchy.** One family (Onest), a named scale, three weights. Nothing below 14px.
  Tabular numerals (`font-variant-numeric: tabular-nums`) for every quantity, price and total —
  non-negotiable for scannable columns.
- **Air where it buys comprehension, density where it buys throughput.** Controls are 48px and
  touch targets never below 44px, because a till is a touchscreen and a mistap costs a transaction.
  Grid rows are 48px. The POS list still shows ≥ 12 lines without scrolling at 1366×768 — that
  budget is unchanged, and it is what stops this rule becoming an excuse for a spacious screen that
  holds nothing.
- **Gradients only on the primary action and the sign-in hero.** `--accent-grad` exists so the one
  button that matters reads as raised. A gradient anywhere else is decoration and is refused.
- **Every destination is written in plain language.** "Products" and "Stock", never "Inventory"
  twice meaning two things; "Owed to you", not "Accounts receivable balance". The legacy guide's
  vocabulary wins over the accounting vocabulary wherever they differ.

### Tokens

Every colour is a CSS custom property in `globals.css`, surfaced to Tailwind by name in
`tailwind.config.js`. Nothing in a component names a colour directly.

```
surface  panel  panel-hover  panel-sunken       the four grounds; panels never nest
subtle  strong  control                          the borders that do the work cards would
ink  ink-muted  ink-faint                        three text weights, no more
accent  accent-strong  accent-soft  accent-text  one hue, four jobs
positive · warning · negative · live · special   what is happening — in data and badges
tone-home · sell · catalog · stock ·             where you are — in navigation chrome only
  people · supply · money
```

Each semantic and each tone has a `-text` tone and a `-soft` tint alongside the base: the base is
what you fill with, `-text` is the same colour at a lightness that can be *read*, and `-soft` is
the ground the pair sits on. Holding one value for all three is what previously left badges at
4.2:1 — a fill only has to be seen, but a word has to be read, and the two want different
lightnesses.

The accent tokens hold `L C H` and are consumed as `oklch(var(--accent) / <alpha-value>)`, which
keeps the alpha slot free so `bg-accent/10` still works. `soft` is the tint an active nav item sits
on; `text` is the same hue at a lightness that reads *on* that tint — one colour kept in step by
construction rather than three colours kept in step by hand.

Type is Onest for the interface and a mono face for anything that has to be read back aloud —
EPCs, stock codes, references. Sizes are a named scale (`caption` / `label` / `body` / `h3` …) with
line height travelling with the size, so a heading never needs correcting at the call site.

There is one theme. A dark mode was built and then removed: two palettes meant every colour
decision was made twice and verified twice, and the second one was where the contrast failures hid.
This is a shop-floor system whose users are told which screen to look at, not a personal tool where
a preference is worth a second palette. `contrast.test.ts` measures the shipped stylesheet, so the
one palette that exists is checked rather than trusted.

---

## The POS screen — Miller's Law, five regions

Exactly **five** functional groups are visible at rest. Everything else is nested behind an
explicit key or a contextual drawer.

```
┌──────────────────────────────────────────────────────────────────────────────────┐
│ ⑤ STATUS BAR   Station 001 · Loc TST · Sarah K. · Drawer open · ●RFID ●Printer   │
├──────────────────────────────────────────┬───────────────────────────────────────┤
│ ① CART / LIVE RFID FEED                  │ ④ CUSTOMER CONTEXT                    │
│                                          │  Jane Doe   #10432                    │
│  ┌ live feed strip ───────────────────┐  │  Level 2 · 5% usual · Tax2 exempt     │
│  │ ▸ 3 tags settling…                 │  │  Points 480/500 · Balance $214.00     │
│  └────────────────────────────────────┘  │  [F5 Client]                          │
│                                          ├───────────────────────────────────────┤
│  QTY  ITEM                    PRICE  EXT │ ② TOTALS                              │
│    1  COLUMBIA POLO           49.99 49.99│   Subtotal          148.97            │
│    2  JANSPORT ROCKIES  ᴿᶠᴵᴰ   34.99 69.98│   Discount          - 7.45            │
│  1.24 GROUND COFFEE ⚖         12.99 16.11│   GST 5%              7.08            │
│                                          │   PST 7%              9.91            │
│                                          │   ─────────────────────────           │
│                                          │   TOTAL             158.51            │
│                                          ├───────────────────────────────────────┤
│                                          │ ③ PAYMENT MATRIX                      │
│                                          │  [ CASH ] [ CREDIT ] [ DEBIT ]        │
│                                          │  [ GIFT ] [ ON ACCT ] [ SPLIT ]       │
├──────────────────────────────────────────┴───────────────────────────────────────┤
│ F2 Find  F3 Credits  F4 Pay  F5 Client  F6 Delete  F9 Save  F10 Drawer  F11 More │
└──────────────────────────────────────────────────────────────────────────────────┘
```

| Region | Content | Nested behind |
|---|---|---|
| ① Cart / Live RFID feed | line items + streaming tag strip | line detail drawer (price, qty, discount, tax, notes, weight) |
| ② Totals | subtotal, discounts, each tax by its configured name, add-on charge, total | tax breakdown popover |
| ③ Payment matrix | tender buttons, tendered/change | split tender dialog, currency picker, card flow |
| ④ Customer context | identity, price level, discount, exemptions, points, AR balance | invoices / quotes / layaways / history tabs |
| ⑤ Status bar | station, location, staff, drawer state, peripheral health | station settings, staff switch |

**Visual weighting**: `Pay` is the only filled high-contrast button on the screen. `Hold` and
`Cancel` are outlined. Overrides, notes and specials are text-weight entries inside menus. A
cashier's eye should land on *Pay* with no search.

### Line detail drawer (legacy Item Detail window, p.6)

Slides from the right, does not cover the cart. Fields in the legacy tab order: Quantity (focused
on open) → Price → Discount → Price level (F5) → Tax 1 (F6) → Tax 2 (F7) → Product info → Zero
Scale / Get Weight. `Enter` accepts, `F12` cancels — identical to the legacy contract, so muscle
memory transfers.

In **Fast Scan Mode** the drawer never opens; `F3 Detail` before ringing an item forces it (p.6).
Bulk RFID mode implies fast scan.

---

## Keyboard model

The POS is fully operable without a mouse (guide p.4: *"The POS system does not require a mouse"*).

| Key | Action | Legacy |
|---|---|---|
| `F2` | Find item / stock code entry (press again → pick list) | p.5 |
| `F3` | Credits menu → `F2` discount, `F3` coupon, `F4` return, `F5` gift certificate, `F6` bottle, `F7` trade-in, `F8` gift card balance | p.7 |
| `F4` | Pay / total window | p.8 |
| `F5` | Client menu (`SHIFT+F2..F12` mirror the legacy client shortcuts) | p.9, p.13 |
| `F6` | Delete last line | p.10 |
| `F7` | Reprint last sale | p.12 |
| `F8` | Packing slip | p.12 |
| `F9` | Save sale | p.10 |
| `F10` | Drawer menu → float / view / print / close / pay in / pay out / pop | p.10 |
| `F11` | Special → unknown item, void, suspend, recall, taxes, output, staff ID, hours | p.11 |
| `F12` | Close POS / cancel dialog | p.13 |
| `Ctrl+I` | Enter staff ID | p.13 |
| `Ctrl+K` | **Command palette** (new) — every action, searchable | — |
| `Ctrl+/` | Shortcut cheat sheet overlay | — |

Implementation: a single global hotkey registry (`lib/hotkeys`) with **scopes** (`pos`, `dialog`,
`grid`, `global`). Dialogs push a scope so `F4` inside the payment dialog means *Copies* (legacy
p.8), not *Pay*. Registered handlers are declarative, so the cheat sheet and the command palette are
generated from the same source — they can never drift from reality.

`Ctrl+K` covers everything the F-keys do plus navigation ("go to inventory", "find client Smith",
"open PO 1042"), with recent-actions ranking.

---

## Back office

Two-pane, matching the legacy mental model (guide ch. 3: Browse View + Form View with tabs) without
inheriting its problems.

```
┌ sidebar ─┬ Browse (DataGrid) ─────────────────────────────┐
│ Inventory│  filters · saved views · column reorder · split│
│ Customers│  ─────────────────────────────────────────────  │
│ Invoices │  ▸ live rows (SignalR patched, no refresh)     │
│ Suppliers├───────────────────────────────────────────────┤
│ POs      │  Form View tabs: Detail │ Sales │ Pricing │    │
│ Staff    │  Ordering │ Notes │ Matrix │ Kit │ Special      │
│ Reports  │                                                │
│ Settings │                                                │
└──────────┴────────────────────────────────────────────────┘
```

`DataGrid` capabilities, mapped from the legacy guide:

| Legacy | Modern |
|---|---|
| Drag to reorder columns; "Reset Column Order" | Same, persisted per user per grid |
| Split screen (two scroll regions) | Pinned/frozen leading columns + optional split view |
| Flags column (double-click to flag) | Multi-select checkboxes + "select all matching filter" |
| "Flag By Search" cumulative two-pass targeting | Saved filters with AND/OR and add-to/remove-from selection |
| Inline cell edit (click/double/triple-click) | Inline edit with optimistic update + conflict toast |
| "Open In MS-Excel" | Export XLSX/CSV of the current view (server-rendered) |
| Stale rows on a network (p.100) | **SignalR row patching** — the headline fix |

Grids are virtualized (`@tanstack/react-virtual`) and cursor-paginated; 50k-row inventories scroll
at 60fps.

## Realtime UX rules

- Live regions animate **once, briefly** (120ms fade), never pulse or loop. A POS screen that
  breathes is a POS screen nobody can read.
- Optimistic updates for local actions; the server's `CartUpdated` revision is authoritative and
  silently reconciles.
- Connection loss shows a persistent amber bar with a countdown to retry — never a modal, never a
  silent failure.
- Rejected RFID tags appear in the feed with a plain-language reason and stay visible for 10s.

## Branding & white-labelling

Two images per location, uploaded through Settings → Branding and stored in `branding_assets`.
Nothing about a particular customer is in the bundle, so a reseller opening a new shop changes two
images and ships no code.

**The watermark** is `position: fixed; inset: 0`, flex-centred, sized as a share of the shorter
viewport edge (`max-h-[45vmin] max-w-[60vmin]`) so it is the same size relative to the screen on a
1366×768 till and a 27-inch back-office monitor. A fixed pixel width fills one and vanishes on the
other.

It is drawn **under** the content, which is the opposite of what the word usually means. A mark at
20% laid over body text costs real contrast on every screen in the application, read all day by
people who did not choose the logo — and that fights the contrast rule three sections up. Underneath
it still carries across the gutters and the empty half of a till's item list, which at a counter is
most of the screen. Implementation: the watermark sits at `z-0` inside the root, and the chrome and
page sit in a sibling at `z-10`.

Two properties on it are load-bearing rather than decorative. `pointer-events: none`, because the
element covers the whole viewport and without it swallows every click in the application —
a total loss of function in exchange for a decoration. And `aria-hidden`, because a screen reader
announcing the company logo before every screen is noise; the shop's name is already in the header.

Opacity is stored per asset, defaulted to 20%, adjustable from the same panel. It is a default and
not a constant because a pale logo and a dark one do not carry at the same weight, and the only way
to find the right figure is to look at it.

**The company logo** sits in the header, sized by height alone (`h-7 w-auto max-w-[180px]`) so a
wide wordmark and a square badge both sit on the same baseline without anyone cropping either to a
template. Nothing renders when a store has not uploaded one — an unbranded installation looks
deliberate rather than broken.

Both are served from `/api/v1/locations/{id}/branding/{slot}` with a strong ETag and
`Cache-Control: private, max-age=86400, must-revalidate`, and the URL carries the ETag as a cache
buster. The server tag alone would get a browser to a replaced logo on its next revalidation, and
for a day-long max-age "next revalidation" is tomorrow — which reads to the administrator as a
failed upload, so they do it again.

## Accessibility & ergonomics

- Everything reachable by keyboard; visible focus rings (2px, offset).
- Contrast ≥ 4.5:1 for text, ≥ 3:1 for UI boundaries, in both themes. The watermark does not erode
  this, by construction — see above.
- Touch targets ≥ 44px on POS controls (touchscreen tills are common; the legacy system had "Pad"
  buttons for exactly this, p.7).
- Screen-reader labels on all icon-only controls; `aria-live="polite"` on the totals region so
  totals are announced.
- Respects `prefers-reduced-motion`.
- Target hardware: 1366×768 minimum for POS, 1440×900+ for back office; layout degrades to a
  stacked single column on tablets for handheld RFID stocktaking.

### Feedback for a scan

RFID removes the one signal a barcode scanner always gave: the beep. A cashier who passes an item
over a scanner and hears nothing tries again immediately. Waving a basket at an antenna gives them
nothing — and the screen is not a substitute, because at the moment the item is scanned the cashier
is looking at the item.

Three channels, all at once:

| | |
|---|---|
| **Audible** | Three distinguishable tones — one short high blip for accepted, two lower blips for refused, one longer mid tone for a tag nothing recognises. Three rather than one, because "did that work" is exactly the question being asked and a single beep answers it wrongly half the time. Synthesised with the Web Audio API: no assets, no network, tunable in place. One tone per batch, not per line — a basket of thirty is one action, and thirty blips is an alarm. |
| **On the list** | The line appears in the sale, priced. |
| **On the tag feed** | Every tag in the field, including the ones that do not resolve. An unknown tag is the most interesting row on that panel: it is either stock nobody commissioned or a customer's own coat, and the difference matters. |

Sound is on by default and switchable from the tag feed itself rather than from a settings screen —
three tills within earshot is three beeps nobody can attribute, and the person who needs it off is
standing at the counter. Pressing the control plays the tone it is enabling, which answers "is this
working" and doubles as the user gesture browsers require before an `AudioContext` will make any
sound at all.

## Performance budgets (POS route)

| Metric | Budget |
|---|---|
| Route JS (gzipped) | ≤ 180 KB |
| Time to interactive (cold, local network) | ≤ 1.5 s |
| Keystroke → visible response | ≤ 50 ms |
| RFID tag → line rendered (p95) | ≤ 300 ms end-to-end |
| Cart quote round trip (p95) | ≤ 120 ms |

Enforced by a bundle-size check and a Playwright performance assertion in CI.
