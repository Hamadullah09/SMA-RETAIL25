namespace RetailPlus.Core.Model;

public enum PurchaseOrderStatus
{
    Draft,
    Posted,
    Received,
}

/// <summary>The "How many to order" choices of File / New on the Purchase Order window.</summary>
public enum OrderMethod
{
    Blank,
    OneWeek,
    TwoWeeks,
    ReorderPoints,
    MonthlySales,
}

/// <summary>A purchase order (the original kept one DBF file per PO: PO&lt;number&gt;.&lt;LOC&gt;).</summary>
public class PurchaseOrder : IVersioned
{
    public int Id { get; set; }
    public long Number { get; set; }
    public int LocationId { get; set; }
    public Location? Location { get; set; }
    public DateTime CreatedAt { get; set; }
    public PurchaseOrderStatus Status { get; set; }
    public OrderMethod Method { get; set; }
    public string? Description { get; set; }
    public DateTime? PostedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<PurchaseOrderLine> Lines { get; set; } = [];
}

/// <summary>One item on a purchase order (one record of a PO file).</summary>
public class PurchaseOrderLine : IFlaggable
{
    public int Id { get; set; }
    public int PurchaseOrderId { get; set; }
    public int? ItemId { get; set; }
    public string Plu { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Size { get; set; }
    /// <summary>Items per case [PKG]; Order Qty counts cases when this is above 1.</summary>
    public decimal CaseQuantity { get; set; } = 1;
    /// <summary>[ORDQTY]</summary>
    public decimal OrderQuantity { get; set; }
    /// <summary>[LASTCOST]</summary>
    public decimal CostEach { get; set; }
    /// <summary>Order Qty × Case Qty × Cost Each [ORDERCOST].</summary>
    public decimal OrderCost { get; set; }
    public decimal OnHandAtOrder { get; set; }
    public decimal OnOrderAtOrder { get; set; }
    public decimal BaseStock { get; set; }
    public decimal ReorderPoint { get; set; }
    public decimal ReorderQuantity { get; set; }
    public decimal BackOrders { get; set; }
    /// <summary>Pieces received, not cases [RECVD].</summary>
    public decimal QuantityReceived { get; set; }
    public int? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    /// <summary>The supplier's stock code [REORDERNO].</summary>
    public string? ReorderNumber { get; set; }
    public bool IsFlagged { get; set; }
}
