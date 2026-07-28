# 12 — Legacy CSV Analysis & Revised Data Model

Source: `RETAIL PLUS 2.5/TSTINV11.csv` — 28 data rows, 89 columns, plus a legend block in rows 30–35.
Every number below was computed from the file, not estimated.

---

## 1. What the file actually contains

| Measure | Value |
|---|---:|
| Data rows | 28 |
| Distinct product names | 25 |
| Distinct PLUs | 26 |
| Distinct EPCs | 28 |
| Sum of `ONHAND` | 1,293 |
| EPC length | 24 hex characters, on every row |

The legend block confirms the intended semantics:

| Legend cell | Meaning |
|---|---|
| `EPC` → `24 CHR HEXADECIMAL` | EPC is a fixed 24-character hex string |
| `PWEIGHT` / `TOLRANCE` → `MILIGRAM, GRAM, KG` | weight and tolerance carry a **unit**, chosen from a list |
| `SSTATUS` → `DEFAULT=0`, `SOLD=1` | per-unit lifecycle state |
| `SALES RECPT.` | the receipt a unit was sold on |

Those four columns are the whole point: `EPC`, `SSTATUS` and `RECEIPT#` describe **one physical unit**,
while every other column describes **a product**. The file flattens the two into one row.

---

## 2. The central finding — one row is not one thing

`COLUMBIA POLO` has `ONHAND = 72` and **one** EPC. Seventy-two garments cannot share a tag; each
carries its own. The CSV supplies a *sample* tag per product, not the tag set.

So the row is really two records:

```
row  ──►  Product          (PLU, name, dept, category, prices, costs, reorder rules)
     └─►  SerializedUnit×N (one per physical item: EPC, status, receipt, received date)
```

**Full expansion of this file is 1,293 serialized units across 25 products.**

### How the importer handles the shortfall

Only 28 of those 1,293 EPCs are known. Fabricating the other 1,265 would put tag identifiers in the
database that no physical tag carries — the system would then reject every real tag it ever saw.

| Unit | State on import | Meaning |
|---|---|---|
| The one with a supplied EPC | `InStock`, EPC set | Ready to sell by RFID |
| The remaining `ONHAND − 1` | `InStock`, EPC null, `AwaitingCommission` | Real stock, not yet tagged |

Commissioning (Phase 4) assigns EPCs to those units as tags are encoded or first read. Stock counts
are correct from day one; RFID coverage grows as tagging proceeds. A `--synthesize-epcs` switch
generates placeholder tags for demo and load testing, and is **off by default**.

---

## 3. The four prices

`COLUMBIA POLO`: `UNITPRICE1..4 = 30.00, 27.99, 24.79, 19.99` with `PRICEBRK1..3 = 2, 4, 6`.

The same four columns serve two purposes in Retail Plus, and both are in use here:

1. **Customer segment** — a customer assigned level 3 always pays 24.79 (guide p.52).
2. **Volume break** — anyone buying 4 or more drops to level 3 (guide p.34).

The engine already implements both, at rungs 4 and 6 of the precedence ladder
(see [04](04-pricing-and-tax-engine.md)). What was missing is that the levels had no **names**.

New table, seeded from your description:

| Level | Seeded name | Populated in the file |
|---:|---|---:|
| 1 | Daily Customer | 28 / 28 |
| 2 | Retailer | 11 / 28 |
| 3 | Wholesaler | 7 / 28 |
| 4 | Distributor | 3 / 28 |

> **Assumption to confirm:** you named three segments. Level 4 is seeded as *Distributor*. It is a
> row in `price_level_definition`, so renaming it is a settings change, not a code change.

A level priced at zero means "not offered for this item" and falls through to the next rule — the
documented legacy behaviour (guide p.52), already covered by a passing test.

---

## 4. Data-quality findings

These are facts about your file, and each needs a decision before import.

| # | Finding | Rows affected | Consequence | Proposed handling |
|---|---|---:|---|---|
| D1 | **PLU is not unique.** `9988776654321140` is both `KELTY ZEN TENT` and `ZEIZ TRAVELITE BINOCULARS`; `9988776654321130` is both `LC CROSS TRAINER` and `TIMEX REEF GEAR WATCH`. | 4 | A PLU scan is ambiguous — the till cannot know which item was meant. | Import both, flag the collision, and make the till **prompt** on an ambiguous PLU. EPC stays unambiguous. |
| D2 | **One product spans four PLUs.** `AERO WOMENS SANDALS` appears with PLUs `…180`, `…290`, `…210`, `…070`. | 4 | Looks like size or colour variants entered as separate items. | Import as four products; offer a "merge into a matrix item" step, since `ITEMTYPE` is `STANDARD`, not `MATRIX`. |
| D3 | **Negative on-hand.** `GARMIN VISTA GPS` = −3; two `AERO WOMENS SANDALS` rows = −1. | 3 | Stock sold that the system did not know it had. | Import the negative honestly and raise a reconciliation item. Silently clamping to zero would hide a real shrinkage or receiving error. |
| D4 | **`AVGCOST` is 0 on every row** while `LASTCOST` is populated. | 28 | Margin and cost-of-goods would compute against zero cost. | Seed `AvgCost = LastCost` at import and let the moving average take over from the first receipt. |
| D5 | **`ITEMTYPE = MATRIX` with no matrix defined.** `KELTY ZEN TENT`, `LC CROSS TRAINER`; the `MATRIX` column is empty on every row. | 2 | A matrix item with no dimensions cannot be sold. | Import as matrix, flag as incomplete, block sale until dimensions are defined. |
| D6 | **Link columns hold codes, not keys.** `PARENT=2345`, `SUBCODE=0606`, `SUBCODE=0604`, `TAGALONG=0443` — none of these match any PLU in the file. | 4 | Dangling references. | Resolve after all rows are loaded; unresolved links are reported, not silently dropped. |
| D7 | **`SUPPLIER` is a company name string**, e.g. `ROBINSON & HEATH`. | 24 | No supplier key. | Create suppliers by name on first sight; the reconciliation report lists them for review. |
| D8 | **Two rows carry a malformed trailing block** in the notes area — a quoted CSV field with embedded commas and a fragment like `2F 1 … 3`. | ~15 | Junk in free-text fields. | Parse defensively, preserve the raw text in `import_raw`, do not attempt to interpret it. |
| D9 | `PWEIGHT`, `TOLRANCE`, `SALESTART`, `SALEEND`, `BONUSBUY`, `TWOFORONE`, `BONUSGET`, `NOTES`, `KIT`, `MATRIX`, `SERNO`, `SSTATUS`, `RECEIPT#` are **empty on every row**. | 28 | Nothing to migrate, but the columns must still exist. | Map them; import nulls. |

---

## 5. Column map

Every legacy column has a home. The map itself is a YAML file the importer reads, so a differently
shaped export is a configuration change (`INT-008` in the benchmark).

### → `product`

| CSV | Column | Note |
|---|---|---|
| `PROD` | `name` | |
| `PLU` | `stock_code` | 16 digits here; not unique (D1) |
| `DEPT` | `department_id` | resolved by name, created on demand |
| `CATEGORY` | `category_id` | resolved by name |
| `ITEMTYPE` | `type` | `STANDARD` / `MATRIX` / `SERVICE` present |
| `BSIZE` | `size_label` | free text: `1 LB`, `60 GM`, `1 HR.` |
| `PKG` | `case_qty` | 12 on the two case items |
| `LASTCOST` | `last_cost` | 3 dp |
| `AVGCOST` | `avg_cost` | seeded from `last_cost` (D4) |
| `MARGIN` | *derived* | recomputed, not imported |
| `BASTOCK` / `ROP` / `ROQ` | `base_stock` / `reorder_point` / `reorder_qty` | |
| `ONHAND` / `ONORDER` | `on_hand` / `on_order` | negatives preserved (D3) |
| `TX1` / `TX2` | `tax1_applies` / `tax2_applies` | `T`/`F` |
| `BINLOC` | `bin_location` | |
| `SHIPWEIGHT` | `ship_weight` | |
| `SUBCODE` / `TAGALONG` / `PARENT` | `substitute_product_id` / `tag_along_product_id` / `parent_product_id` | resolved in a second pass (D6) |
| `PICPATH` | `image_path` | legacy BMP path, retained for reference |
| `SALESMSG` / `INVOICEMSG` / `NOTES` | `pos_message` / `invoice_message` / `notes` | |
| `LASTUPD` | `modified_at` | |

### → `product_price` (4 rows per product)

`UNITPRICE1..4` → `(product_id, level, price)`, skipping zeros.

### → `price_break`

`PRICEBRK1..3` → `(product_id, level 2..4, min_quantity)`, skipping zeros.

### → `sale_pricing`

`SALESTART`, `SALEEND`, `SALEPRICE` — empty in this file, mapped for completeness.

### → `bonus_pricing`

`BONUSBUY`, `BONUSGET`, `TWOFORONE` — empty here.

### → `serialized_unit` (the expansion)

| CSV | Column |
|---|---|
| `EPC` | `epc` — 24 hex, unique, nullable until commissioned |
| `SSTATUS` | `status` — `0` → `InStock`, `1` → `Sold` |
| `RECEIPT#` | `sold_on_receipt` |
| `SERNO` | `serial_number` — a serial and an EPC are two labels on one unit, so they share a row |
| `ONHAND` | drives **how many rows are created** |

### → `commission_rule`

`COMMISSION`, `MAXCOMM`, `COMMTYPE` (`None` throughout this file).

### → `monthly_sales_snapshot`

`JAN_GROSS…DEC_GROSS`, `JAN_VOLUME…DEC_VOLUME`, `YTDTOTAL`, `YTDGROSS`, `LASTYR`, `LASTYR_TOT`,
`WEEKLY`, `B4YREND` — twenty-nine columns collapse into one narrow table of
`(product_id, year, month, volume, gross)`. Adding a year stops being a schema change.

### → `product_supplier`

`SUPPLIER` (name), `ORDATE`, `LASTSHIP`, `RECVD`, `SUPLIST`.

---

## 6. Schema changes this analysis forces

| # | Change | Reason |
|---|---|---|
| S1 | `price_level_definition` table — level, name, sort order, active | The four prices need names (Daily Customer / Retailer / Wholesaler / Distributor) and must stay data |
| S2 | `serialized_unit.epc` nullable + `AwaitingCommission` state | 1,265 of 1,293 units are untagged on day one |
| S3 | `serialized_unit.sold_on_receipt`, `serial_number` | `RECEIPT#` and `SERNO` from the legend |
| S4 | `product.size_label` | `BSIZE` has no home today |
| S5 | `weight_unit` on weight columns (`MILIGRAM`/`GRAM`/`KG`) | The legend defines a unit dropdown; a bare number is ambiguous |
| S6 | `monthly_sales_snapshot` narrow table | Replaces 24 wide columns |
| S7 | `import_batch` + `import_raw` + `import_issue` | D1–D8 need to be reported, not swallowed |
| S8 | `product.stock_code` unique index → **per location, non-unique with a collision flag** | D1 proves the legacy data violates uniqueness |

---

## 7. Sequencing

1. `price_level_definition` and the naming (S1) — small, unblocks the pricing UI.
2. `serialized_unit` changes (S2, S3) — the EPC-per-unit model this file is really about.
3. Remaining schema (S4–S7).
4. First EF migration covering the whole schema (benchmark `CFG-009`).
5. Seed data (`CFG-008`), then the importer (`INT-006`, `INT-010`) with a dry-run report.

Nothing here can be exercised end to end until the migration and seed exist, which is why they come
before the importer rather than after it.
