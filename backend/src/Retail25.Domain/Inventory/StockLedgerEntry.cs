using Retail25.Domain.Common;

namespace Retail25.Domain.Inventory;

public enum MovementType
{
    Sale = 0,
    ReturnIn = 1,
    TradeInIn = 2,
    Receipt = 3,
    TransferOut = 4,
    TransferIn = 5,
    Adjustment = 6,
    CountVariance = 7,
    KitExplode = 8,
    CaseBreak = 9,
    YearEnd = 10,
}

/// <summary>
/// Append-only stock movement ledger. Every change to stock quantity produces a ledger entry.
/// StockLevel.OnHand is derived and rebuildable by replaying this ledger.
/// </summary>
public sealed class StockLedgerEntry : Entity
{
    private StockLedgerEntry()
    {
    }

    public Guid ProductId { get; set; }

    public Guid? VariantId { get; set; }

    public Guid LocationId { get; set; }

    public MovementType MovementType { get; set; }

    /// <summary>Signed quantity: negative for sales/transfer-out, positive for receipts/returns.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Unit cost at the time of this movement (for COGS and avg cost calculation).</summary>
    public decimal UnitCost { get; set; }

    /// <summary>Polymorphic reference: the type of the entity that caused this movement.</summary>
    public string? ReferenceType { get; set; }

    /// <summary>Id of the referencing entity (e.g. SalesTransaction.Id, PurchaseOrder.Id).</summary>
    public Guid? ReferenceId { get; set; }

    public string? Reason { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public Guid? StaffId { get; set; }

    /// <summary>
    /// Records a movement. The sign of <paramref name="quantity"/> carries the direction — negative
    /// leaves the building — so replaying the ledger is a plain sum and needs no knowledge of
    /// movement semantics.
    /// </summary>
    public static StockLedgerEntry Create(
        Guid productId,
        Guid locationId,
        MovementType movementType,
        decimal quantity,
        decimal unitCost,
        DateTimeOffset occurredAt,
        Guid? variantId = null,
        string? referenceType = null,
        Guid? referenceId = null,
        string? reason = null,
        Guid? staffId = null) => new()
        {
            ProductId = productId,
            LocationId = locationId,
            MovementType = movementType,
            Quantity = quantity,
            UnitCost = unitCost,
            OccurredAt = occurredAt,
            VariantId = variantId,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Reason = reason,
            StaffId = staffId,
        };
}
