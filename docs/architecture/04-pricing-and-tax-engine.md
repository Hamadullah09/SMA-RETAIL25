# 04 — Pricing & Tax Engine

This is the highest-risk logic in the system: it decides money. It lives in `Retail25.Domain`,
is **pure** (no I/O, no clock — `PricingContext` carries the date), and is covered by a golden-file
suite before any of it is written.

Legacy sources: guide p.6 (item detail overrides), p.11 (per-sale tax suspension), p.34–35 (price
levels, break points, sale pricing, bonus pricing), p.51–52 (client discount / price level / tax
exemptions), p.76–77 (tax configuration), p.83–84 (loyalty rewards, minimum tender), p.98 (Type 2
random-weight barcodes).

---

## 1. Inputs

```csharp
public sealed record PricingContext(
    DateOnly BusinessDate,
    TaxConfiguration Tax,           // effective-dated snapshot
    PosPolicy Policy,               // ApplyTax1, ApplyTax2, AllowTaxOverride,
                                    // ApplyAddOnCharge, StaffMayDiscount, MinimumTender
    CustomerPricingProfile? Customer,
    CartTaxOverride? SaleOverride,  // F11 Special / F6 Taxes
    LoyaltyPolicy Loyalty);
```

```csharp
public sealed record LineInput(
    Product Product,
    ProductVariant? Variant,
    decimal Quantity,
    decimal? ManualUnitPrice,       // staff override, or embedded random-weight price
    decimal? ManualDiscountPct,
    int? RequestedPriceLevel,       // F5 at POS
    bool? Tax1Override,             // F6
    bool? Tax2Override,             // F7
    LineType Type,                  // Sale | Return | TradeIn
    PriceSource Source);            // Rfid | Barcode | StockCode | Manual | RandomWeight
```

---

## 2. Unit-price resolution — precedence ladder

Evaluated **top to bottom; first match wins.** Each step records a `PriceOrigin`, which is persisted
on `SaleLine` so any receipt can be explained after the fact.

| # | Rule | Condition | Result | Guide |
|---|---|---|---|---|
| 1 | **Manual override** | Staff entered a price on the item-detail window | that price → `PriceOrigin.Manual` | p.6 |
| 2 | **Random-weight embedded price** | Type 2 barcode, `Price1 > 0` | unit price = `Price1`; **quantity** = `EmbeddedPrice / Price1` → `RandomWeight` | p.98 |
| 3 | **Bonus / BOGO** | `BonusPricing.Enabled` and `qty >= BuyQty` | free units priced 0; chargeable = `qty - floor(qty/BuyQty)*FreeQty` → `Bonus` | p.35 |
| 4 | **Volume break point** | `qty >= PriceBreak[L].MinQuantity` for the highest qualifying level *L* | `ProductPrice[L]` → `Break` | p.34 |
| 5 | **Requested price level (F5)** | staff picked level *L* **and** has `Pricing.SelectLevel` permission **and** `ProductPrice[L]` exists | `ProductPrice[L]` → `Level{L}` | p.6, p.34 |
| 6 | **Customer's assigned price level** | `Customer.PriceLevel = L` **and** `ProductPrice[L]` exists | `ProductPrice[L]` → `ClientLevel` | p.52 |
| 7 | **Sale pricing window** | `BusinessDate` within `[StartsOn, EndsOn]` | `RegularPrice * (1 - DiscountPct)` → `Sale` | p.35 |
| 8 | **Regular price** | fallback | `Product.RegularPrice` → `Regular` | p.32 |

> **Legacy semantics preserved (p.52):** *"If that price is available for any given item it will
> automatically be applied, otherwise the regular price is used."* — i.e. a missing level falls
> through, it does not error.

> **Ordering decision (rules 4 vs 7):** the guide says a sale price applies *"unless one of the
> other pricing features applies"* (p.35), so break points and explicit level selection outrank the
> date-ranged sale price. This is a **decision to confirm with you** — it is a one-line change if
> your stores expect sale price to always win.

### Line discount

```
lineNet = unitPrice * chargeableQty
lineDiscountPct = ManualDiscountPct                       (requires Sales.Discount permission)
               ?? Customer.UsualDiscountPct               (p.51)
               ?? 0
lineNet -= lineNet * lineDiscountPct / 100
```
`Return` and `TradeIn` lines negate `lineNet` and produce negative tax.

---

## 3. Tax resolution — per line

```
tax1Applies = SaleOverride?.Tax1                 // F11 Special / F6 Taxes, current sale only
           ?? LineOverride.Tax1                  // F6 on item detail  (needs AllowTaxOverride)
           ?? (Policy.ApplyTax1 && Product.Tax1Applies && !Customer.ExemptTax1)
```
identically for tax 2. Precedence: **sale-level override → line override → product flag ∧ policy ∧
customer exemption.**

Two legacy behaviours are load-bearing:

1. **The per-sale tax override is not retroactive.** Guide p.11: *"This command allows you to change
   those settings only for the current sale and only for the items that are not already on the POS
   screen."* → `CartTaxOverride` stamps `AppliesFromSequence = cart.NextLineSequence`; lines with a
   lower sequence keep their original flags.
2. **Overrides require `AllowTaxOverride`.** If the policy is off, the F6/F7 keys are disabled and
   the server rejects the field.

`ProductType.GiftCard` forces both taxes off — tax is charged when the card is *used* (p.106).

---

## 4. Sale-level pipeline — strict order

```
 1. lines[]                    → resolve unit price, qty, discount, tax flags   (§2, §3)
 2. Subtotal                   = Σ lineNet
 3. Subtotal adjustments (applied to Subtotal, in this order):
      a. Coupons                 (fixed amounts, p.7)
      b. Bottle return credits   (p.7)
      c. Trade-in credits        (negative lines, already in Subtotal)
      d. Subtotal discount       (F3-F2, % or fixed, p.7)
      e. Loyalty reward          — ONLY if (d) did not apply  (p.84, verbatim legacy rule)
      f. Gift-certificate redemption is a TENDER, not a discount → step 8
 4. DiscountedSubtotal         = Subtotal - Σ adjustments   (floored at 0)
 5. Add-on charge              = DiscountedSubtotal * AddOnRate            (p.76)
                                 applied only if Policy.ApplyAddOnCharge and not suspended for the sale
 6. Tax base
      TaxableBase1 = Σ (line.net_after_prorated_subtotal_discount) where tax1Applies
                     + (AddOnCharge if AddOnTaxable)                       (p.77)
      Tax1 = round(TaxableBase1 * Tax1Rate/100)
      TaxableBase2 = same for tax2
                     + Tax1 if Tax2Compound                                (p.77)
      Tax2 = round(TaxableBase2 * Tax2Rate/100)
 7. GrandTotal = DiscountedSubtotal + AddOnCharge + Tax1 + Tax2
 8. Tenders (N-way split): cash, card, debit, gift card, gift certificate,
    cheque, on-account, foreign currency × rate                            (p.8–9)
 9. Cash rounding: change and any cash tender round to Policy.MinimumTender (p.84)
10. Loyalty accrual: floor(pointsPerDollar * PreTaxPreChargeSubtotal)       (p.83)
```

### Subtotal-discount proration (a decision the legacy docs leave implicit)

A subtotal discount must reduce the taxable base, otherwise a tax-exempt line subsidises a taxable
one. We **prorate the subtotal discount across lines by their net contribution**, then compute tax
per line. Rounding residue (≤ 1 minor unit) is assigned to the largest line — deterministic, and
verified by a property test asserting `Σ prorated == discountTotal` exactly.

### Tax-inclusive mode (p.77)

When `TaxInclusive = true`, `RegularPrice` already contains tax. The engine back-solves:

```
netOfTax = gross / (1 + r1 + r2)                        // non-compound
netOfTax = gross / ((1 + r1) * (1 + r2))                // Tax2 compound
tax1 = netOfTax * r1 ;  tax2 = (compound ? (netOfTax + tax1) : netOfTax) * r2
```
Line-level rounding uses banker's-free `MidpointRounding.AwayFromZero` at 2 dp (retail convention),
applied **once per line per tax**, never on the running total — this is the classic penny-drift bug
and it is pinned by tests.

---

## 5. Random-weight (Type 2) barcodes — exact legacy contract

Format `2ABBBBCDDDDE` (12 digits, p.98):

| Segment | Meaning |
|---|---|
| `2` | number-system character; **required** |
| `A` | package code (from the scale) |
| `BBBB` | item identifier |
| `ABBBB` | **the 5-digit stock code stored on the product** (zero-padded) |
| `C` | price check digit — placeholder, ignored |
| `DDDD` | embedded net price, format `99.99` |
| `E` | modulo check digit |

Rules implemented verbatim:
- Recognised only when the barcode is 12 digits, starts with `2`, **and**
  `Station.ScanRandomWeightBarcodes` is on.
- `quantity = embeddedPrice / Product.Price1` where `Price1` = weight unit price.
- If `Price1` is null or 0 → **quantity = 1** and the embedded price is the line price.
- If staff overrides the price, the override is treated as **price per unit weight** and multiplied
  by the weight derived from the embedded price.
- Rounding is acknowledged as approximate (guide says so explicitly); we round quantity to 4 dp.

---

## 6. Loyalty / bonus points (p.83–84)

```
earned      = floor(PointsPerDollar * subtotalBeforeTaxAndCharges)
qualifies   = customer.Points >= MinimumRequired
              && no subtotal discount already applied on this sale     ← legacy rule
reward      = PercentEnabled && FixedEnabled ? min(subtotal * pct/100, fixedAmount)
            : PercentEnabled ? subtotal * pct/100
            : fixedAmount
on redeem   → points -= MinimumRequired
on return   → clawback points proportionally (p.84: "If a customer returns an item the point
              total is reduced again")
```
All movements land in `LoyaltyLedgerEntry`; `Customer.RewardPoints` is the derived snapshot.

---

## 7. Costing & margin

- `AvgCost` is a **moving average**, updated on `PostShipment`:
  `newAvg = (onHand*oldAvg + qtyRecvd*costEach + allocatedFreight) / (onHand + qtyRecvd)`
  (guide p.68: freight distributed across received items).
- `LastCost` = cost on the most recent posted order (p.31, 3-decimal precision, p.37).
- `GrossMargin = ((price - cost) / price) * 100`; `SuggestPrice(cost, margin) = cost / (1 - margin/100)`
  (p.32 — note this is margin, *not* markup; the guide is emphatic and cashiers get it wrong).
- `SaleLine.UnitCostSnapshot` freezes `AvgCost` at sale time so COGS reports are stable.

---

## 8. Testing strategy — parity by construction

| Layer | Approach |
|---|---|
| **Golden files** | `tests/goldens/pricing/*.json`: input cart + expected totals, each traceable to a guide page or a legacy screenshot. Written **before** the engine. |
| **Property tests** | FsCheck/CsCheck invariants: `Σ lineTax == saleTax`; `GrandTotal == Σ tenders` (± rounding to MinimumTender); discount proration sums exactly; tax-inclusive round-trips to the sticker price. |
| **Differential tests** | If a legacy install is available, replay real historical sales through the new engine and diff totals to the cent. This is the strongest parity evidence available — worth doing if you can supply a `TOTAL001.DBF` + sales log. |
| **Snapshot tests** | `Verify` over rendered receipts, so escape-code and layout changes are visible in review. |

## 9. Decisions needing your confirmation

| # | Decision | Default taken |
|---|---|---|
| P1 | Sale-price window vs break points/price levels precedence (§2) | Break points and explicit level win over sale pricing |
| P2 | Subtotal discount proration for tax purposes (§4) | Prorated by line net; residue to largest line |
| P3 | Rounding mode | Away-from-zero at 2 dp, per line per tax |
| P4 | Cash rounding | Applied to cash tenders/change only, at `MinimumTender`; non-cash tenders exact |
| P5 | Do returns claw back loyalty points at the original earn rate or the current rate? | Original rate (stored on the loyalty ledger entry) |
