# 05 — Application Layer, API & Realtime

## MediatR pipeline

Order matters; behaviours run outermost → innermost:

```
Request
 └─ RequestLoggingBehavior        correlation id, actor, station, duration
    └─ PerformanceBehavior        warn > 200 ms
       └─ AuthorizationBehavior   [RequiresPermission] on the request type
          └─ ValidationBehavior   FluentValidation, → 400 ProblemDetails with field errors
             └─ IdempotencyBehavior  commands only; replays stored response for a repeated key
                └─ TransactionBehavior  commands only; one DB transaction, outbox flush on commit
                   └─ Handler
```

Queries skip idempotency/transaction and use `AsNoTracking` + `Select` projections.

## Command / query inventory (representative, not exhaustive)

### Carts & POS

| Request | Notes |
|---|---|
| `CreateCartCommand` | per station; returns cart + station policy snapshot |
| `AddCartLineByIdentifierCommand` | the universal entry point: EPC \| stock code \| UPC \| Type-2 weight \| variant \| serial |
| `AddRfidBatchCommand` | *N* EPCs from one bulk read; returns accepted / rejected(reason) per tag |
| `UpdateCartLineCommand` | qty, price, discount, price level, tax overrides — mirrors the Item Detail window |
| `RemoveCartLineCommand`, `ClearCartCommand` | |
| `ApplyCartAdjustmentCommand` | subtotal discount, coupon, bottle, gift certificate, loyalty reward |
| `AddUnknownItemCommand` | legacy F11-F2 "U/I"; optionally creates the product |
| `SetCartTaxOverrideCommand` | stamps `AppliesFromSequence` (non-retroactive, doc 04 §3) |
| `AssignCustomerToCartCommand` / `ClearCustomerCommand` | |
| `SuspendCartCommand` / `RecallCartCommand` / `ListSuspendedCartsQuery` | |
| `QuoteCartQuery` | runs the pricing engine, returns totals **without** persisting — drives the live totals panel |
| `CompleteSaleCommand` | tenders[], print options, idempotency key → `SalesTransaction` (+ `Invoice` if AR) |
| `VoidSaleCommand` | supervisor step-up; creates reversal |
| `ReprintTransactionCommand`, `PrintPackingSlipCommand` | |

### Drawer

`OpenDrawerSessionCommand(float)` · `PayInCommand` · `PayOutCommand` · `PopDrawerCommand` ·
`GetDrawerTotalsQuery` · `CloseDrawerSessionCommand(countedCash)` → snapshot + variance
(legacy F10 menu, p.10–11).

### Catalog / Inventory

`CreateProductCommand` · `CloneProductCommand` · `UpdateProduct*Command` (detail/pricing/ordering/
notes/matrix/kit/special) · `SoftDeleteProductCommand` / `RestoreProductCommand` ·
`BulkPriceAdjustmentCommand` · `BulkSetTaxFlagsCommand` · `AdjustStockCommand` ·
`ReceiveStockCommand` · `StartStockCountCommand` / `ImportCountFileCommand` / `PostCountVarianceCommand` ·
`CreateStockTransferCommand` / `ReceiveStockTransferCommand` · `BreakCaseCommand` ·
`RunFiscalYearCloseCommand` · queries for browse grids, pick lists, understock/overstock, stock value,
top sellers, analysis.

### Customers / AR / Purchasing / Staff

`CreateCustomerCommand` … `GetPurchaseHistoryQuery` · `RecordInvoicePaymentCommand` ·
`DistributePaymentCommand` · `AccrueLateChargesCommand` (job) · `VoidInvoiceCommand` ·
`RefundPaymentCommand` · `PrintStatementsQuery` · `GeneratePurchaseOrderCommand(scope, supplierRule,
quantityStrategy)` · `PostOrderCommand` · `PostShipmentCommand(freightTotal, flagForLabels)` ·
`ExportPurchaseOrderQuery` · `ClockInCommand`/`ClockOutCommand` · `GetCommissionReportQuery`.

---

## HTTP API surface

Minimal APIs, grouped, versioned `/api/v1`. All responses use `ProblemDetails` on error.

```
POST   /api/v1/carts                                  create
GET    /api/v1/carts/{id}
POST   /api/v1/carts/{id}/lines                       { identifier | productId, qty, … }
POST   /api/v1/carts/{id}/lines/rfid-batch            { epcs[] }
PATCH  /api/v1/carts/{id}/lines/{lineId}
DELETE /api/v1/carts/{id}/lines/{lineId}
POST   /api/v1/carts/{id}/adjustments
PUT    /api/v1/carts/{id}/customer
PUT    /api/v1/carts/{id}/tax-override
POST   /api/v1/carts/{id}/suspend | /recall
GET    /api/v1/carts/{id}/quote                       live totals (pricing engine, no write)
POST   /api/v1/carts/{id}/complete                    Idempotency-Key required
POST   /api/v1/sales/{id}/void | /reprint | /packing-slip
GET    /api/v1/sales                                  itemized sales log (filter, page, export)

POST   /api/v1/drawer-sessions | /{id}/pay-in | /pay-out | /pop | /close
GET    /api/v1/drawer-sessions/current

GET    /api/v1/products?search=&department=&…         browse grid (cursor paged)
GET    /api/v1/products/lookup?identifier=            resolver: EPC/code/UPC/weight barcode
POST   /api/v1/products … PATCH /{id} … POST /{id}/restore
POST   /api/v1/products/bulk/price-adjustment | /tax-flags
POST   /api/v1/stock/adjust | /receive | /transfers | /counts
GET    /api/v1/stock/levels?locationId=

GET/POST/PATCH /api/v1/customers … /{id}/history | /invoices | /quotes | /layaways
GET/POST /api/v1/invoices … /{id}/payments | /void | /refund | /statement
POST   /api/v1/payments/distribute
GET/POST /api/v1/suppliers, /api/v1/purchase-orders … /{id}/post | /receipts | /print | /export
GET/POST /api/v1/staff, /api/v1/time-clock
GET    /api/v1/reports/{name}                         params vary; ?format=json|pdf|xlsx|csv
GET/PUT /api/v1/settings/{section}                    business, taxes, pos, printers, hardware, users, options
POST   /api/v1/migration/analyze | /dry-run | /import  (admin only)
POST   /api/v1/sync/accounting/{direction}/{entity}
GET    /api/v1/labels/preview | POST /api/v1/labels/print
GET    /health/live | /health/ready | /metrics
```

**Conventions** — cursor pagination (`?cursor=&limit=`), `If-Match`/`ETag` on entity updates,
`Idempotency-Key` on every POST that moves money or stock, RFC 7807 errors with a machine-readable
`code` (e.g. `stock.insufficient`, `epc.already_sold`, `tax.override_not_allowed`).

---

## SignalR hubs

Three hubs, group-scoped so a station never receives another station's cart.

### `PosHub` — `/hubs/pos`
Groups: `station:{stationId}`, `location:{locationId}`, `cart:{cartId}`

**Server → client**
| Event | Payload | Purpose |
|---|---|---|
| `CartUpdated` | `CartDto` (full) + `revision` | authoritative state after any mutation |
| `CartLinesAdded` | `CartLineDto[]` | fast path for bulk RFID (avoids resending the whole cart) |
| `CartLineRejected` | `{ epc, reason }` | tag already sold / unknown / wrong location |
| `TotalsChanged` | `TotalsDto` | live totals panel |
| `TagStreamStatus` | `{ readerOnline, antennaZones, readRate }` | the "Live RFID Feed" health strip |
| `CartSuspended` / `CartRecalled` | `{ cartId, staff, label }` | visible to all stations at the location |
| `DrawerStateChanged` | `DrawerTotalsDto` | |
| `PeripheralStatus` | `{ printer, drawer, scale, poleDisplay }` | status bar |
| `SupervisorApprovalRequested` | `{ requestId, action, context }` | manager approves from any station |
| `PosMessage` | `{ productId, message }` | legacy per-item POS prompt (p.43) |

**Client → server**
`JoinStation(stationId)` · `LeaveStation` · `Heartbeat` · `RequestCartResync(cartId, knownRevision)`

### `InventoryHub` — `/hubs/inventory`
Groups: `grid:{entity}:{filterHash}`, `location:{id}`
Events: `StockLevelChanged`, `ProductChanged`, `ProductDeleted`, `PriceChanged`,
`PurchaseOrderChanged`, `InvoiceChanged`, `RowsInvalidated`.
This is the direct answer to the legacy complaint (guide p.100–101) that browse windows go stale on
a network. Grids subscribe to their filter and patch rows in place; TanStack Query caches are
updated by key, not refetched.

### `TerminalHub` — `/hubs/terminal` (agents only, certificate/secret auth)
Server → agent: `PrintReceipt(payload)` · `OpenDrawer()` · `DisplayPole(line1,line2)` ·
`RequestWeight()` · `ZeroScale()` · `SetReaderMode(mode)` · `UpdateProfile(profileJson)`
Agent → server: `PublishTags(TagRead[])` · `ReportWeight(value,unit)` · `ReportStatus(...)` ·
`ReportPrintResult(...)`

**Reliability**: every hub message carries a monotonically increasing `revision` per cart. The
client compares; on a gap it calls `RequestCartResync`. Reconnect uses exponential backoff with
jitter; on reconnect the client always resyncs rather than trusting local state. Redis backplane
(`AddStackExchangeRedis`) is configured from day one so horizontal scale is a config change.

---

## Domain events & the outbox

```
CompleteSaleCommand
  └─ tx: SalesTransaction + SaleLines + StockLedgerEntries + StockLevel updates
         + DrawerLedgerEntry + LoyaltyLedgerEntry [+ Invoice + ARLedgerEntry]
         + OutboxMessage[] ← written in the SAME transaction
  commit
     └─ OutboxProcessor (BackgroundService, polls + LISTEN/NOTIFY)
          ├─ SaleCompleted        → IPosNotifier.CartUpdated / InventoryHub.StockLevelChanged
          ├─ SaleCompleted        → AccountingSyncJob (queued, batched daily)
          ├─ StockFellBelowReorder→ reorder suggestion feed
          ├─ CustomerOrderFillable→ notify (legacy "Fill This Order")
          └─ ReceiptRequested     → TerminalHub.PrintReceipt
```

Handlers are **idempotent** and keyed by `OutboxMessage.Id`; delivery is at-least-once. Failed
messages retry with exponential backoff and land in a dead-letter view with the exception, visible
in the admin UI.

Scheduled jobs (Hangfire): nightly `AccrueLateCharges`, daily `PostPosRevenueToAccounting`,
`SyncAccountingEntities`, `ExpirePriceQuotes`, `PurgeExpiredCarts`, `PartitionMaintenance`,
on-demand `FiscalYearClose`.

---

## Error taxonomy (selected)

| `code` | HTTP | Meaning |
|---|---|---|
| `stock.insufficient` | 409 | serialized/tracked item not available at this location |
| `epc.unknown` | 404 | EPC not mapped to a product |
| `epc.already_sold` | 409 | tag state is `Sold`/`Void` — likely a shelf read or a returned tag |
| `epc.claimed_by_other_station` | 409 | debounce lock held elsewhere |
| `cart.revision_conflict` | 409 | client acted on stale state; resync |
| `tax.override_not_allowed` | 403 | `AllowTaxOverride` is off |
| `discount.not_permitted` | 403 | `StaffMayDiscount` off and actor < level 3 |
| `credit.limit_exceeded` | 409 | AR tender beyond `CreditLimit` (0 = unlimited) |
| `tender.mismatch` | 400 | Σ tenders ≠ grand total |
| `sale.requires_supervisor` | 428 | step-up approval needed (void, price override) |
