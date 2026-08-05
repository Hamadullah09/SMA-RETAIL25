# 09 — Integration & Data Migration

## 1. Accounting / ERP synchronization (replaces QB-XML)

The legacy integration (guide App. Q) is QuickBooks Desktop over QB-XML, requiring the company file
open in multi-user mode on the same machine. We replace it with a port + adapters.

```csharp
public interface IAccountingConnector
{
    string Provider { get; }                                  // "quickbooks-online" | "xero" | "csv" | …
    Task<SyncResult> PushCustomersAsync(SyncScope s, CancellationToken ct);
    Task<SyncResult> PushItemsAsync(SyncScope s, CancellationToken ct);
    Task<SyncResult> PushVendorsAsync(SyncScope s, CancellationToken ct);
    Task<SyncResult> PushInvoicesAsync(SyncScope s, CancellationToken ct);   // open AR invoices
    Task<SyncResult> PostPosRevenueAsync(DateOnly business, CancellationToken ct);
    Task<SyncResult> PostBillAsync(PurchaseOrderId po, DateOnly dueOn, CancellationToken ct);
    Task<SyncResult> PullCustomersAsync(CancellationToken ct);
    Task<SyncResult> PullItemsAsync(CancellationToken ct);
    Task<SyncResult> PullVendorsAsync(CancellationToken ct);
}
```

### Legacy behaviours preserved

| Legacy | Guide | Modern |
|---|---|---|
| Push customers / inventory / vendors, dedupe by name / stock code | p.110–111 | `ExternalEntityMap(entityType, localId, remoteId, provider, lastSyncedAt, hash)` — dedupe by mapping, not by name collision |
| Push **open** invoices only; transferred invoices removed from Retail Plus but still payable at POS | p.111 | We **never delete**. Invoice is marked `ExternallyOwned`, still payable; payments sync back |
| **POS Revenue batch** to a Bank account, Income account "Sales"; source rows cleared after transfer | p.111 | `PostPosRevenueAsync` posts a daily journal per location from closed `DrawerSession` snapshots; source rows retained, marked `PostedOn` |
| PO → A/P bill, one bill per supplier, **due date default +30 days** | p.112–113 | `PostBillAsync(poId, dueOn)`; default `+30d` preserved and editable |
| Payment methods and tax items must match by name | p.109–110 | Explicit mapping UI: local `TenderType`/`TaxConfiguration` ↔ remote ids. No silent name matching |
| Subtotal discount needs a "DISC" discount item | p.110 | Mapping entry; validated by a pre-flight check |
| "Last QB Request / Last QB Response" troubleshooting | p.111 | Every sync attempt stores request/response/error in `SyncLog`, viewable in the admin UI |

### Mechanics

- **Direction & cadence**: pushes are event-driven via outbox (batched, default nightly for revenue,
  near-real-time for customers/items if the provider's rate limits allow); pulls are scheduled.
- **Idempotency**: every push carries a deterministic `externalRequestId` derived from
  `(entity, localId, contentHash)`; replays are no-ops.
- **Conflict policy**: configurable per entity — `LocalWins` (default for items/prices),
  `RemoteWins` (default for the chart of accounts), or `Manual` (queues a review task).
- **Pre-flight validation** before the first sync: required accounts exist, tax items map, payment
  methods map, discount item exists. This is where the legacy integration failed most often, and the
  guide's troubleshooting section exists precisely because it failed silently.
- **Failure isolation**: accounting outages never block selling. Sync is entirely downstream of the
  sales ledger.

Adapters shipped: `QuickBooksOnlineConnector` (OAuth2, REST), `GenericRestConnector` (configurable
JSON mapping for in-house ERPs), `CsvExportConnector` (always available; produces journal/AR/AP
files for manual import). Selection is per-deployment config. **Q2 decides which is built first.**

---

## 2. Multi-store & replication (replaces FTP, guide ch. 11)

The legacy design shipped `.DBF` files over FTP for sales logs, stock levels, stock transfers and
stock updates. All four collapse into ordinary features of a shared database:

| Legacy FTP feature | Replacement |
|---|---|
| Send/view sales logs | Single sales ledger; HQ filters by location |
| Send/view stock levels | `GET /stock/levels?locationId=` + live `InventoryHub` |
| Stock transfers (send DBF/FPT, receive) | `StockTransfer` aggregate: `Draft → InTransit → Received`, with variance capture on receipt |
| Stock updates (harmonize items/prices) | Single catalog; per-location price/stock overrides where a store genuinely differs |
| Generic file transfer | Dropped |

If stores must survive WAN outages (**Q4**), the Phase 8 design is: store-local API + a SQL Server
logical replication for reference data, station-scoped number ranges, and outbox-based upstream
sync of sales. The schema already supports it (every ledger row carries `LocationId` and a
globally-unique id), so this is additive, not a rewrite.

---

## 3. Legacy data migration (`Retail25.Migration`)

### Source files (documented in the guide)

| File | Contents | Guide |
|---|---|---|
| `XXXINV.DBF` (+ `.FPT`) | inventory for location `XXX` | p.103 |
| `CLIENT.DBF` / `CLIENTS.DBF` + `.FPT` | clients incl. purchase-history memo | p.97, p.103 |
| `INVOICE.DBF` | accounts receivable | p.103 |
| supplier file | suppliers | p.103 |
| `TOTAL001.DBF` | POS history / exit totals per station | p.15 |
| sales log export | itemized sales log | p.14 |
| `SETUP.DBF`, `PLUS20.INI` | global + per-workstation configuration | p.88 |
| CSV/`.DTA` exports | documented field orders for inventory, clients, suppliers | p.28, p.48, p.61 |

Note the guide's own caveat (p.103): version 2.5 itself never converted sales logs, POs, exit totals
or back orders from older versions. We do better — we import whatever exists, and report exactly
what could not be mapped.

### Pipeline

```
 analyze  → read DBF headers + FPT memos, profile columns, row counts, null/format anomalies
            → ANALYSIS REPORT (no writes)
 map      → column mapping file (YAML), pre-filled from the documented v2.5 layouts, human-editable
 stage    → bulk COPY into legacy_staging.* tables, 1:1 with source, everything text
 validate → referential checks, duplicate stock codes/customer numbers, orphan invoices,
            unparseable dates/decimals, encoding issues
            → VALIDATION REPORT: blocking errors vs warnings, row-addressable
 dry-run  → full transform into a scratch schema; produce reconciliation totals
            (item count, inventory value, AR balance, YTD sales) vs. legacy reports
 import   → transactional load into the live schema, ledger-first:
              locations → departments/categories → suppliers → products (+ variants, kits,
              serials, supplier links) → opening stock as StockLedgerEntry(MovementType=Adjustment,
              Reason='Legacy opening balance') → customers → open invoices as AR charges →
              historical sales (optional, bounded by date) → configuration
 verify   → re-run reconciliation; publish a signed migration report
```

Implementation notes:

- DBF reading is hand-rolled over the VFP/dBase III+ layout (a small, well-specified format) rather
  than an abandoned NuGet package: header, field descriptors, records, deletion flags, plus `.FPT`
  memo blocks. Encoding defaults to CP437/CP1252 with an override.
- **Idempotent and resumable**: every staged row carries a source hash; re-running skips completed
  batches. A failed import rolls back to the pre-import snapshot (`pg_dump` taken automatically).
- **Dry-run is mandatory** before a live import; the CLI refuses `import` without a passing
  `dry-run` artifact for the same source hash.
- Opening balances arrive as ledger entries, never as raw `OnHand` writes — so the ledger is
  authoritative from row one.
- Historical sales import is bounded (default: current + previous fiscal year) with an option for
  everything.

### Also supported

- CSV import presets matching the legacy documented field orders exactly (inventory p.28: *item
  name, stock code, department, category, size, pack quantity, cost, price, onhand, supplier,
  reorder number*; clients p.48 14 fields; suppliers p.61 15 fields), including the legacy
  "double-comma for an empty field" tolerance.
- Cash-register `.ASC` import (stock code, item name, qty sold, total) for third-party registers
  (p.17).
- Stock-count device CSV (stock code, shelf count) for batch onhand adjustment (p.22).

---

## 4. Other integration points

| Integration | Port | Notes |
|---|---|---|
| Payments / gift cards | `IPaymentGateway`, `IGiftCardProvider` | **Q1**. Simulator adapter ships first so the whole payment UX is testable without hardware |
| Supplier ordering ("Web Submit", p.71) | `ISupplierPortal` | EDI 850, vendor REST, or emailed PDF. Phase 8 |
| Email (invoices, statements, marketing selects) | `IEmailSender` | SMTP/Graph; replaces "send by Outlook Express" (p.13) |
| Object storage (product/client photos, receipt archives) | `IFileStorage` | Local disk or S3-compatible |
| Label & document printing | `ILabelRenderer`, `IDocumentRenderer` | QuestPDF; Avery 5160/8160/8163/S-644N presets, COM10 envelopes, Code 39 rendered natively |
