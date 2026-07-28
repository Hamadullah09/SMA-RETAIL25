using Retail25.Domain.Catalog;

namespace Retail25.Domain.Sales.Pricing;

/// <summary>
/// How the item reached the cart. Recorded on the line so a manager can tell a bulk RFID read
/// from a keyed stock code after the fact.
/// </summary>
public enum PriceSource
{
    Rfid = 0,
    Barcode = 1,
    StockCode = 2,
    Manual = 3,
    RandomWeight = 4,
}

/// <summary>
/// Input to the unit-price resolution pipeline for a single line (doc 04 §2).
/// </summary>
/// <param name="Sequence">
/// Position of the line in the cart. Load-bearing: a per-sale tax override applies only to lines
/// added after it, so the sequence decides whether the override reaches this line (guide p.11).
/// </param>
/// <param name="Product">The catalog item.</param>
/// <param name="Variant">Matrix variant, when the product is a matrix item.</param>
/// <param name="Quantity">Quantity requested, before any bonus-pricing free units are deducted.</param>
/// <param name="ManualUnitPrice">A price typed by staff. Honoured only with the price-override permission.</param>
/// <param name="EmbeddedUnitPrice">
/// Net price read out of a Type 2 random-weight barcode. Distinct from
/// <paramref name="ManualUnitPrice"/> because it drives a quantity calculation rather than a price
/// substitution (guide p.98).
/// </param>
/// <param name="ManualDiscountPct">A discount typed by staff. Honoured only with the discount permission.</param>
/// <param name="RequestedPriceLevel">Price level picked at the till with F5.</param>
/// <param name="Tax1Override">Tax 1 flag forced on or off for this line (F6).</param>
/// <param name="Tax2Override">Tax 2 flag forced on or off for this line (F7).</param>
/// <param name="Type">Sale, return or trade-in. The latter two credit the customer.</param>
/// <param name="Source">How the item was identified.</param>
public sealed record LineInput(
    int Sequence,
    Product Product,
    ProductVariant? Variant,
    decimal Quantity,
    decimal? ManualUnitPrice,
    decimal? EmbeddedUnitPrice,
    decimal? ManualDiscountPct,
    int? RequestedPriceLevel,
    bool? Tax1Override,
    bool? Tax2Override,
    LineType Type,
    PriceSource Source);
