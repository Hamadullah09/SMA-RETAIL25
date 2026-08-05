# 12 — Schema Reference

The physical schema, as the database actually holds it. [03](03-domain-model.md) is the model and
the reasoning; this is the DDL, grouped by the module a reader is likely to be looking for.

The full script is [`schema.sql`](schema.sql) — 93 tables, 80 indexes, generated rather than
maintained by hand:

```bash
dotnet ef migrations script --project src/Retail25.Infrastructure --startup-project src/Retail25.Api -o docs/architecture/schema.sql
```

Regenerate it whenever a migration lands. A schema document written by hand is a schema document
that is wrong within a month, and wrong in the direction that costs the most: it describes the
system somebody thinks they built.

## Conventions that hold everywhere

| | |
|---|---|
| **Engine** | SQL Server 2019 or later. Migrated from PostgreSQL 16 — see [The move from PostgreSQL](#the-move-from-postgresql) below. |
| **Keys** | `id bigint NOT NULL IDENTITY`. Assigned by the database, so an entity's id is `0` until `SaveChanges` returns. |
| **Naming** | `snake_case`, plural tables, via EF's naming convention. Not SQL Server house style, and kept anyway: the column names are a published interface — the schema reference, the reporting views a store's accountant writes, the external system that wanted numeric ids — and renaming ninety tables' worth of columns to earn a convention nobody outside the database can see is all of the risk and none of the benefit. |
| **Money** | `decimal(19,4)` unless a configuration narrows it. Costs carry three decimals and quantities four; the legacy system carried three decimals of cost, and rounding it on import changes every margin figure the store has ever seen. |
| **Time** | `datetimeoffset`, always written as UTC. |
| **Soft delete** | `is_deleted`, `deleted_at`, `deleted_by`. Unique indexes are filtered `WHERE [is_deleted] = 0` so a deleted code can be reused. |
| **Audit** | `created_at`, `created_by`, `modified_at`, `modified_by` on anything a person edits. |
| **Concurrency** | `row_version bigint` — present, and **not currently a concurrency token**: nothing maps it and nothing checks it. Tracked separately; do not rely on it. |
| **Enums** | Stored as `nvarchar`, converted in the model. A number in a column is unreadable in a support session and silently reordered by an insertion into the enum. |

### The move from PostgreSQL

The system ran on PostgreSQL 16 through Phase 7 and moved to SQL Server after it. Six things
changed; everything else was portable as written, and no handler, query or domain type was touched.

| | |
|---|---|
| **Provider** | `UseNpgsql` → `UseSqlServer`; `Hangfire.PostgreSql` → `Hangfire.SqlServer`; `Testcontainers.PostgreSql` → `Testcontainers.MsSql`. |
| **JSON columns** | `jsonb` → `nvarchar(max)`. Nothing queried inside them — they are read as strings — so the only loss is an indexing option nobody was using. |
| **Filtered indexes** | SQL Server takes only simple comparisons, so `WHERE NOT is_deleted` became `WHERE [is_deleted] = 0`. A negation is rejected at `CREATE INDEX`, not at anything that names the line that wrote it. |
| **Sequences** | `nextval('x')` → `NEXT VALUE FOR [x]`, and `CREATE SEQUENCE IF NOT EXISTS` → an explicit `OBJECT_ID(…, 'SO') IS NULL` guard. The property that matters — monotonic, unaffected by rollback — is the same on both. |
| **Decimal precision** | The one with teeth. An unspecified `decimal` was `numeric` on PostgreSQL: arbitrary precision, nothing to think about. On SQL Server EF maps it to `decimal(18,2)` and silently truncates. Sixty-six properties were relying on that, including tax amounts, change given and every cost. Fixed by a convention (`HavePrecision(19, 4)`) rather than sixty-six edits, so a decimal added next year is covered too. |
| **Batch size** | EF batches 42 statements on SQL Server against Npgsql's 1000. Raised to 200, and one operation had to be rewritten — see below. |

**What the move exposed.** Validating a migration batch loaded every staging row and wrote a verdict
to each through the change tracker: twenty thousand UPDATE statements for a twenty-thousand-item
inventory export. At a thousand statements per round trip that was seconds; at tens it was sixteen
minutes. The shape was always wrong and the engine is what made it visible. It is now one statement
for the rows that are fine plus one per row that has a finding.

There is no application-layer knowledge of the engine. The one place that had to learn about it is
`ApplicationDbContext.SetStagingVerdictAsync`, which falls back to a row-by-row write when the
provider is not relational — that path exists only for the in-memory provider the handler unit tests
run on, and the real one is covered by the twenty-thousand-row integration test.

---

## Inventory

The catalogue and everything that counts it.

| Table | Holds |
|---|---|
| `products` | The item. Every field from the legacy inventory screen. |
| `product_variants`, `matrix_dimensions` | Matrix items — colour/size/third dimension and the cross product they generate. |
| `serialized_units` | One physical thing. Serial number, EPC, or both. |
| `kit_components` | An assembly's parts, with the quantity of each. |
| `product_prices`, `price_breaks`, `sale_pricings`, `bonus_pricings` | The four price levels, quantity breaks, dated sales and buy-N-get-M. |
| `product_images` | The picture the till's grid draws. Separate table so the catalogue query does not carry it. |
| `stock_levels` | On hand and on order, per product per location. |
| `stock_ledger_entries` | Every movement, ever. The stock figure is derived from this and reconciled against it. |
| `stock_counts`, `stock_count_lines` | Physical counts and their variances. |
| `stock_transfers`, `stock_transfer_lines` | Stock moving between locations. |
| `departments`, `categories` | The two-level grouping. |

Parent/child (case and single) is `products.parent_product_id` — a self-reference rather than a
join table, because a case breaks into exactly one kind of unit.

```sql
CREATE TABLE [serialized_units] (
    [id] bigint NOT NULL IDENTITY,
    [product_id] bigint NOT NULL,
    [variant_id] bigint NULL,
    [serial_number] nvarchar(64) NULL,
    [epc] nvarchar(96) NULL,
    [state] nvarchar(20) NOT NULL,
    [location_id] bigint NOT NULL,
    [received_on] datetimeoffset NOT NULL,
    [last_seen_at] datetimeoffset NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_serialized_units] PRIMARY KEY ([id])
);
```

`serial_number` and `epc` are both nullable and at least one is required — enforced in the domain
rather than by a check constraint, because the error a clerk needs to read is "a serial number or
EPC is required", not a constraint name. A store with no RFID uses serials only and never touches
the EPC column; the partial unique index below is what makes that free.

`state` is the EPC lifecycle: `Provisioned → InStock → InCart → Sold → Returned → InStock`, with
`Transferred` and `Lost` reachable from anywhere. Every transition is a method on the entity, and
the importer walks the same path rather than assigning a state directly — a tag that arrives
already `Sold` without ever having been in stock is a stock figure nobody can reconcile.

## Point of sale

Two families, deliberately separate.

**In flight** — `carts`, `cart_lines`, `cart_adjustments`, `cart_tax_overrides`. A cart is mutable,
lives mostly in Redis, and is addressed by `sequence` within the cart rather than by row id: a line
that has never been saved has no id, and a till must be able to void line 3 of an unsaved sale.

**Settled** — `sales_transactions`, `sale_lines`, `sale_tenders`, `sale_adjustments`,
`sale_tax_snapshots`. Immutable. A void is a new transaction that reverses an old one, never an
update, because a receipt that has been handed to a customer is a fact.

`sale_tax_snapshots` is the one that looks redundant and is not: it records the rates that were in
force at the moment of the sale. Tax rates change, and a return processed after a rate change has
to refund what was actually charged.

Split tender is `sale_tenders` — many rows per transaction, each with its own type, amount and
reference. Suspended sales and layaways are `carts` with a status and `layaways` / `layaway_lines` /
`layaway_payments` respectively.

## Clients

| Table | Holds |
|---|---|
| `customers` | Name, company, billing and ship-to addresses, contact details, birthday, notes. |
| `customer_accounts` | Credit limit and balance due. One per customer, created with the customer. |
| `customer_pricing_profiles` | Usual discount, price level 1–4, the two tax exemptions. |
| `loyalty_ledger_entries`, `loyalty_policies` | Reward points earned and spent, and the rules that govern them. |
| `customer_orders`, `customer_order_lines` | Special orders. |
| `price_quotes`, `price_quote_lines` | Quotations that can become sales. |

The account and the pricing profile are separate tables rather than columns on `customers` because
they answer to different permissions: a supervisor may set a credit limit without being able to
change a discount, and vice versa.

Purchase history is a query over `sales_transactions`, not a stored total. A denormalised
lifetime-value column is a column that disagrees with the sales log the first time a sale is voided.

## Accounts receivable

`invoices` → `invoice_payments`, with `ar_ledger_entries` as the movement log and
`late_charge_policies` driving the charge run. Open balance is `invoices.balance_due`, maintained in
the same transaction as the payment that changes it.

A customer with a non-zero balance cannot be deleted. That is a rule in the delete handler and not
a foreign key, because the useful behaviour is a message a clerk can act on — "settle the account
first" — rather than a constraint violation.

## Purchasing

`suppliers` → `purchase_orders` → `purchase_order_lines` → `purchase_order_receipts`, plus
`product_suppliers` for the many-to-many between an item and everyone who sells it.

Reordering is driven by `products.reorder_point`, `reorder_qty` and `base_stock` against
`stock_levels.on_hand`. Receiving rolls the moving-average cost, works the quantity off
`on_order`, and writes a `stock_ledger_entries` row — one transaction, three effects, or the stock
figure and the ledger disagree.

## Staff

| Table | Holds |
|---|---|
| `staff_profiles` | Staff code, name, access level 0–4, PIN hash and lockout state. |
| `time_clock_entries` | Clock in and out. |
| `commission_rules`, `commission_ledger_entries` | Fixed or percentage, and what each sale earned. |
| `permissions`, `role_permissions` | The permission catalogue and the role presets. |
| `supervisor_approvals` | Who authorised an override, and for what. |
| `audit_log_entries` | Every change to anything that matters. |

Identity itself is ASP.NET Core Identity's tables keyed to `bigint`, with OpenIddict's application
and token tables alongside. Authorisation is by **permission**, never by role: the legacy access
levels 0–4 are presets that map onto permission sets, so a store keeps its old mental model while an
administrator reshapes what any level can actually do.

`data_protection_keys` is not optional. Without it the key ring is regenerated on every restart and
every issued antiforgery token, cookie and reset link stops validating — which presents as "that
form had expired" on the login page and nothing in the logs.

## Configuration and branding

`locations`, `business_profiles`, `tax_configurations`, `pos_policies`, `stations`,
`printer_profiles`, `reader_profiles`, `scale_profiles`, `pole_display_profiles`, `tender_types`,
`currencies`, `number_sequences`, `pricing_rule_settings`, `fiscal_years`, `branding_assets`.

Every escape sequence, baud rate and antenna threshold is a row, never a compiled constant. A shop
that swaps a failed reader for the spare in the drawer gets its settings pushed back into the new
unit on connect, rather than having a till nobody can explain.

```sql
CREATE TABLE [branding_assets] (
    [id] bigint NOT NULL IDENTITY,
    [location_id] bigint NOT NULL,
    [slot] nvarchar(20) NOT NULL,            -- Watermark | CompanyLogo
    [content] varbinary(max) NOT NULL,
    [content_type] nvarchar(40) NOT NULL,
    [e_tag] nvarchar(32) NOT NULL,
    [opacity_pct] int NOT NULL,
    ...
    CONSTRAINT [pk_branding_assets] PRIMARY KEY ([id])
);

CREATE UNIQUE INDEX [ix_branding_assets_location_id_slot] ON [branding_assets] ([location_id], [slot]);
```

That unique index is what makes "one image per slot" true in the database rather than only in the
handler. Two administrators uploading a logo at the same moment is a race, and a shop that sees its
old logo on one till and its new one on another has no way to tell which row will win next.

## Migration

`migration_batches`, `migration_staging_rows`, `external_entity_maps`, `sync_logs`,
`sales_history_archives`. Legacy DBF files land in staging, are validated there, and are promoted
into the real tables only when a batch passes — a half-imported catalogue is worse than none.

---

## EPC lookups at the till

The question the brief asks: what makes an EPC lookup instantaneous during checkout.

### The index

```sql
CREATE UNIQUE INDEX [ix_serialized_units_epc]
    ON [serialized_units] ([epc]) WHERE [epc] IS NOT NULL;
```

Three decisions in one line.

**Unique**, because one EPC is one physical unit. Uniqueness is not a nicety here — it is what lets
the resolver stop at the first row instead of scanning for a second, and it is what stops a
mis-encoded roll of labels from putting the same tag on two items and making stock unreconcilable.

**Filtered**, `WHERE [epc] IS NOT NULL`, and on SQL Server this one is not optional. A store that
tracks serial numbers without RFID leaves the column null on every row, and SQL Server treats two
NULLs as equal in a unique index — so without the filter the *second* non-RFID unit ever created
would be rejected as a duplicate of the first. PostgreSQL, where this index was first written,
treats NULLs as distinct and would have let it pass; the filter was there for index size and turned
out to be load-bearing for correctness.

**A b-tree on the raw column**, not a hash and not a computed column. Every lookup is an exact match
on a normalised value, which is what a b-tree equality seek is best at: three or four page reads at
any catalogue size this application will ever see. It also answers prefix queries, which is how a
supervisor finds "every tag from that roll" when a batch was encoded wrongly.

Normalisation is what makes the exact match work. Every EPC is uppercased and stripped of
whitespace before it is stored *and* before it is queried, in one place
([`Epc.Create`](../../backend/src/Retail25.Domain/ValueObjects/Epc.cs)). Half the tag exports in
the world write `E2 80 11 70 …` and the other half write `E28011700…`; if that normalisation
happened at only one of the two ends, the index would be perfect and never hit.

### The path a tag actually takes

An index is the last thing that matters here, not the first. At a counter the query is not the cost:

1. **The reader's own field.** The terminal agent coalesces reads inside a configurable window
   (`reader_profiles.coalesce_ms`) — a tag in the field is reported dozens of times a second, and
   dozens of identical lookups per tag is the actual load.
2. **The batch.** A basket arrives as one `AddRfidBatchCommand`, not thirty. Thirty round trips is
   thirty times the latency, and the round trip dominates the query by two orders of magnitude.
3. **The in-batch deduplication.** One antenna sweep reports the same tag from two angles; the
   handler groups by EPC before touching the database.
4. **The process-level cache.** `TagStreamRegistry.Catalogue` holds EPC → item **including misses**.
   A shop always has tags that will never resolve — a customer's own coat, a returned item's old
   label — and those are precisely the ones that would otherwise hit the database hardest, because a
   miss never gets cached by anything downstream. The cache is invalidated per EPC whenever a tag is
   commissioned or imported; without that invalidation, tags a supervisor has just mapped go on
   reading "not recognised" on every till indefinitely while the database says otherwise.
5. **The index**, for what is left.

The target is in [10](10-nfr-deployment-testing.md): tag to rendered line, 300 ms at p95.
`BulkReadTests.cs` puts 300 tags through one batch and asserts no duplicates, no rejections and a
wall clock well under a per-tag round trip — a shape assertion, not the budget itself, because it
runs against an in-memory provider where a timing number would mean nothing. The budget is measured
where it is real, in the Playwright run against a live stack.

Either way, the index is not where that time goes.

### The second index

```sql
CREATE INDEX ix_serialized_units_product_id_state ON serialized_units (product_id, state);
```

This is not for scanning. It is for the picker that opens when a serialized item is rung by its
parent stock code — "which of these forty units is the customer holding" — and for the stock query
that asks how many units of an item are actually on the shelf. Product first because it is always
constrained; state second because it is the filter applied within a product.

### What is deliberately absent

No index on `last_seen_at`. It is written on every read of every tag and queried by nobody at the
till — indexing it would add a write to the hottest path in the system to serve a report that runs
once a week.
