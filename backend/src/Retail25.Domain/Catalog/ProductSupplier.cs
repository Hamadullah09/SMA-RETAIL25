using Retail25.Domain.Common;

namespace Retail25.Domain.Catalog;

/// <summary>
/// Many-to-many relationship between products and suppliers with ranking and cost (guide p.36–37).
/// The highest-ranked supplier (lowest Rank number) is preferred for automatic PO generation.
/// </summary>
public sealed class ProductSupplier : Entity, IAuditable
{
    private ProductSupplier()
    {
    }

    public Guid ProductId { get; private set; }

    public Guid SupplierId { get; private set; }

    /// <summary>1 = preferred. Used for automatic PO generation (guide p.64).</summary>
    public int Rank { get; private set; }

    /// <summary>Cost from this supplier for this product, 3 decimal precision (guide p.37).</summary>
    public decimal Cost { get; private set; }

    /// <summary>The supplier's own stock code (guide p.36).</summary>
    public string? ReorderNumber { get; private set; }

    /// <summary>Case quantity for this supplier's packaging.</summary>
    public decimal CaseQty { get; private set; }

    /// <summary>Minimum order quantity for this supplier.</summary>
    public decimal MinimumOrderQty { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    public static Result<ProductSupplier> Create(Guid productId, Guid supplierId, int rank, decimal cost, string? reorderNumber = null)
    {
        if (rank < 1)
            return Result.Failure<ProductSupplier>(new Error("product_supplier.rank_invalid", "Supplier rank must be at least 1."));

        return Result.Success(new ProductSupplier
        {
            ProductId = productId,
            SupplierId = supplierId,
            Rank = rank,
            Cost = cost,
            ReorderNumber = reorderNumber?.Trim(),
        });
    }

    public void Update(int rank, decimal cost, string? reorderNumber, decimal caseQty, decimal minimumOrderQty)
    {
        Rank = rank;
        Cost = cost;
        ReorderNumber = reorderNumber?.Trim();
        CaseQty = caseQty;
        MinimumOrderQty = minimumOrderQty;
    }

    public void Promote() => Rank = Math.Max(1, Rank - 1);

    public void Demote() => Rank += 1;
}
