using Retail25.Domain.Common;
using Retail25.Domain.Configuration;
using Retail25.Domain.ValueObjects;

namespace Retail25.Domain.Catalog;

/// <summary>
/// The central catalog entity. Every field from the legacy inventory screen (guide p.30–44) is
/// present. Product type drives behaviour via configuration rows, not compiled-in logic.
/// </summary>
public sealed class Product : AggregateRoot, IAuditable, ISoftDeletable
{
    public static readonly Error StockCodeRequired = new("product.stock_code_required", "A stock code is required.");
    public static readonly Error NameRequired = new("product.name_required", "A product name is required.");
    public static readonly Error DuplicateStockCode = new("product.duplicate_stock_code", "A product with this stock code already exists at this location.");

    private Product()
    {
    }

    public Guid LocationId { get; private set; }

    /// <summary>Unique per location, legacy 5-digit code (guide p.31).</summary>
    public string StockCode { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public ProductType Type { get; private set; } = ProductType.Standard;

    /// <summary>Universal Product Code for barcode scanning (guide p.31).</summary>
    public string? Upc { get; private set; }

    // --- Tax flags (guide p.31) ---

    public bool Tax1Applies { get; private set; } = true;

    public bool Tax2Applies { get; private set; } = true;

    // --- Pricing (guide p.31–35) ---

    /// <summary>The default selling price (guide p.32). Also the unit price for random-weight items.</summary>
    public decimal RegularPrice { get; private set; }

    /// <summary>Most recent purchase cost (guide p.31, 3 decimal precision per p.37).</summary>
    public decimal LastCost { get; private set; }

    /// <summary>Moving-average cost, maintained by the stock ledger (guide p.31).</summary>
    public decimal AvgCost { get; private set; }

    /// <summary>((price - cost) / price) * 100 — stored generated column (guide p.32).</summary>
    public decimal GrossMarginPct { get; private set; }

    // --- Stock control (guide p.31, p.36–37) ---

    public int BaseStock { get; private set; }

    public int ReorderPoint { get; private set; }

    public int ReorderQty { get; private set; }

    /// <summary>Items in stock at this location. Derived from the stock ledger; updated in the same transaction.</summary>
    public decimal OnHand { get; private set; }

    public decimal OnOrder { get; private set; }

    /// <summary>Quantity per case for break-case ordering (guide p.43).</summary>
    public decimal CaseQty { get; private set; }

    public decimal ShipWeight { get; private set; }

    public string? BinLocation { get; private set; }

    // --- Display messages (guide p.43–44) ---

    /// <summary>Shown on POS screen when item is scanned (guide p.43).</summary>
    public string? PosMessage { get; private set; }

    /// <summary>Printed on invoice (guide p.44).</summary>
    public string? InvoiceMessage { get; private set; }

    /// <summary>Notes shown in product info panel (guide p.38).</summary>
    public string? Notes { get; private set; }

    // --- Relationships ---

    public Guid? DepartmentId { get; private set; }

    public Guid? CategoryId { get; private set; }

    /// <summary>Substitute item if this one is out of stock (guide p.42).</summary>
    public Guid? SubstituteProductId { get; private set; }

    /// <summary>Tag-along item added automatically when this item is sold (guide p.42).</summary>
    public Guid? TagAlongProductId { get; private set; }

    /// <summary>Parent item for case-break (guide p.43). If set, this is the individual unit.</summary>
    public Guid? ParentProductId { get; private set; }

    // --- Audit ---

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    public static Result<Product> Create(
        Guid locationId,
        string stockCode,
        string name,
        ProductType type,
        decimal regularPrice,
        bool tax1Applies = true,
        bool tax2Applies = true)
    {
        if (string.IsNullOrWhiteSpace(stockCode))
            return Result.Failure<Product>(StockCodeRequired);

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Product>(NameRequired);

        var product = new Product
        {
            LocationId = locationId,
            StockCode = stockCode.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Type = type,
            RegularPrice = regularPrice,
            Tax1Applies = tax1Applies,
            Tax2Applies = tax2Applies,
        };

        // Gift cards are never taxable (guide p.106).
        if (type == ProductType.GiftCard)
        {
            product.Tax1Applies = false;
            product.Tax2Applies = false;
        }

        return Result.Success(product);
    }

    public void UpdatePricing(decimal regularPrice, decimal lastCost, decimal avgCost)
    {
        RegularPrice = regularPrice;
        LastCost = lastCost;
        AvgCost = avgCost;
        RecalculateMargin();
    }

    public void UpdateStockLevels(decimal onHand, decimal onOrder)
    {
        OnHand = onHand;
        OnOrder = onOrder;
    }

    public void UpdateDetails(string name, string? description, string? upc, string? binLocation, string? notes)
    {
        Name = name.Trim();
        Description = description;
        Upc = upc?.Trim();
        BinLocation = binLocation?.Trim();
        Notes = notes;
    }

    public void UpdateMessages(string? posMessage, string? invoiceMessage)
    {
        PosMessage = posMessage;
        InvoiceMessage = invoiceMessage;
    }

    public void UpdateOrdering(int baseStock, int reorderPoint, int reorderQty, decimal caseQty, decimal shipWeight)
    {
        BaseStock = baseStock;
        ReorderPoint = reorderPoint;
        ReorderQty = reorderQty;
        CaseQty = caseQty;
        ShipWeight = shipWeight;
    }

    public void SetLinks(Guid? substituteId, Guid? tagAlongId, Guid? parentId)
    {
        SubstituteProductId = substituteId;
        TagAlongProductId = tagAlongId;
        ParentProductId = parentId;
    }

    public void SetDepartment(Guid? departmentId) => DepartmentId = departmentId;

    public void SetCategory(Guid? categoryId) => CategoryId = categoryId;

    /// <summary>
    /// Renames the stock code. Uniqueness per location is a database constraint and is checked by
    /// the handler before this is called; the entity only normalises.
    /// </summary>
    public Result SetStockCode(string stockCode)
    {
        if (string.IsNullOrWhiteSpace(stockCode))
        {
            return Result.Failure(StockCodeRequired);
        }

        StockCode = stockCode.Trim().ToUpperInvariant();
        return Result.Success();
    }

    /// <summary>
    /// Sets the two taxability flags (guide p.31). A gift card is never taxable no matter what is
    /// asked for — the tax is charged when the card is spent, and charging it twice is a refundable
    /// error the store only discovers at reconciliation.
    /// </summary>
    public void SetTaxFlags(bool tax1Applies, bool tax2Applies)
    {
        if (Type == ProductType.GiftCard)
        {
            Tax1Applies = false;
            Tax2Applies = false;
            return;
        }

        Tax1Applies = tax1Applies;
        Tax2Applies = tax2Applies;
    }

    public void SetType(ProductType type)
    {
        Type = type;
        if (type == ProductType.GiftCard)
        {
            Tax1Applies = false;
            Tax2Applies = false;
        }
    }

    /// <summary>
    /// Moving-average cost update when stock is received (guide p.68):
    /// newAvg = (onHand*oldAvg + qtyRecvd*costEach + freight) / (onHand + qtyRecvd)
    /// </summary>
    public void RecalculateAvgCost(decimal quantityReceived, decimal unitCost, decimal allocatedFreight)
    {
        var totalCost = (OnHand * AvgCost) + (quantityReceived * unitCost) + allocatedFreight;
        var totalQty = OnHand + quantityReceived;
        AvgCost = totalQty > 0 ? decimal.Round(totalCost / totalQty, 3, MidpointRounding.AwayFromZero) : 0m;
        RecalculateMargin();
    }

    private void RecalculateMargin()
    {
        GrossMarginPct = RegularPrice > 0
            ? decimal.Round((RegularPrice - AvgCost) / RegularPrice * 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;
    }
}
