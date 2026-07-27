# 08 — Frontend & UX

## Design stance

**Professional enterprise minimalism.** The reference points are Odoo POS's split-pane efficiency
and a trading terminal's information density — not a marketing dashboard.

Hard rules (these are review-blocking):

- **No bento grids.** No card-in-card-in-card. Panels are delimited by a 1px border and whitespace.
- **No gradients, no glow, no glassmorphism, no neon accents.** Flat Slate/Zinc surfaces.
- **No decorative iconography.** An icon appears only when it is faster to parse than its label.
- **Colour is semantic, never decorative.** Colour carries exactly four meanings: *success/committed*
  (emerald 600), *warning/attention* (amber 500), *destructive/negative* (red 600), *live/streaming*
  (sky 500). Everything else is neutral.
- **Type is the hierarchy.** One family (Inter/system), four sizes, three weights. Tabular numerals
  (`font-variant-numeric: tabular-nums`) for every quantity, price and total — non-negotiable for
  scannable columns.
- **Density over air.** Grid row height 32px (comfortable) / 28px (compact). The POS list shows
  ≥ 12 lines without scrolling at 1366×768.

### Token sketch

```
surface        zinc-50   / zinc-950         (light / dark)
panel          white     / zinc-900
border         zinc-200  / zinc-800
text           zinc-900  / zinc-100
text-muted     zinc-500  / zinc-400
accent         zinc-900  / zinc-100         (primary action = high contrast, not a colour)
positive emerald-600 · warning amber-500 · negative red-600 · live sky-500
radius         4px (2px on dense controls) · shadow: none except overlays
```

Dark mode is first-class: POS terminals sit under fluorescent light all day and staff prefer dark;
back office defaults to light.

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

## Accessibility & ergonomics

- Everything reachable by keyboard; visible focus rings (2px, offset).
- Contrast ≥ 4.5:1 for text, ≥ 3:1 for UI boundaries, in both themes.
- Touch targets ≥ 44px on POS controls (touchscreen tills are common; the legacy system had "Pad"
  buttons for exactly this, p.7).
- Screen-reader labels on all icon-only controls; `aria-live="polite"` on the totals region so
  totals are announced.
- Respects `prefers-reduced-motion`.
- Target hardware: 1366×768 minimum for POS, 1440×900+ for back office; layout degrades to a
  stacked single column on tablets for handheld RFID stocktaking.

## Performance budgets (POS route)

| Metric | Budget |
|---|---|
| Route JS (gzipped) | ≤ 180 KB |
| Time to interactive (cold, local network) | ≤ 1.5 s |
| Keystroke → visible response | ≤ 50 ms |
| RFID tag → line rendered (p95) | ≤ 300 ms end-to-end |
| Cart quote round trip (p95) | ≤ 120 ms |

Enforced by a bundle-size check and a Playwright performance assertion in CI.
