using Retail25.Domain.Common;

namespace Retail25.Domain.Orders;

public enum CustomerOrderStatus
{
    Open = 0,
    PartiallyFilled = 1,
    Filled = 2,
    Cancelled = 3,
}

/// <summary>
/// A customer order / back order (guide p.16) — an item the store did not have, logged so it can be
/// filled the moment stock allows. Each line reserves its quantity against
/// <see cref="Inventory.StockLevel.Committed"/> the instant it is placed, so the same stock cannot be
/// promised to two customers.
/// </summary>
public sealed class CustomerOrder : AggregateRoot, IAuditable
{
    public CustomerOrder()
    {
    }

    public long OrderNumber { get; set; }

    public Guid CustomerId { get; set; }

    public Guid LocationId { get; set; }

    public CustomerOrderStatus Status { get; set; } = CustomerOrderStatus.Open;

    public DateOnly OrderedOn { get; set; }

    public string? Notes { get; set; }

    public Guid StaffId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}

/// <summary>One requested item on a customer order. Price is snapshotted at order time (guide convention).</summary>
public sealed class CustomerOrderLine : Entity, IAuditable
{
    public CustomerOrderLine()
    {
    }

    public Guid CustomerOrderId { get; set; }

    public Guid ProductId { get; set; }

    public Guid? VariantId { get; set; }

    public decimal OrderedQty { get; set; }

    public decimal FilledQty { get; set; }

    public decimal UnitPrice { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}
