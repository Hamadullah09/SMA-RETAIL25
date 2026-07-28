using Retail25.Domain.Configuration;

namespace Retail25.Domain.Sales.Pricing;

/// <summary>
/// One fully priced line. Every field is a snapshot: it is written to <c>SaleLine</c> unchanged and
/// is what a reprint renders, so a later change to prices, tax rates or customer settings can never
/// alter a document that has already been issued (guide p.56).
/// </summary>
/// <param name="Sequence">The line's position in the cart.</param>
/// <param name="ProductId">Catalog item sold.</param>
/// <param name="VariantId">Matrix variant, where applicable.</param>
/// <param name="UnitPrice">Resolved price per chargeable unit.</param>
/// <param name="PriceOrigin">Which pricing rule produced <paramref name="UnitPrice"/>.</param>
/// <param name="ResolvedPriceLevel">The price level used, when the price came from one.</param>
/// <param name="ChargeableQuantity">Units charged for.</param>
/// <param name="FreeQuantity">Units given away by bonus pricing.</param>
/// <param name="StockQuantity">Units leaving stock — charged plus free.</param>
/// <param name="GrossAmount">Unit price × chargeable quantity, before discounts.</param>
/// <param name="LineDiscountPct">Discount percentage applied to this line.</param>
/// <param name="LineDiscountAmount">Value of the line discount.</param>
/// <param name="NetAmount">Line value after its own discount, signed for returns.</param>
/// <param name="AllocatedSubtotalAdjustment">This line's share of sale-wide credits and discounts.</param>
/// <param name="TaxableAmount">Net after that share — the amount tax is actually charged on.</param>
/// <param name="Tax1Applies">Whether tax 1 was charged.</param>
/// <param name="Tax2Applies">Whether tax 2 was charged.</param>
/// <param name="Tax1Source">Why tax 1 was or was not charged.</param>
/// <param name="Tax2Source">Why tax 2 was or was not charged.</param>
/// <param name="Tax1Amount">Tax 1 charged on this line.</param>
/// <param name="Tax2Amount">Tax 2 charged on this line.</param>
/// <param name="LineType">Sale, return or trade-in.</param>
public sealed record PricedLine(
    int Sequence,
    Guid ProductId,
    Guid? VariantId,
    decimal UnitPrice,
    PriceOrigin PriceOrigin,
    int? ResolvedPriceLevel,
    decimal ChargeableQuantity,
    decimal FreeQuantity,
    decimal StockQuantity,
    decimal GrossAmount,
    decimal LineDiscountPct,
    decimal LineDiscountAmount,
    decimal NetAmount,
    decimal AllocatedSubtotalAdjustment,
    decimal TaxableAmount,
    bool Tax1Applies,
    bool Tax2Applies,
    TaxDecisionSource Tax1Source,
    TaxDecisionSource Tax2Source,
    decimal Tax1Amount,
    decimal Tax2Amount,
    LineType LineType)
{
    /// <summary>What this line contributes to the amount owed.</summary>
    public decimal TotalWithTax => TaxableAmount + Tax1Amount + Tax2Amount;
}

/// <summary>A sale-wide credit that reduced the subtotal, itemised for the receipt and the ledger.</summary>
/// <param name="Kind">Which kind of credit.</param>
/// <param name="Description">What to print.</param>
/// <param name="Amount">Value credited, always positive.</param>
public sealed record AppliedAdjustment(AdjustmentKind Kind, string Description, decimal Amount);

/// <summary>The kinds of sale-wide credit the till can apply.</summary>
public enum AdjustmentKind
{
    Coupon = 0,
    BottleReturn = 1,
    SubtotalDiscount = 2,
    LoyaltyReward = 3,
}

/// <summary>
/// The complete, immutable outcome of pricing a sale (doc 04 §4). Totals are derived from the line
/// snapshots, never the other way round, so the parts always sum to the whole.
/// </summary>
/// <param name="Lines">Priced lines, in cart order.</param>
/// <param name="Adjustments">Sale-wide credits that were applied.</param>
/// <param name="Subtotal">Sum of line net amounts before sale-wide credits.</param>
/// <param name="AdjustmentTotal">Total credited by coupons, bottle returns, subtotal discount and loyalty.</param>
/// <param name="DiscountedSubtotal">Subtotal less credits, floored at zero.</param>
/// <param name="AddOnChargeName">Name of the percentage add-on charge, for the receipt.</param>
/// <param name="AddOnCharge">The add-on charge applied.</param>
/// <param name="AddOnChargeTax1">Tax 1 on the add-on charge, when it is taxable.</param>
/// <param name="AddOnChargeTax2">Tax 2 on the add-on charge.</param>
/// <param name="Tax1Name">Name of tax 1 at the time of sale.</param>
/// <param name="Tax1Total">Total tax 1, lines plus add-on charge.</param>
/// <param name="Tax2Name">Name of tax 2 at the time of sale.</param>
/// <param name="Tax2Total">Total tax 2.</param>
/// <param name="GrandTotal">Amount owed.</param>
/// <param name="LoyaltyPointsEarned">Points this sale earns.</param>
/// <param name="LoyaltyPointsRedeemed">Points spent on this sale.</param>
/// <param name="TaxationType">Whether prices included tax, recorded for the reprint.</param>
public sealed record SalePricingResult(
    IReadOnlyList<PricedLine> Lines,
    IReadOnlyList<AppliedAdjustment> Adjustments,
    decimal Subtotal,
    decimal AdjustmentTotal,
    decimal DiscountedSubtotal,
    string AddOnChargeName,
    decimal AddOnCharge,
    decimal AddOnChargeTax1,
    decimal AddOnChargeTax2,
    string Tax1Name,
    decimal Tax1Total,
    string Tax2Name,
    decimal Tax2Total,
    decimal GrandTotal,
    int LoyaltyPointsEarned,
    int LoyaltyPointsRedeemed,
    TaxationType TaxationType)
{
    /// <summary>All sales tax on this sale, both taxes, lines and add-on charge together.</summary>
    public decimal TotalTax => Tax1Total + Tax2Total;
}
