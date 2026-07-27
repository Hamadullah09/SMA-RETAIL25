using Retail25.Domain.Catalog;
using Retail25.Domain.ValueObjects;

namespace Retail25.Domain.Sales.Pricing;

public enum LineType { Sale, Return, TradeIn }
public enum PriceSource { Rfid, Barcode, StockCode, Manual, RandomWeight }

/// <summary>
/// Input to the unit-price resolution pipeline for a single line (doc 04 §2).
/// </summary>
public sealed record LineInput(
    Product Product,
    ProductVariant? Variant,
    decimal Quantity,
    decimal? ManualUnitPrice,
    decimal? ManualDiscountPct,
    int? RequestedPriceLevel,
    bool? Tax1Override,
    bool? Tax2Override,
    LineType Type,
    PriceSource Source);
