using Retail25.Domain.Catalog;

namespace Retail25.Application.Catalog;

/// <summary>
/// One line in the inventory browse grid (doc 08 §Screen inventory). Deliberately narrow: the grid
/// is virtualized over tens of thousands of rows, and every field here is paid for on every one.
/// The form view fetches the rest.
/// </summary>
public sealed record ProductRowDto(
    long Id,
    string StockCode,
    string Name,
    ProductType Type,
    string? DepartmentName,
    string? CategoryName,
    decimal RegularPrice,
    decimal AvgCost,
    decimal GrossMarginPct,
    decimal OnHand,
    decimal OnOrder,
    int ReorderPoint,
    bool Tax1Applies,
    bool Tax2Applies,
    string? Upc,
    bool IsDeleted);

public sealed record ProductPriceDto(int Level, decimal Price);

public sealed record PriceBreakDto(int Level, decimal MinQuantity);

public sealed record SalePricingDto(decimal DiscountPct, DateOnly StartsOn, DateOnly EndsOn);

public sealed record BonusPricingDto(decimal BuyQty, decimal FreeQty);

public sealed record ProductSupplierDto(
    long SupplierId,
    string SupplierName,
    int Rank,
    decimal Cost,
    string? ReorderNumber,
    decimal CaseQty,
    decimal MinimumOrderQty);

/// <summary>A linked item, resolved to its code and name so the form can show it without a second call.</summary>
public sealed record LinkedProductDto(long Id, string StockCode, string Name);

/// <summary>
/// Everything the Form View shows for one item (guide p.30–44).
/// <para>
/// Sent whole rather than section by section. The legacy screen was a set of tabs over one record,
/// and a user who edits pricing and then switches to ordering expects both to save together; loading
/// each tab separately would make a half-saved item possible.
/// </para>
/// </summary>
public sealed record ProductFormDto(
    long Id,
    long LocationId,
    string StockCode,
    string Name,
    string? Description,
    ProductType Type,
    string? Upc,
    bool Tax1Applies,
    bool Tax2Applies,
    decimal RegularPrice,
    decimal LastCost,
    decimal AvgCost,
    decimal GrossMarginPct,
    int BaseStock,
    int ReorderPoint,
    int ReorderQty,
    decimal OnHand,
    decimal OnOrder,
    decimal CaseQty,
    decimal ShipWeight,
    string? BinLocation,
    string? PosMessage,
    string? InvoiceMessage,
    string? Notes,
    long? DepartmentId,
    string? DepartmentName,
    long? CategoryId,
    string? CategoryName,
    LinkedProductDto? Substitute,
    LinkedProductDto? TagAlong,
    LinkedProductDto? Parent,
    IReadOnlyList<ProductPriceDto> Levels,
    IReadOnlyList<PriceBreakDto> Breaks,
    SalePricingDto? Sale,
    BonusPricingDto? Bonus,
    IReadOnlyList<ProductSupplierDto> Suppliers,
    IReadOnlyList<KitComponentDto> KitComponents,
    bool HasImage,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt);

public sealed record KitComponentDto(long ComponentProductId, string StockCode, string Name, decimal Quantity);

/// <summary>Reference rows for the department and category pickers, and for the browse filter bar.</summary>
public sealed record ReferenceRowDto(long Id, string Name, string? Code, int SortOrder, bool IsActive, int UsageCount);
