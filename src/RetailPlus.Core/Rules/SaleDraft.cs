using RetailPlus.Core.Model;

namespace RetailPlus.Core.Rules;

/// <summary>A line on the POS screen before the sale is saved.</summary>
public sealed class SaleDraftLine
{
    public SaleLineKind Kind { get; set; } = SaleLineKind.Sale;
    public int? ItemId { get; set; }
    public ItemType ItemType { get; set; }
    public string? Plu { get; set; }
    public string Description { get; set; } = "";
    /// <summary>Negative for returns, credits, coupons and bottle returns.</summary>
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public bool Taxable1 { get; set; }
    public bool Taxable2 { get; set; }
    public string? Department { get; set; }
    /// <summary>Cost per unit for the cost of goods sold (average cost, else last cost).</summary>
    public decimal UnitCost { get; set; }
    public int? VariantId { get; set; }
    public string? VariantText { get; set; }
    public string? SerialNumber { get; set; }
    public string? SupplierName { get; set; }
    public decimal CommissionPercent { get; set; }
    /// <summary>For returns: put the goods back into stock.</summary>
    public bool Restock { get; set; } = true;
    /// <summary>The price came from the pricing engine (re-priced when the quantity changes).</summary>
    public bool AutoPriced { get; set; } = true;
    public string? PriceExplanation { get; set; }
    public decimal ThcWeight { get; set; }
    /// <summary>The RFID tags rung up on this line, one per piece: three tagged chocolates are one line with three tags.</summary>
    public List<string> Epcs { get; set; } = [];

    public decimal LineTotal => Money.Round(UnitPrice * Quantity - DiscountAmount);
}

/// <summary>A sale being rung up: lines, client, discounts and the tax setup in use.</summary>
public sealed class SaleDraft
{
    public List<SaleDraftLine> Lines { get; set; } = [];
    public int? ClientId { get; set; }
    public string? ClientName { get; set; }
    public string? CustomerNumber { get; set; }
    public int ClientPriceLevel { get; set; }
    public decimal ClientDiscountPercent { get; set; }
    public decimal SubtotalDiscountPercent { get; set; }
    public bool ExemptTax1 { get; set; }
    public bool ExemptTax2 { get; set; }
    /// <summary>An out-of-state tax configuration picked for this sale; null uses the store's taxes.</summary>
    public TaxSetup? TaxOverride { get; set; }
    public string? Staff { get; set; }
    public string? Notes { get; set; }
    /// <summary>Bonus points given up for a reward; the reward itself is a coupon line on the sale.</summary>
    public decimal PointsRedeemed { get; set; }
    /// <summary>Save as a layaway: the payments are a deposit and the rest stays owing.</summary>
    public bool IsLayaway { get; set; }

    public TaxSetup EffectiveTax(StoreSettings settings) => TaxOverride ?? new TaxSetup(settings.Tax1Name, settings.Tax1Rate,
        settings.Tax2Name, settings.Tax2Rate, settings.Tax2Compound, settings.TaxInclusive);

    public SaleTotals Calculate(TaxSetup tax) => SaleCalculator.Calculate(
        Lines.Where(l => l.Kind != SaleLineKind.Text).Select(l => new CalcLine(l.LineTotal, l.Taxable1, l.Taxable2)).ToList(),
        tax, SubtotalDiscountPercent, ExemptTax1, ExemptTax2);

    public decimal TotalThc => Lines.Where(l => l.Kind == SaleLineKind.Sale).Sum(l => l.ThcWeight * l.Quantity);

    /// <summary>
    /// The line that more of an item joins, so the bill shows each item once with its quantity: same item, option and
    /// kind, whole quantities, no serial number and no hand-made discount. Null when the item needs a line of its own
    /// (serialized pieces, gift cards, weighed goods).
    /// </summary>
    public static SaleDraftLine? LineToJoin(IEnumerable<SaleDraftLine> lines, Item item, int? variantId, SaleLineKind kind, decimal quantity)
    {
        if (item.ItemType is ItemType.Serialized or ItemType.GiftCard || item.ReadScale || quantity != decimal.Truncate(quantity)) return null;
        return lines.LastOrDefault(l => l.ItemId == item.Id && l.VariantId == variantId && l.Kind == kind && l.SerialNumber is null
                                        && l.Quantity == decimal.Truncate(l.Quantity) && (l.AutoPriced || l.DiscountAmount == 0));
    }
}

/// <summary>One payment entered at the Pay window.</summary>
/// <param name="Amount">The part of the total this tender pays (after change).</param>
/// <param name="Tendered">What the customer handed over; above Amount for cash with change.</param>
public sealed record TenderEntry(string TenderType, TenderKind Kind, decimal Amount, decimal Tendered, string? AuthCode = null, string? Reference = null)
{
    public decimal Change => Math.Max(0, Tendered - Amount);
}
