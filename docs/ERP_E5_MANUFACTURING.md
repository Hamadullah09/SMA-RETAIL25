# ERP phase E5: manufacturing and production

Built on branch `erp-e5-manufacturing`, from `integration-all-phases` (the general ledger foundation of
[ERP_PLAN.md](ERP_PLAN.md), section 6). The decision it follows: manufacturing of any kind, assembly and process work,
made to order and to stock, bills of materials with yields, scrap and by-products.

## 1. What is there

| Area | What it does |
|---|---|
| Work centres | A machine, line or team at one location of a store: labour, machine and overhead cost per hour, hours a day and days a week |
| Bills of materials | What a product (a stock code, the same at every location) is made of, for a yield (a batch of 50 loaves, one chair). Components with a quantity, a scrap allowance (5 % lost means 5 % more is issued), issued by hand or taken at the end (backflush), and the operation they are used at. By-products take a share of the cost. Versions with dates in force and an active flag; the active version in force on the day is used, the highest first. Copy as the next version, compare two versions, explode over every level, roll the cost up. Loops (A made of B made of A) are refused |
| Routings | The operations of a bill: sequence, work centre, setup minutes once per order, run minutes per unit, instructions for the floor |
| Production orders | Planned, released, in progress, completed, closed, or cancelled. Releasing copies the bill and the routing onto the order (so the bill can change afterwards), works out its standard cost, sets aside the material free in stock, and sends what is short to planning as a suggestion (to make, for a component with a bill). Made to order (a customer order) or to stock. Numbered per station; the web uses the server's own range |
| Shop floor | Start and finish operations; issue material by stock code or barcode (a negative quantity gives it back); issue the rest of the list; throw away spoiled material; report minutes, good and scrapped quantities under the operator's name; receive output; complete the order (backflush, by-products) |
| Planning (MRP) | Per store: demand from open customer orders, items below their reorder point, and open production; netted against stock at every location, open purchase orders and open production; bills exploded level by level; lead times from the supplier (or a default) and from the routing and the work centres' hours. Suggestions to make or buy are firmed into planned production orders or a draft purchase order (one per location), or dismissed. A new run replaces every open suggestion nobody firmed, the shortages of released orders included |
| Capacity | The load of each work centre week by week (open orders' remaining time spread over their planned days, optionally with what planning suggests to make) against its hours |
| Quality | A quality plan per product: pass/fail, a measurement between a minimum and a maximum, or a note. Inspections of an order's output (failed goods to a hold location, scrapped, or left) and of goods received on a purchase order (recorded only; receiving is unchanged) |
| Reports | Orders by status, cost and variances, work in progress value, material usage against standard, output per work centre, scrap, capacity load by week; each as a table and as CSV. Cost reports need `production.costing` |

Every quantity issued, scrapped or received is a stock movement of the order (`ProductionIssue`, `ProductionScrap`,
`ProductionReceipt`, with `StockMovement.ProductionOrderId`) and every report of work is a `ProductionReport`; nothing is
kept as a count on the order. So tills working offline on the same order never overwrite each other.

### Where it is

| Layer | Files |
|---|---|
| Model | `src/RetailPlus.Core/Model/Manufacturing.cs`; the three stock movement kinds and `ProductionOrderId` in `Sales.cs` |
| Data | `src/RetailPlus.Data/Configuration/ManufacturingConfiguration.cs`; services `ManufacturingService` (bills, routings, work centres, explosion, roll-up), `ProductionService` (orders and the floor), `ProductionCostingService` (costs, variances, ledger), `MrpService` (planning, capacity), `QualityService` |
| Reports | `src/RetailPlus.Reports/ProductionReports.cs` |
| Server | `src/RetailPlus.Server/Endpoints.Production.cs`; posting after a till's package in `ServerSync.cs` |
| Web | `web/src/app/(app)/production`, `production/view`, `boms`, `boms/edit`, `routings`, `work-centres`, `planning`, `capacity`, `quality`, `production-reports`; `web/src/lib/production*.ts`; `web/src/components/production.tsx`, `routing-table.tsx` |
| Desktop | `src/RetailPlus.App/Views/ProductionView.cs`, the Production help topic, the smoke check |

### Permissions and roles

| Key | Allows |
|---|---|
| `production.view` | See bills, routings, work centres, orders, planning, capacity, quality and the quantity reports |
| `production.plan` | Change bills (company-wide: only users with every store, or while there is one store), work centres (their store's), orders, planning and quality |
| `production.floor` | Work on orders: operations, material, output (the desktop Production screen, and the same actions on the web) |
| `production.costing` | See costs and variances, close orders, post costs to the ledger, the cost reports |

Standard roles: **Production planner** (web: view, plan, costing, and items) and **Production operator** (the till's
Production screen: view and floor, and the time clock). Managers can see production. Users without
`production.costing` see quantities only: the server sends no costs, unit costs or postings to them.

The audit log keeps releases, completions, closes and cancellations of orders, planning runs and firming, quality
inspections, and every change to work centres, bills, quality plans and supplier lead times.

## 2. Costing

**Standard cost.** At release the bill is rolled up for the order's quantity at its material location: components at
their average cost (last cost when there is none), a component with a bill of its own at its own rolled-up cost, the
routing's minutes at the work centres' labour, machine and overhead rates, and the bill's overhead percentage of the
material. By-products take their cost share; the product the rest. The order keeps the standard cost of one unit, split
into material, labour, machine and overhead.

**Actual cost.** What the order is charged:
- material issued and scrapped, at the average cost of the moment (a component made by another order carries the cost
  that order received it at, so a two-level product costs what its levels really cost);
- labour, machine and overhead for each report of work, at the operation's rates copied at release;
- the bill's overhead percentage of the material;
- output scrapped back into the order by an inspection.

**Output.** Output received before completion is valued at the standard cost of the product's share, never more than the
work in progress. Completing the order first takes the backflushed components, then by-products take their cost share of
everything charged and the last receipt of the product takes what is left, so the order's output carries all its cost.
What is charged afterwards (a late issue, scrapped output) stays in work in progress until the order is closed.

**Variances**, for the good quantity made: material usage (more or less used than the standard, at the standard price)
and price (what it cost above or below the standard price) per component, labour, machine and overhead.

## 3. Postings to the general ledger

Production posts through `LedgerService.PostAsync`, as `JournalKind.Production` entries of the order's store:

| Step | Debit | Credit | Amount |
|---|---|---|---|
| issue | Work in progress (1220) | Inventory (1200) | Material issued and scrapped, and output scrapped back into the order |
| conversion | Work in progress | Manufacturing overhead (5050, absorbed) | Labour, machine and overhead reported, and the bill's overhead percentage of the material |
| receipt | Inventory | Work in progress | Output and by-products received |
| close | Production variances (5040) | Work in progress | What is left in work in progress when the order is closed |

- Finished goods are kept in the one Inventory account of the standard chart, as all stock is.
- Each step is posted **per month**, under the source `prod:{order}:{step}:{yyyy-MM}`, dated on the last day of that
  month something was done; the close under `prod:{order}:close`. So each month's books hold that month's production,
  and work in progress at a month end is right.
- Posting again replaces the entry while its month is open, and posts only the difference, in the first open month, once
  it is locked (the ledger's own rule). Work done in a month that is locked already (a till that sent late) is dated in
  the first open month.
- A month that comes back to nothing after it was posted (all the material given back) is posted again as an entry whose
  lines cancel out, because the ledger has no entries of nothing.
- Every entry has one debit and one credit of the same amount, both on the order's store, so it balances. After the
  close the order's work in progress in the ledger is nothing.
- The server posts an order after every change it makes to it (web actions and inspections), and every open or recently
  closed order of a store after every package a till sends; `POST /api/production/post-costs` posts a store's orders on
  demand. Tills never post: the ledger lives on the server. What cannot be posted (a missing account role, no open
  period) is written to the server log and does not stop the work.
- The daily journal (Setup › Accounting, `PostDailyAsync`) leaves the three production movement kinds out, so production
  is never posted twice.

The checks: `tests/RetailPlus.Data.Tests/ProductionCostingTests.cs`, on SQLite and SQL Server. A loaf is made of dough
(made by its own order from flour and salt) and bags; both bills have a yield above one and a scrap allowance, the floor
spills flour and drops dough, and an inspection scraps two loaves. Every entry is checked to balance and to use the
step's accounts; the amounts are checked by hand; work in progress comes to nothing for both orders; the change in the
Inventory account equals the value of the stock moved; the overhead absorbed equals what was applied; the variance is
the scrapped loaves. A second test locks the month: a later change posts only the difference in the next open month,
material all given back brings the order's work in progress back to nothing, a new order is dated in the next open
month, and an emptied month in an open period leaves an entry whose lines cancel.

## 4. Sync: what goes to the tills

| Table | Where | Why |
|---|---|---|
| `BillOfMaterials`, `BomLine`, `BomByProduct` | Down to every till | Company-wide, made on the server; the floor needs them to explode and backflush |
| `WorkCentre`, `RoutingOperation` | Down to the work centre's store | A store's; merged on store and code |
| `ProductionOrder`, `ProductionOrderLine`, `ProductionOrderOperation` | Both ways, within the store | Released on the web, started and finished on the floor |
| `ProductionReport`, the production stock movements | Both ways, within the store | The floor's work goes up; nothing on the order is a count, so tills never overwrite each other |
| `MrpRun`, `MrpSuggestion`, `SupplierLeadTime`, `QualityPlan`, `QualityCheck`, `QualityInspection`, `QualityResult` | Server only (`ILocalOnly`) | Planning and quality are office work |
| Journal entries | Server only | The ledger |

## 5. The screens

**Web** (in the desktop's design; the Manufacturing group of the navigation, and Ctrl+K finds every page and the actions
*New production order*, *New bill of materials* and *Run planning*):

| Page | What, and its keys |
|---|---|
| Production orders | A board by status, the most urgent first; Alt+N new order, F8 post costs |
| Production order | Header, then tabs Materials (scan box, Enter issues; Ctrl+I issue the list; F4 scrap), Operations (start, report, finish per step), Output and quality (Enter receives; Ctrl+Q inspect), Costs (standard against actual, material usage and price, the ledger entries), History. F9 release, Ctrl+O change, Del cancel, F7 close, F8 post costs, Esc back |
| Bills of materials | Every version, the one in force marked; Alt+N new |
| Bill of materials | Components, by-products, routing (Insert adds a line), the exploded tree, the cost for a lot, versions compared; Ctrl+S save, F7 copy as a new version, Del delete |
| Routings | A bill's operations beside the list of bills; Insert adds, Ctrl+S saves |
| Work centres | Rates and hours; Alt+N new, Del deletes in the dialog |
| Planning | Run (F5), choose suggestions and change their quantity, F9 firm, Del dismiss; supplier lead times (Ctrl+S) |
| Capacity | Load by work centre and week, coloured when nearly full or over |
| Quality | Plans (Alt+N, Insert adds a check), inspections, goods received to inspect |
| Production reports | The seven reports with dates (F5), print (Ctrl+P) and CSV (Ctrl+E) |

**Desktop** (the till): *Production* (Ctrl+Shift+F), for roles with `production.floor` or `production.view`. The
orders released to the station's location, the most urgent first; on the chosen order its operations (with the
instructions) and materials. Scan or type a material and press Enter to issue it; F6 start operation, F7 finish
operation, F8 report work, F9 issue the list, F4 scrap material, F11 receive output (and print its labels), F12 finish
order, F5 refresh, Ctrl+P print labels. It works without the server. The help has a topic for it, and the `--smoke`
check makes a small order, releases it, opens the Production screen, starts the step, scans the material by its
barcode, reports the work, receives and finishes the order, and checks the stock, the cost of the output and its label.

## 6. Endpoints

The endpoints are listed in [API.md](API.md), section "Manufacturing (phase E5)". The web app's types are in
`web/src/lib/production-types.ts`; a server test (`ProductionApiTests.Every_manufacturing_answer_has_the_shape_of_the_web_apps_types`)
reads that file and checks every answer against it, field by field, so the server and the web cannot drift apart.

## 7. Tests

| Where | What |
|---|---|
| `tests/RetailPlus.Data.Tests/ManufacturingTests.cs` | Bills (versions, copies, comparison, loops), explosion and roll-up, work centres, an order from release to close, reservations and cancel, planning with firming and capacity, quality, every report; SQLite and SQL Server |
| `tests/RetailPlus.Data.Tests/ProductionCostingTests.cs` | The postings check above; SQLite and SQL Server |
| `tests/RetailPlus.Server.Tests/ProductionApiTests.cs` | Through the API: an order made on the web, worked on a till offline, synced back and posted; planning, capacity, quality and reports per store and permission; the web app's shapes |
| `web/src/lib/production.test.ts` | The bill editor's checks, the tree, the capacity grid, the inspection preview |
| `web/e2e/manufacturing.spec.ts`, `web/e2e/layout.spec.ts` | Every page against the mocked API (`production-mock.ts`, `production-fixtures.ts`), the keys, and no sideways scroll on a phone |

## 8. What is left

- **Lots and expiry (E2).** Production issues and receipts do not choose or record batches yet; once E2's batches are
  merged, issues should follow first-expiry-first-out and output should get a batch.
- **Labour against payroll (E3).** Labour is absorbed at the work centres' rates and credited to Manufacturing overhead;
  payroll posts what people are paid. The difference (under- or over-absorbed overhead) is left in that account for the
  accountant; there is no automatic link from hours reported to payslips.
- **Scrap of a backflushed component.** Material scrapped counts towards what was planned, so scrapping a backflushed
  component lowers what is taken at the end. Throw away backflushed material only when it replaces what the order uses.
- **Routing alternatives and subcontracting** (sending work out) are not there; nor are operation-level schedules (the
  capacity view spreads an order's time over its planned days).
- **Quality on the till.** Inspections are made on the web; the floor cannot record one at the till.
- **Several output locations per order**, and serial numbers on made items, are not supported.

## 9. Merging after E1 to E4 (done on 2026-11-04; kept as a record)

- **Migrations:** regenerate both `InitialCreate` migrations after the merge, as for every phase.
- **Shared lists E5 added to** (at the end, or in a block of their own): `Permissions.cs` (four keys, two roles, and
  `production.view` for managers), `AuditActions` and `AuditedTables`, the DbSets and `Sequences.All`
  (`ProductionOrderNumbers`) of `RetailPlusDbContext`, `StockMovementKind` (three kinds; `Kind` is stored as text up to
  20 characters), `SyncPolicy`, the `MapProduction` call in `Endpoints.cs`, the posting after a package in
  `ServerSync.cs`; on the web `nav.ts` (permission keys, icon names, pages, actions), `icons.tsx`, `e2e/fixtures.ts`
  (permissions), `e2e/mock-api.ts` (the call to `productionAnswer`) and `e2e/layout.spec.ts` (pages).
- **A shared component changed:** pages may carry a `group`, and the navigation panel shows a heading before a group's
  first page (`nav-panel.tsx`); the Ctrl+K palette finds a page by its group's name. Another phase that groups its pages
  can use the same field.
- **The ledger (E1):** production uses `LedgerService.PostAsync`, `IsOpenAsync` and `EnsureYearAsync`, the roles
  Inventory, WorkInProgress, ManufacturingOverhead and ProductionVariance, and the kinds Production and Adjustment. The
  daily journal must keep leaving out the production movement kinds, or production is posted twice.
- **Batches (E2):** see "What is left".
