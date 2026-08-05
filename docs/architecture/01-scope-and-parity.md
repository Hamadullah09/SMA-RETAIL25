# 01 — Scope & Legacy Parity Matrix

The brief's functional matrix is a **subset** of what Retail Plus 2.5 actually does. The user guide
is the parity contract. This document enumerates every feature in the guide, states its modern
replacement, and assigns a delivery phase.

Legend — **Status**: `PARITY` (behaviour preserved) · `MODERNIZED` (same outcome, new mechanism) ·
`NEW` (no legacy equivalent) · `DROPPED` (deliberately removed, with reason).

---

## 1. Point of Sale (Guide ch. 2)

| Legacy feature | Guide ref | Status | Modern design | Phase |
|---|---|---|---|---|
| F2 Find Item — stock code entry, barcode scan, pick-list lookup | p.5 | PARITY | `POST /carts/{id}/lines` with `IdentifierResolver` chain: EPC → stock code → UPC → Type 2 weight → matrix/serial prompt | 3 |
| **Bulk RFID reading** | — | NEW | Continuous LLRP stream → agent → SignalR → server cart | 4 |
| Item Detail window (qty, price, discount, tax override, price level F5, tax override F6/F7) | p.6 | PARITY | Line-edit drawer; same F-keys | 3 |
| Fast Scan Mode (suppress detail window; F3 to force it) | p.6, p.77 | PARITY | Station setting `fastScanMode`; RFID bulk mode implies it | 3 |
| Product Info / Notes at POS | p.7 | PARITY | Notes panel in line drawer | 3 |
| Zero Scale / Get Weight buttons | p.7 | PARITY | Agent serial scale commands (`W`/`Z` configurable) | 4 |
| Touch keypad ("Pad" buttons) | p.7 | PARITY | On-screen numpad component, touch-target sized | 3 |
| F3 Credits: **Discount** (subtotal) | p.7 | PARITY | Cart-level discount, permission-gated (`Staff May Discount`) | 3 |
| F3 Credits: **Coupon** (name + value) | p.7 | PARITY | `CartAdjustment{Type=Coupon}` | 3 |
| F3 Credits: **Return** (± return to inventory) | p.7 | PARITY | Negative line + `RestockFlag`; stock ledger `ReturnIn` | 3 |
| F3 Credits: **Gift Certificate** (serial + value) | p.7 | PARITY | `GiftCertificate` entity, serial-tracked | 5 |
| F3 Credits: **Bottle** deposit return | p.7 | PARITY | `CartAdjustment{Type=BottleDeposit}` | 3 |
| F3 Credits: **Trade In** (must exist in inventory) | p.7 | PARITY | Negative line, `Reason=TradeIn`, stock ledger `TradeInIn` | 3 |
| F3 Credits: **Gift Card Balance** inquiry | p.7 | PARITY | `IGiftCardProvider.GetBalanceAsync` | 5 |
| F4 Pay — total window, copies, preview | p.8 | PARITY | Payment matrix panel | 3 |
| **Split tender** (2 methods) | p.8 | MODERNIZED | *N* tenders, not 2. Cash/Credit/Debit/Gift/Cheque/On-Account/Foreign | 3/5 |
| F10 Currency — up to 5 FX rates | p.9, p.17 | PARITY | `Currency` + `ExchangeRate` tables; tender in FX, ledger in base | 5 |
| F11 Recalc split | p.9 | PARITY | Client-side derived; server validates sum | 3 |
| ENTER Print + Auto Save | p.8, p.77 | PARITY | `CompleteSaleCommand`; `autoSaveSales` station setting | 3 |
| F9 Save (no print) | p.10 | PARITY | Same command, `print=false` | 3 |
| F5 Client functions (find/new/edit/ship-to/clear/history/delete) | p.9 | PARITY | Customer context pane + Ctrl+K | 3 |
| Client Invoices / Quotes / Layaways from POS | p.9 | PARITY | Tabs in customer pane | 5 |
| F6 Delete line | p.10 | PARITY | Line delete, audit-logged | 3 |
| F10 Drawer: Float / View / Print / Save(close) / Pay Out / Pay In / Pop | p.10–11 | PARITY | `DrawerSession` aggregate + `DrawerLedger`; Pop = ESC/POS pulse via agent | 3 |
| F11 Special: **U/I Unknown Item** (sell ad-hoc, optionally create item) | p.11 | PARITY | `AddUnknownItemCommand`; if catalog fields supplied, creates `Product` | 3 |
| F11 Special: **Void** sale (supervisor password) | p.11 | PARITY | Permission `Sales.Void`; step-up re-auth | 3 |
| F11 Special: **Suspend / Recall** | p.11 | PARITY | `CartStatus=Suspended`, visible to all stations via SignalR | 3 |
| F11 Special: **Taxes** — per-sale tax & add-on suspension | p.11 | PARITY | `CartTaxOverride`; applies to lines added *after* the override (legacy semantics preserved) | 3 |
| F11 Special: Output — printer style, **reprint last / by number**, header/footer edit, port | p.12 | MODERNIZED | Receipt render stored per transaction (unbounded history, not 600); reprint by number | 3 |
| F11 Special: Staff ID / Hours (clock in-out) | p.12 | PARITY | `TimeClockEntry`; `StaffId` on every transaction | 6 |
| F7 Reprint / F8 Packing Slip | p.12 | PARITY | Document service (QuestPDF + ESC/POS) | 3 |
| POS Toolbar: envelope (COM10), shipping label (Avery S-644N), email invoice | p.13 | PARITY | Label/envelope PDF templates; email via SMTP adapter | 6 |
| Itemized Sales Log (+ void from log, flags, export, Excel, print) | p.14 | MODERNIZED | Immutable `SaleLine` ledger + saved views + CSV/XLSX export. **No delete** — retention policy instead | 3/6 |
| POS History / exit totals (`TOTAL001.DBF`), date-range & tax reports | p.15 | PARITY | `DrawerSession` close snapshots; reports over ledger | 3/6 |
| Customer Order Log / back orders / Fill This Order | p.16 | PARITY | `CustomerOrder` aggregate; fill → cart | 5 |
| Import sales from another workstation / cash-register CSV (`.ASC`) | p.16–17 | MODERNIZED | Real-time API/SignalR replaces file exchange; CSV importer retained for third-party registers | 7 |
| Payment Options list (editable tender types) | p.17 | PARITY | `TenderType` reference table | 3 |
| Invoice header/footer text (7 + 2 lines) | p.17 | PARITY | `DocumentTemplate` settings | 3 |
| Bonus/reward points (points per $, minimum, % or fixed reward, print on slip) | p.83–84 | PARITY | `LoyaltyPolicy` + `LoyaltyLedger`; legacy rules preserved incl. "no reward if subtotal already discounted" | 5 |

## 2. Inventory (Guide ch. 3)

| Legacy feature | Guide ref | Status | Modern design | Phase |
|---|---|---|---|---|
| Item Types: Standard, **Matrix**, **Serialized**, **Kit**, Non-Stock, Rental, Service, Shipping, Admission, **Gift Card** | p.30–31, p.106 | PARITY | `ProductType` enum drives behaviour strategies | 2 |
| Departments / Categories (user-defined lists) | p.31 | PARITY | `Department`, `Category` entities | 2 |
| Taxable flags (Tax 1 / Tax 2) per item | p.31 | PARITY | `Product.Tax1Applies`, `Tax2Applies` | 2 |
| Bin location, Qty in stock, Last sold, Last cost, **Avg cost**, Regular price, **Gross margin** | p.31–32 | PARITY | Moving-average cost maintained by stock ledger; margin = `(price-cost)/price*100` | 2 |
| Sales tab — monthly volume/gross, **per-item commissions** (%, fixed, % of profit, max, Update All) | p.33 | PARITY | `MonthlySalesSnapshot` (derived), `CommissionRule` | 6 |
| Pricing tab — **4 price levels**, **break points**, **sale pricing (date range)**, **bonus/BOGO pricing** | p.34–35 | PARITY | `ProductPrice`, `PriceBreak`, `SalePricing`, `BonusPricing` — see [04](04-pricing-and-tax-engine.md) | 3 |
| Ordering tab — base stock, reorder point, reorder qty, on order, order date, last supplier, last shipment, cust. orders, **ranked supplier list** (add/edit/remove/promote/demote) | p.36–37 | PARITY | `ProductSupplier` with `Rank`, `Cost`, `ReorderNumber` | 5 |
| Notes tab — catalogue description, **product photo** | p.38 | MODERNIZED | Rich text notes; images via object storage (not 240×200 BMP) | 2 |
| Matrix tab — 1–3 user-named dimensions, per-combination stock, clone/clear matrix | p.39–40 | PARITY | `MatrixDimension`, `ProductVariant` | 4 |
| Kit tab — components with quantities, explode on sale | p.41 | PARITY | `KitComponent`; sale explodes to component stock movements | 5 |
| Special tab — **serial numbers** (add/edit/delete/print, pick at sale) | p.42 | PARITY | `SerializedUnit` — unified with RFID EPC | 4 |
| Special tab — **substitute item**, **tag-along item**, **parent/child case break**, ship weight, case qty | p.42–43 | PARITY | Self-referencing `Product` links; `CaseQty` drives break-case stock move | 5 |
| Special tab — **message on POS screen**, **message on invoice** | p.43–44 | PARITY | `Product.PosMessage`, `InvoiceMessage` | 3 |
| Receive Stock (manual, item by item) | p.20 | PARITY | `ReceiveStockCommand` → stock ledger | 5 |
| **Transfer Stock** between locations (out/in, internet transfer) | p.20–21, p.94 | MODERNIZED | `StockTransfer` aggregate with `InTransit` state over API — no file exchange | 6 |
| Batch price adjustments (flagged items, %/fixed, per price level) | p.21–22 | PARITY | Bulk command over a selection | 6 |
| **Batch onhand adjustments** from stock-counter CSV + variance/shrinkage report | p.22 | PARITY | `StockCount` session, variance report, post-to-ledger | 6 |
| Manual onhand adjustments | p.22 | PARITY | `AdjustStockCommand` with reason code | 5 |
| Print item / **labels & price tags** / price list / catalogue | p.22–23 | PARITY | Label engine: Avery + custom, **Code 39 rendered server-side** (no TTF install), plus RFID tag encoding | 6 |
| Add / Clone / Delete / Undelete item | p.23–24 | MODERNIZED | Soft delete + restore; "Undelete" becomes a real audit-backed restore | 2 |
| Flags / Flag By Search (cumulative two-pass selection) | p.27–28 | MODERNIZED | Saved filters + multi-select + "select all matching" — same power, no hidden global state | 6 |
| Set Taxes on flagged items | p.28 | PARITY | Bulk command | 6 |
| Import inventory from CSV (documented field order) | p.28 | PARITY | CSV importer with the legacy field order as a preset | 7 |
| Import from Retail Plus 1.5 / DOS / **2.5 DBF** | p.28, p.103 | PARITY | `Retail25.Migration` DBF/FPT reader | 7 |
| Export inventory (`.DTA` field order) | p.28 | PARITY | CSV/XLSX export | 6 |
| Duplicate check (stock code) | p.29 | MODERNIZED | Unique index per location; report for pre-existing dupes at import | 2 |
| **Year-End close** (archive, clear histories, roll monthly to last-year) | p.29 | PARITY | `FiscalYearClose` job — archives to `sales_history_archive`, no data destroyed | 6 |
| Rebuild Indexes / Reindex | p.29 | DROPPED | SQL Server maintains its own indexes. Reason: the failure mode does not exist. |
| Multiple inventories / **Locations** (3-char code), New/Select/Delete | p.44 | PARITY | `Location` entity; legacy 3-char code kept as `LegacyCode` | 2 |
| Reports: Sales, Inventory (all/combined/separate counts, hide onhand), Top Sellers, Analysis, Stock Value, Understock/Overstock, On Order, Stock Received | p.25–27 | PARITY | Report module; Overstock keeps the legacy heuristic (3-week sales, on-order, base stock) | 6 |

## 3. Clients / CRM (ch. 4)

| Legacy feature | Guide ref | Status | Design | Phase |
|---|---|---|---|---|
| Client record: name, company, address, phone/fax/mobile, email, client type, customer number | p.46–50 | PARITY | `Customer` aggregate | 2 |
| Purchase history per client (with ~400-sale limit) | p.51, p.97 | MODERNIZED | Unlimited history via ledger query. Legacy cap removed | 3 |
| Account number, **credit limit** (0 = unlimited — preserved) | p.51 | PARITY | `CustomerAccount` | 5 |
| Usual discount %, **reward points**, **tax exemptions**, **assigned price level**, ship-to address | p.51–52 | PARITY | `CustomerPricingProfile` | 3 |
| Client photo | p.52 | MODERNIZED | Object storage | 6 |
| Invoices / Quotes / Layaways buttons | p.51 | PARITY | Related-records tabs | 5 |
| Print: client, list, envelope (COM10), mailing labels (Avery 5160/8160), shipping labels (8163), open layaways, reward points | p.46 | PARITY | Document/label engine | 6 |
| Import clients from CSV (14 documented fields) / 1.5 / DOS | p.48–49 | PARITY | Importer preset matches documented field order exactly | 7 |
| Export clients (flagged, optional `AA`-prefixed header row) | p.49 | MODERNIZED | CSV/XLSX export (no `AA` hack needed) | 6 |
| Duplicate check on customer number | p.48 | MODERNIZED | Unique constraint + import report | 2 |
| Targeted marketing selects ("everyone who bought a widget last year") | p.45, p.97 | PARITY | Segment builder over the sales ledger | 6 |

## 4. Invoices / Accounts Receivable (ch. 5)

| Legacy feature | Guide ref | Status | Design | Phase |
|---|---|---|---|---|
| "On Account" tender creates an invoice | p.53 | PARITY | `Invoice` created by `CompleteSaleCommand` when an AR tender is present | 5 |
| Payments, **partial payments**, payment method, back-dating | p.58 | PARITY | `InvoicePayment` → `ARLedger` | 5 |
| **Distribute payment** across open invoices, oldest first | p.58 | PARITY | `DistributePaymentCommand` — exact legacy allocation order | 5 |
| **Late charges**: monthly interest %, grace period, payment applied to penalty first, re-accrual from last payment date | p.56, p.84 | PARITY | `LateChargePolicy` + nightly accrual job. Legacy subtleties preserved verbatim | 5 |
| Void invoice (balance → 0, void notice retained in history) | p.57 | PARITY | Reversing ledger entries; nothing deleted | 5 |
| Refund payments | p.57 | PARITY | `RefundPaymentCommand` | 5 |
| Print current / flagged / **account statements (date range)** / **receivables summary** | p.54 | PARITY | Document engine | 6 |
| Invoice header/footer (7+2 lines, first line larger) | p.55 | PARITY | `DocumentTemplate` | 3 |
| Reprint uses taxes **as at sale time** | p.56 | PARITY | Enforced by tax snapshot columns — see [04](04-pricing-and-tax-engine.md) | 3 |
| Delete invoice / delete all flagged | p.54 | DROPPED | Replaced by void + retention policy. Reason: destroying financial records is a defect, not a feature. |

## 5. Suppliers & Purchasing (ch. 6–7)

| Legacy feature | Guide ref | Status | Design | Phase |
|---|---|---|---|---|
| Supplier record (contact, company, address, phone/fax/mobile, supplier number, email) | p.59–62 | PARITY | `Supplier` aggregate | 2 |
| Import suppliers CSV (15 documented fields) | p.61–62 | PARITY | Importer preset | 7 |
| PO creation scopes: all from supplier / flagged only / customer orders only / all inventory | p.63–64 | PARITY | `GeneratePurchaseOrderCommand(scope)` | 5 |
| Supplier selection: **preferred** vs **lowest cost** | p.64 | PARITY | Strategy on `ProductSupplier` | 5 |
| Order quantity formulas: **blank**, **1 week**, **2 weeks**, **reorder point (2 variants)**, **monthly sales (vs last year)** | p.64–65 | PARITY | `IOrderQuantityStrategy` — 6 implementations, formulas per guide | 5 |
| PO review grid (order qty, case qty, cost each, order cost, qty recvd, in stock, on order, min stock, reorder pt/qty, back orders) | p.66–67 | PARITY | PO editor grid | 5 |
| Split-case ordering (order 1.5 cases = 18 items) | p.66 | PARITY | Decimal order qty × case qty | 5 |
| **Post Order** → updates On Order + Order Date (deliberately separate from printing) | p.67 | PARITY | Explicit `PostOrderCommand`; the guide's Christmas-order rationale still holds | 5 |
| **Post Shipment** → In Stock, Last Cost, **Avg Cost**, flag received for labels, **distribute shipping cost across items** | p.67–68 | PARITY | `PostShipmentCommand` with landed-cost allocation | 5 |
| Partial receipt / qty recvd correction | p.67 | PARITY | Multiple receipts per PO line | 5 |
| Print PO (split per supplier, supplier address, **supplier stock code**, PO header text) | p.68–69 | PARITY | Document engine | 5 |
| Matrix orders (qty per variant) | p.74 | PARITY | PO lines reference `ProductVariant` | 5 |
| Export PO to CSV | p.71 | PARITY | Export | 5 |
| **Web Submit** to supplier server | p.71 | MODERNIZED | `ISupplierPortal` adapter (EDI 850 / REST / email PDF) — deferred | 8 |
| **QuickBooks → A/P bill** with due date (default +30 days) | p.71, p.112–113 | MODERNIZED | `IAccountingConnector.PostBillAsync` | 6 |

## 6. Staff (ch. 8)

| Legacy feature | Guide ref | Status | Design | Phase |
|---|---|---|---|---|
| Staff records, staff ID before each sale | p.75, p.82 | PARITY | Identity user + `StaffProfile`; PIN/badge fast-switch at POS | 3 |
| Clock in / clock out, recalc hours | p.75–76 | PARITY | `TimeClockEntry` | 6 |
| Commissions (per item rules) + hours/commission report, date range | p.76 | PARITY | `CommissionCalculator` over the sales ledger | 6 |

## 7. Configuration (ch. 9)

| Legacy setting group | Guide ref | Status | Design | Phase |
|---|---|---|---|---|
| Business ID (name, address, license) | p.76 | PARITY | `Tenant`/`BusinessProfile`. License validation dropped. | 2 |
| **Taxes**: Tax1/Tax2 name+rate, **Tax2 compound**, add-on charge name/rate/taxable, **tax inclusive vs exclusive**, tax registration no. | p.76–77 | PARITY | `TaxConfiguration` — see [04](04-pricing-and-tax-engine.md) | 2 |
| POS defaults: apply tax1/2, allow tax override, apply add-on, CC signature line, client name on slip, carry over city/state/zip, allow item-list edit, staff may discount, fast scan, auto save, **scan random weight barcodes**, confirm before saving, go to POS on startup, **station ID (001–999)**, default payment method | p.77–78 | PARITY | `StationSettings` (per station) + `PosPolicy` (global) | 3 |
| Slip printer: setup/cutter/red/black escape codes, port, copies, page eject, extra CC copy, serial init, **20/40-col vs invoice output** | p.78–80 | PARITY | `PrinterProfile` consumed by the agent | 4 |
| Cash drawer: **trigger command** (`27,112,0,50,250` Epson / `07` Star), port, repeat count, open-on-print | p.80 | PARITY | `DrawerProfile` on the agent | 4 |
| Pole display: port, scrolling idle (45 ch) + fixed idle (19 ch) messages | p.80–81 | PARITY | Agent serial writer, PD3000-compatible | 4 |
| Pinpad (Verifone 1000 / SC5000, DSIClient-X) | p.81, p.105 | MODERNIZED | Modern EMV terminal via `IPaymentGateway` (see Q1) | 5 |
| Weigh scale: enable, comms type, **Get Weight char (`W`)**, **Zero char (`Z`)** | p.81 | PARITY | Agent `System.IO.Ports` driver | 4 |
| Users: require passwords, supervisor password to void, track staff sales, time clock, **access levels 0–4** | p.81–83 | MODERNIZED | Role + permission model mapped from levels — see [07](07-security-and-identity.md) | 1 |
| Assigned numbers: next customer / invoice / PO number | p.83 | PARITY | SQL Server sequences per location, seeded from legacy values | 2 |
| Bonus points setup | p.83–84 | PARITY | `LoyaltyPolicy` | 5 |
| Overdue finance charges (rate, grace) | p.84 | PARITY | `LateChargePolicy` | 5 |
| **Minimum tender** (smallest coin, default 0.01) | p.84 | PARITY | Drives cash rounding | 3 |
| UK/US date format | p.84 | MODERNIZED | Full i18n via locale, not a boolean | 2 |

## 8. Backup / Comms / Appendices (ch. 10–11, A–Q)

| Legacy feature | Guide ref | Status | Design | Phase |
|---|---|---|---|---|
| Backup / Restore / **Rebuild** to floppy/CD/tape | p.86–88 | MODERNIZED | `pg_dump` + WAL archiving + PITR; restore runbook. Rebuild dropped. | 0 |
| FTP: sales logs, stock levels, stock transfers, stock updates, generic file transfer | p.90–95 | MODERNIZED | Single database + SignalR + REST. Multi-site: outbox replication. FTP dropped entirely. | 6 |
| Networking: map a drive letter, share host `C:`, Error 21/23 | p.100–101 | DROPPED | Reason: HTTP over TLS. This class of failure is designed out. |
| Browse-window staleness on multi-user networks (p.100–101) | p.100 | MODERNIZED | **SignalR live grids** — this is exactly the legacy complaint the brief asks to fix | 3 |
| **Type 2 random-weight barcodes** `2ABBBBCDDDDE` (5-digit item code `ABBBB`, embedded price `DDDD`, qty = embedded price ÷ Price 1; blank Price 1 ⇒ qty 1; price override ⇒ treated as unit price × derived weight) | p.98 | PARITY | `RandomWeightBarcodeParser` + the exact division/rounding semantics | 3 |
| **Code 39 barcode font (3of9.ttf)** | p.104 | MODERNIZED | Server-side Code 39 rendering to PDF/PNG; no font install. Font-based printing kept as fallback | 6 |
| X-Charge card processing, gift card issue/balance | p.105–108 | MODERNIZED | `IPaymentGateway` + `IGiftCardProvider` (see Q1) | 5 |
| **Gift card inventory** (item type Gift Card, tax flags off, initialize on sale, balance on receipt) | p.106–107 | PARITY | `ProductType.GiftCard`; issue-on-sale workflow preserved | 5 |
| QuickBooks: push customers/inventory/vendors/invoices/**POS revenue batch**; pull customers/inventory/vendors; QB-XML troubleshooting | p.109–113 | MODERNIZED | `IAccountingConnector` two-way sync, mapping tables, idempotent. **POS revenue posts to a GL bank/income account exactly as legacy** | 6 |
| Trainee mode (password "Trainee", level 0, nothing saved) | p.82 | PARITY | Training mode flag — sandboxed cart, receipts watermarked, nothing posted | 6 |
| Software licensing / 60-session demo | p.2, p.114 | DROPPED | Self-hosted deployment. |

---

## Explicitly out of scope (v1)

- E-commerce storefront / online ordering.
- Multi-tenant SaaS. The schema is single-tenant with multi-location; tenant isolation is a later change.
- Payroll (hours are tracked and exported; no pay runs).
- Full general ledger. We post *to* an accounting system; we do not become one.
- Native mobile apps (the web UI is responsive; a handheld RFID gun is served by the same UI).

## Scope risks

| Risk | Impact | Mitigation |
|---|---|---|
| Pricing/tax parity is subtle (compound tax, inclusive pricing, break points × sale pricing × BOGO × client level × manual override precedence) | Wrong prices = wrong money | [04](04-pricing-and-tax-engine.md) fixes precedence; golden-file test suite built from legacy examples **before** implementation |
| RFID bulk read ≠ scan: phantom reads, tags in the next aisle, unsold stock on shelves near the antenna | Wrong cart contents, angry customers | RSSI thresholds, antenna zoning, debounce windows, explicit "confirm bulk read" step, EPC state machine rejecting `Sold`/`NotInStock` units — see [06](06-rfid-and-hardware-bridge.md) |
| Legacy DBF data quality (no FKs, 30-year-old typing) | Migration stalls | Staging tables + validation report + dry-run mode; migration is Phase 7, not day one |
| Feature volume: ~180 discrete behaviours | Schedule | Phased roadmap ([11](11-delivery-roadmap.md)); a usable POS ships at Phase 3 |
