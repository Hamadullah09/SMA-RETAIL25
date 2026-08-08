namespace Retail25.Domain.Sales.Pricing;

/// <summary>
/// A fully priced line. Every field here is frozen onto <c>SaleLine</c> at completion, which is what
/// makes a reprint reproduce the original document to the cent (guide p.56).
/// </summary>
public sealed record ResolvedLine(
    int Sequence,
    long ProductId,
    long? VariantId,
    string StockCode,
    string Name,
    decimal Quantity,
    decimal ChargeableQuantity,
    decimal UnitPrice,
    PriceOrigin PriceOrigin,
    decimal DiscountPct,
    decimal LineGross,
    decimal LineNet,
    decimal ProratedAdjustment,
    decimal TaxableNet,
    bool Tax1Applies,
    bool Tax2Applies,
    decimal Tax1Amount,
    decimal Tax2Amount,
    decimal UnitCost,
    LineType LineType)
{
    /// <summary>What the customer pays for this line before sale-level adjustments.</summary>
    public decimal ExtendedNet => LineNet;

    public decimal CostOfGoods => UnitCost * ChargeableQuantity * (LineType == LineType.Sale ? 1m : -1m);
}

/// <summary>A cart-level adjustment as it was actually applied, after flooring and suppression rules.</summary>
public sealed record AppliedAdjustment(AdjustmentType Type, string Label, decimal Amount);

/// <summary>
/// The output of the whole sale-level pipeline (doc 04 §4). Tax names travel with the result so the
/// totals panel and the receipt can label rows without a second lookup.
/// </summary>
public sealed record SalePricingResult(
    IReadOnlyList<ResolvedLine> Lines,
    IReadOnlyList<AppliedAdjustment> Adjustments,
    decimal Subtotal,
    decimal AdjustmentTotal,
    decimal DiscountedSubtotal,
    decimal AddOnCharge,
    decimal Tax1Total,
    decimal Tax2Total,
    decimal GrandTotal,
    decimal CostOfGoodsSold,
    int LoyaltyPointsEarned,
    int LoyaltyPointsRedeemed,
    string Tax1Name,
    string Tax2Name,
    string AddOnChargeName,
    bool TaxInclusive)
{
    public static SalePricingResult Empty { get; } = new(
        [], [], 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0, 0, string.Empty, string.Empty, string.Empty, false);

    public decimal TaxTotal => Tax1Total + Tax2Total;
}
