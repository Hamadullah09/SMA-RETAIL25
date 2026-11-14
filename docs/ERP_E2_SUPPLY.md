# ERP phase E2: supply chain

Phase E2 of the [ERP plan](ERP_PLAN.md) (sections 3.2 and 4): batches and expiry, bins, requisitions with approval, the
three-way match into E1's payables, landed costs, reorder suggestions from sales, and supplier performance. Built on the
branch `erp-e2-supply`, on top of the general ledger foundation (ERP plan, section 6).

## What is there

| Area | What it does | Where |
|---|---|---|
| Batches and expiry | An item can be *kept in batches* (`Item.TracksBatches`, with `ShelfLifeDays`). Receiving asks for each batch's number, pieces, expiry (offered from the shelf life) and made-on date. A batch (`ItemBatch`) has no stock column: its stock is the sum of the stock movements that name it (`StockMovement.BatchId`). Sales take the batch that expires first (`Fefo`), splitting a line over batches; a void puts back exactly what the sale took. A store can stop expired goods from being sold (`SupplySettings.BlockExpiredSales`, on by default). Counts, transfers and adjustments work per batch. Lists of stock by batch, batches expiring within so many days, and expired stock with its value | Model and services (`BatchService`); till: receiving, POS, item editor; web: *Batches and expiry*, receiving on *Purchase orders* |
| Bins | `Bin` per location (code, zone, active); an item names its default bin in `Item.BinLocation`. Put-away lists after receiving, pick lists for customer orders and transfers, count sheets of a zone or bin, all sorted by zone and bin | `BinService`; web: *Bins*, put-away after receiving |
| Requisitions | `PurchaseRequisition` and its lines: Draft → Submitted → Approved or Rejected → Ordered. Approved by someone with `purchasing.approve`, within their role's limit (`PurchaseApprovalLimit`), optionally not the person who asked (`SupplySettings.ApproverMustDiffer`). Ordering makes draft purchase orders, one per supplier | `RequisitionService`; till: *Purchase Orders › Requisitions*; web: *Requisitions* |
| Three-way match | Each supplier bill line that names a purchase order line is compared with what was ordered (pieces, agreed cost `PurchaseOrderLine.OrderedCost`) and received (pieces, the cost stock was valued at). Within the store's tolerances it is *Matched*; outside them *Held* until someone with `purchasing.approve` accepts or rejects it. The price difference of matched and accepted lines goes to the stock still on hand and to cost of goods (`PriceVariance`, ledger source `match:{purchaseOrderLineId}`: Dr Inventory and Cost of goods, Cr Goods received not billed). Reports: received but not billed, billed but not received | `PurchaseMatchService` over `IBillLineSource` (see *The link to E1*); web: *Matching* |
| Landed costs | `LandedCostVoucher`: freight, duty, insurance and other charges over one or more received orders, spread by value, quantity or weight; posting raises the average cost of what is on hand and puts the rest in cost of goods (source `landed:{voucherId}`: Dr Inventory and Cost of goods, Cr Landed costs to allocate) | `LandedCostService`; web: *Landed costs* |
| Reorder suggestions | Average daily sales over the store's window (30 days by default), the supplier's lead time (the item's supplier entry, else the supplier's, else the store's default), safety days and the days an order should cover, against stock on hand, on order and promised to customers; whole cases, never below the item's reorder point, at least the supplier's minimum. Ticked lines become purchase orders (one per supplier) or a requisition | `ReorderService`, `Core.Rules.ReorderRules`; web: *Reorder suggestions* |
| Supplier performance | Per supplier over a period (90 days by default on the web): orders received and how many on time (by the expected date, else posted + lead time), fill rate, price rises and falls between receipts, returns (RMAs) and average lead time | `SupplierPerformanceService`; web: *Supplier performance* |
| Settings | Per store (`SupplySettings`): expired sales, expiry warning days, match tolerances (quantity and price, percent and amount), sales window, safety, cover and default lead time days, a second approver. Per role: the approval limit | `SupplySettingsService`; web: settings on *Batches and expiry*, *Matching* and *Reorder suggestions*, limits on *Requisitions* |

Permissions (in `Core.Rules.Permissions`): `purchasing.approve` (approve requisitions, decide held bill lines, make
and post landed cost vouchers), `inventory.batches` and `inventory.bins`. Manager and the administrators get all
three; Stock clerk gets `inventory.batches`. Purchase orders, requisitions and matching are read with the existing
`purchasing.manage`; supplier performance with `reports.view`; changing the store's supply settings needs
`setup.store`, approval limits `setup.users` (and the company-wide rule, as for roles).

The audit log gets a line for every requisition step (submitted, approved, rejected, ordered), every decision on a held
bill line, every landed cost voucher posted and every batch adjusted; batches, bins, the store's supply settings and the
approval limits are audited tables (added, changed, deleted with the values before and after).

## Where each table is kept (sync)

| Table | Kept | Why |
|---|---|---|
| `ItemBatch` | Both ways with its store (merged on item and batch number) | Batches are made where goods arrive, a till or the web; the till needs them to sell first expiry first |
| `Bin` | Both ways with its store (merged on location and code) | Bins are set up on the web or at a till; put-away and count sheets are printed in both |
| `StockMovement.BatchId`, `Item.TracksBatches`, `Item.ShelfLifeDays`, `PurchaseOrder.SupplierId`, `ExpectedOn`, `PurchaseOrderLine.OrderedCost`, `ItemSupplier.LeadTimeDays`, `MinimumOrder` | With their rows, as before | New columns of tables that already travel |
| `PurchaseRequisition`, `PurchaseRequisitionLine` | Both ways with their store | Requisitions are made and approved at the till's Purchase Orders screen or on the web |
| `SupplySettings` | Down to the store's tills (merged on store) | The POS reads whether expired goods may be sold |
| `PurchaseApprovalLimit` | Down to every till (merged on role) | A till checks the approver's limit, like roles and permissions |
| `BillMatch`, `PriceVariance` | Server only (`ILocalOnly`) | Supplier bills are on the server (E1) |
| `LandedCostVoucher`, `LandedCostCharge`, `LandedCostReceipt`, `LandedCostAllocation` | Server only (`ILocalOnly`) | Kept with the ledger |

All of it is in `SyncPolicy.Create` (the block marked *Supply chain (phase E2)*).

## On the till (desktop)

- **Purchase Orders** has two tabs, *Orders* and *Requisitions* (Ctrl+Tab between them).
  - *Orders*: Add item (Ins), Remove line (Ctrl+Del), Post order (F6), Receive all (F7), Post shipment (F8). For items
    kept in batches, Post shipment opens *Batches received*: one row per line with all the pieces and the expiry from the
    shelf life; *Another batch* (Ins) splits a line; the batches must add up to the pieces received, and a batch cannot
    expire before it was made.
  - *Requisitions*: New requisition (Ctrl+N), Add item (Ins), Remove line (Ctrl+Del), Save changes (Ctrl+S), Send for
    approval (F6), Approve (F7), Reject (F8), Make purchase orders (F9), Delete requisition (Del). Approving or rejecting
    without `purchasing.approve` asks a supervisor to sign in for it (as other approvals at the till), and the role's
    limit applies to whoever approves. Ctrl+K › *Purchase requisitions* opens this tab.
- **Point of Sale**: an item kept in batches says which batch it leaves from (first expiry first). When some of its stock
  has expired the message bar shows a red warning: the sale takes the expired batch (when the store allows it), or the
  expired goods are still on the shelf. When the store stops expired sales and only expired stock is left, the scan is
  refused with the reason.
- **Item editor › Stock**: *Kept in batches, with expiry dates* and *Shelf life (days)*.
- **Help** (Suppliers and purchase orders) explains batches and requisitions.
- **`--smoke`**: a requisition is made, sent, approved and ordered on the Requisitions tab; the order is received in two
  batches, one already expired; the POS warns about the expired batch, sells from the good one, and the expired batch
  keeps its stock.

Batch lists, bins, matching, landed costs, reorder suggestions and supplier performance are web pages only: they are back
office work, not shop-floor work.

## On the server and the web

The endpoints are listed in [API.md](API.md), section *Supply chain (phase E2)*; the server code is
`src/RetailPlus.Server/Endpoints.Supply.cs`. Every list follows the store scope (a `storeId` the user cannot see is
403, a record of another store 404), every change sends the `version` it read (409 when someone else changed it), and
every change is written as the web user at station 0 of the record's store, like the other endpoints.

The web pages sit under a *Supply chain* heading in the navigation and the Ctrl+K palette: Purchase orders, Requisitions,
Batches and expiry, Bins, Matching, Landed costs, Reorder suggestions and Supplier performance (`web/src/app/(app)/…`,
shared parts in `web/src/components/supply.tsx` and `web/src/lib/supply.ts`).

Tests: `tests/RetailPlus.Core.Tests/SupplyChainRulesTests.cs` (first expiry first, tolerances, spreading, reorder
rules), `tests/RetailPlus.Data.Tests/SupplyChainTests.cs` (every service on SQLite and SQL Server),
`tests/RetailPlus.Server.Tests/SupplyChainApiTests.cs` (the endpoints with permissions, store scope, versions, and a
till's batches going up and the web's coming down), `tests/RetailPlus.Server.Tests/SupplyContractTests.cs` (every
answer and request of the server has the fields of its type in `web/src/lib/types.ts`, and every status the web's
union), `web/src/lib/supply.test.ts` and `api.test.ts` (Vitest),
`web/e2e/supply.spec.ts` (Playwright, answered from `web/e2e/fixtures.ts` and `mock-api.ts`).

## Fixes after the demo: batches from the web, the day received, locations

Testing on a demo database found three gaps, closed on the branch `fix-supply`:

- **Batches are set up on the web, and receiving no longer drops batch data.** The item API (`ItemDetail.tracksBatches`,
  `shelfLifeDays`) and the web item editor (*Stock* tab: *Kept in batches, with expiry dates* and *Shelf life (days)*)
  carry both fields; left out of a request they stay as they are, and a shelf life is 0 to 36500 days
  (`InventoryService.SaveAsync`, so the till's editor checks it too). Receiving batches for an item that is not kept in
  batches is refused, as a 400 on the web: *"2001 Milk pack 1 L is not kept in batches; turn that on for the item
  first."* (`PurchasingService.PostShipmentAsync`). The till never offers batch boxes for such an item, and neither does
  the web's receive dialog; the refusal guards the API, which used to answer 200 and throw the batches away.
- **Orders are posted and received on the day given.** `POST /api/purchase-orders/{id}/post` takes `orderedOn` and
  `.../receive` takes `receivedOn` (both `yyyy-MM-dd`, today where the store is when left out). A later day than today
  is refused, a receipt dated before the order was posted is refused, and so is a day in a locked or closed ledger
  period (`LedgerService.IsOpenAsync`, E1's rule for postings; open the period under *Finance › Years and periods*
  first). The receipt's stock movements, its batches' received date (and the expiry offered from the shelf life), the
  order's `PostedAt` and `ReceivedAt` (the day given, at the time it was recorded), the items' last order and receipt
  dates (not moved back by an older receipt) and so the three-way match's *received* date and the business-day ledger
  entry of that day all take it. The web's receive dialog has *Received on*, and *Post order* asks for *Ordered on*,
  both today unless changed; today is sent as `null`, so the store's own today applies.
- **Locations are managed on the web, and one is the store's hold location.** `POST /api/stores/{id}/locations` and
  `PUT /api/locations/{id}` (`stores.manage`, like stations) add a location (code of 1 to 3 characters, once per store)
  and rename it; the code stays, because a till finds the location it sells from by its code. `isHold` marks the
  store's hold location, kept as the store setting `QUALITY_HOLD` (`LocationService`; no model change). An output
  inspection that holds failed goods without naming a location moves them there, the web's inspection dialog offers it
  first, and planning (MRP) does not count its stock. Lists and new items that name no location go to the store's first
  location that is not the hold location. Locations and store settings are master data: both reach the store's tills at
  their next download (`SyncPolicy`: locations both ways with their store and down to every till; settings with their
  store). The web shows them on *Stores*, a *Locations* section per store.

## The link to E1 (supplier bills)

E1 owns supplier bills (`SupplierBill` and `SupplierBillLine` in `Core/Model/Finance.cs`); E2 does not copy those
tables. The three-way match reads bill lines through one interface, `IBillLineSource` in
`src/RetailPlus.Data/Services/PurchaseMatchService.cs`, and since the two phases were merged it reads E1's bills:

- **`SupplierBillLineSource`** (`src/RetailPlus.Data/Services/SupplierBillLineSource.cs`) gives the lines that name a
  purchase order line, of bills that are posted (`Posted`, `PartPaid`, `Paid`; not `Draft`, not `Void`).
  `BillMatch.BillId` and `BillMatch.BillLineId` hold the bill's and the line's ids. The bill number is the supplier's own
  (`SupplierReference`), else the bill's `Number`. A debit note's pieces count against the order (goods sent back); a
  wrong price is put right by voiding the bill and billing again. The cost is `UnitPrice × ExchangeRate`, before tax,
  in the store's currency, as receiving values stock.
- The server registers it (`src/RetailPlus.Server/Program.cs`); the endpoints take `IBillLineSource` from the services.
- **`PayablesService` matches as bills change:** posting a bill, and voiding one, run `MatchBillAsync` for it. After a
  void the bill's rows leave the match, its price difference comes back out of the items' cost and the ledger entry is
  reversed (a later bill posts under `match:{line}/2`). *Run the match again* on the Matching page checks every billed
  order.
- **A held bill is not paid:** a payment, and a payment run (which checks every bill before paying any), refuse a bill
  with a line held or rejected by the match, saying so; once someone with `purchasing.approve` accepts the line, it is
  paid as usual.
- **Ledger:** posting a bill debits *Goods received, not billed* (role `PurchasesClearing`) for its order lines at the
  billed amount, and receiving credited it at the received value; the match posts the difference between the two
  (Dr Inventory and Cost of goods, Cr Goods received not billed, or the other way round), so the clearing account comes
  back to zero for a fully billed order. A carrier's or customs bill for a landed cost voucher is entered in E1 with its
  line on *Landed costs to allocate* (role `LandedCostsClearing`, code 2030), which posting the voucher clears.
- **Tests:** `SupplyChainApiTests.Bills_are_matched_…` enters and posts a real supplier bill through the finance API: it
  is held, its payment is refused, it is accepted, and then paid. The data test `Bills_within_tolerance_match_…` keeps
  `InMemoryBillLineSource`, which stays for tests of the match on its own.

## What is left

- The till's own Post order and Post shipment are still dated today: the date is on the web and in the API
  (`PurchasingService` takes it), not yet in the desktop's Purchase Orders screen.
- The price difference of a matched or accepted bill line is posted on the day of the match, not on the day received.
- A location cannot be deleted (items, movements and documents point at it); a location's code cannot change.
- Stock on hold still shows in the store's stock lists with its location; only planning leaves it out.
- Bins and batch lists on the till: the till sells and receives in batches, but the batch lists, bin set-up and the
  put-away, pick and count lists are on the web only. A till prints the put-away list from the web for now.
- Choosing a batch by hand at the POS: the data layer takes `SaleDraftLine.BatchId`, but the POS has no key for it yet
  (it always sells first expiry first).
- Bills in another currency than the store's: `SupplierBillLineSource` converts with the bill's own rate; the match itself
  compares costs in the store's currency.
