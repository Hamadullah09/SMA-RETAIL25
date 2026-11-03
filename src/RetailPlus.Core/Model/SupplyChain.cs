namespace RetailPlus.Core.Model;

/// <summary>
/// A batch (lot) of an item that tracks batches (<see cref="Item.TracksBatches"/>): its number, when it was made and when it
/// expires, and the supplier it came from. A batch has no stock column: its stock is the sum of the stock movements that
/// name it (<see cref="StockMovement.BatchId"/>), as an item's is the sum of all its movements. Batches are made where the
/// goods are received (a till or the web) and belong to the item's store.
/// </summary>
public class ItemBatch : Entity, IStoreScoped
{
    public Guid StoreId { get; set; }
    public Guid ItemId { get; set; }
    public Item? Item { get; set; }
    /// <summary>The batch or lot number printed on the goods; unique per item.</summary>
    public string BatchNumber { get; set; } = "";
    /// <summary>The last day the goods may be sold; none for goods that do not expire.</summary>
    public DateOnly? ExpiresOn { get; set; }
    /// <summary>When the goods were made (manufacture or packing date).</summary>
    public DateOnly? MadeOn { get; set; }
    public Guid? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    /// <summary>The day the batch was first received.</summary>
    public DateOnly? ReceivedOn { get; set; }
    /// <summary>The purchase order it first arrived on.</summary>
    public long? PurchaseOrderNumber { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// A bin (shelf, rack, bay) of a location, where items are kept. Each item names its default bin by code in
/// <see cref="Item.BinLocation"/>; put-away lists, pick lists and count sheets are sorted by zone and bin. Stock is kept
/// per item, not per bin.
/// </summary>
public class Bin : Entity, IStoreScoped
{
    public Guid StoreId { get; set; }
    public Guid LocationId { get; set; }
    public Location? Location { get; set; }
    /// <summary>The bin's code, as written on the shelf (A-01-03); unique per location.</summary>
    public string Code { get; set; } = "";
    /// <summary>The area the bin is in (back room, cold room, aisle 3); lists are sorted by zone first.</summary>
    public string? Zone { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// The steps of a purchase requisition: made (Draft), sent for approval (Submitted), then Approved or Rejected, and once
/// its approved lines are on purchase orders, Ordered. Stored as names.
/// </summary>
public enum RequisitionStatus
{
    Draft,
    Submitted,
    Approved,
    Rejected,
    Ordered,
}

/// <summary>
/// A request to buy, made by staff of a store before anything is ordered: lines of items with the quantity, when they are
/// needed and a suggested supplier. Someone with <c>purchasing.approve</c> (within their role's amount limit) approves it;
/// its approved lines then become purchase orders, one per supplier.
/// </summary>
public class PurchaseRequisition : Entity, IStationStamped, IVersioned
{
    public Guid StoreId { get; set; }
    /// <summary>The station it was made at (0 on the web).</summary>
    public int StationNumber { get; set; }
    public long Number { get; set; }
    /// <summary>The location the goods are for.</summary>
    public Guid LocationId { get; set; }
    public Location? Location { get; set; }
    public RequisitionStatus Status { get; set; }
    /// <summary>Store local time.</summary>
    public DateTime CreatedAt { get; set; }
    public string? RequestedBy { get; set; }
    public Guid? RequestedById { get; set; }
    /// <summary>When the goods are needed, for the whole requisition (lines may say otherwise).</summary>
    public DateOnly? NeededBy { get; set; }
    public string? Note { get; set; }
    public DateTime? SubmittedAt { get; set; }
    /// <summary>Who approved or rejected it, and when.</summary>
    public DateTime? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
    public Guid? DecidedById { get; set; }
    /// <summary>Why it was rejected.</summary>
    public string? DecisionNote { get; set; }
    public DateTime? OrderedAt { get; set; }
    public Guid Version { get; set; }
    public List<PurchaseRequisitionLine> Lines { get; set; } = [];
}

/// <summary>One item asked for on a requisition, and the purchase order line it went on.</summary>
public class PurchaseRequisitionLine : Entity
{
    public Guid PurchaseRequisitionId { get; set; }
    public int LineNumber { get; set; }
    public Guid? ItemId { get; set; }
    public string Plu { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Pieces asked for.</summary>
    public decimal Quantity { get; set; }
    /// <summary>The expected cost of one piece (the item's last cost when the line was made), for the approval limit.</summary>
    public decimal CostEach { get; set; }
    public DateOnly? NeededBy { get; set; }
    /// <summary>The supplier suggested; the purchase order goes to it.</summary>
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? Note { get; set; }
    /// <summary>The purchase order (and its line) the approved line was put on.</summary>
    public Guid? PurchaseOrderId { get; set; }
    public Guid? PurchaseOrderLineId { get; set; }
}

/// <summary>
/// How much a role may approve on one purchase requisition. A role with <c>purchasing.approve</c> and no limit approves
/// any amount. Set for every store at once, on the server.
/// </summary>
public class PurchaseApprovalLimit : Entity
{
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
    /// <summary>The largest requisition total (in the store's currency) the role may approve.</summary>
    public decimal MaxAmount { get; set; }
}

/// <summary>
/// A store's supply chain settings: selling expired batches, the three-way match tolerances, and the inputs of the reorder
/// suggestions. One row per store; a store without one uses the defaults below. Kept on the server and sent to the
/// store's tills.
/// </summary>
public class SupplySettings : Entity, IStoreScoped
{
    public Guid StoreId { get; set; }
    /// <summary>The POS refuses to sell what is left of an expired batch.</summary>
    public bool BlockExpiredSales { get; set; } = true;
    /// <summary>The days ahead the expiry report looks by default.</summary>
    public int ExpiryWarningDays { get; set; } = 30;

    // Three-way match: a bill line is held when it differs from the order and the receipt by more than both the percent
    // and the amount (each difference may be as large as the larger of the two).
    public decimal QuantityTolerancePercent { get; set; }
    /// <summary>Pieces billed above what was received that still pass.</summary>
    public decimal QuantityToleranceUnits { get; set; }
    public decimal PriceTolerancePercent { get; set; } = 2;
    /// <summary>Money per piece the billed price may differ from the order's.</summary>
    public decimal PriceToleranceAmount { get; set; }

    // Reorder suggestions
    /// <summary>The days of sales the average daily sales are taken over (30, 90).</summary>
    public int SalesWindowDays { get; set; } = 30;
    /// <summary>Extra days of sales kept in stock against late deliveries and busy days.</summary>
    public int SafetyStockDays { get; set; } = 7;
    /// <summary>The days of sales one order should cover once it arrives.</summary>
    public int OrderCoverDays { get; set; } = 14;
    /// <summary>The lead time of suppliers that have none of their own.</summary>
    public int DefaultLeadTimeDays { get; set; } = 7;

    /// <summary>Someone else than the person who asked must approve a requisition.</summary>
    public bool ApproverMustDiffer { get; set; }
}

/// <summary>The outcome of checking a supplier bill line against its purchase order line and what was received.</summary>
public enum BillMatchStatus
{
    /// <summary>Within the tolerances: accepted by itself.</summary>
    Matched,
    /// <summary>Outside a tolerance: waits for someone with <c>purchasing.approve</c>.</summary>
    Held,
    /// <summary>Held, then accepted by a person.</summary>
    Accepted,
    /// <summary>Held, then refused: the supplier should send a corrected bill.</summary>
    Rejected,
}

/// <summary>
/// One supplier bill line checked against its purchase order line (three-way match: ordered, received, billed). The bill
/// itself belongs to the payables module; this row keeps only what the match found and who decided. Server only.
/// </summary>
public class BillMatch : Entity, ILocalOnly, IVersioned
{
    public Guid StoreId { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public Guid PurchaseOrderLineId { get; set; }
    public Guid BillId { get; set; }
    public Guid BillLineId { get; set; }
    public string? BillNumber { get; set; }
    public DateOnly BillDate { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? ItemId { get; set; }
    public string Plu { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Pieces ordered and received on the order line, and billed on this bill line, when last checked.</summary>
    public decimal QuantityOrdered { get; set; }
    public decimal QuantityReceived { get; set; }
    public decimal QuantityBilled { get; set; }
    /// <summary>Pieces of this bill line covered by what was received (the rest is billed but not received).</summary>
    public decimal QuantityMatched { get; set; }
    /// <summary>The cost each of the order, of the receipt (what stock was valued at), and of the bill.</summary>
    public decimal OrderedCost { get; set; }
    public decimal ReceivedCost { get; set; }
    public decimal BilledCost { get; set; }
    public BillMatchStatus Status { get; set; }
    /// <summary>Why the line was held, in words for the person deciding.</summary>
    public string? Reason { get; set; }
    public DateTime CheckedAtUtc { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecidedBy { get; set; }
    public Guid? DecidedById { get; set; }
    public string? DecisionNote { get; set; }
    public Guid Version { get; set; }
}

/// <summary>
/// The price difference posted for one purchase order line: what its accepted bill lines cost more (or less) than the
/// receipt that valued the stock, split between the stock still on hand and the cost of goods already sold. Posted to the
/// ledger with the source <c>match:{purchaseOrderLineId}</c>. Server only.
/// </summary>
public class PriceVariance : Entity, ILocalOnly
{
    public Guid StoreId { get; set; }
    public Guid PurchaseOrderLineId { get; set; }
    public Guid? ItemId { get; set; }
    /// <summary>Pieces the difference was worked out on (billed and received).</summary>
    public decimal Quantity { get; set; }
    /// <summary>The whole difference: billed less received value (negative when the bill was cheaper).</summary>
    public decimal Difference { get; set; }
    /// <summary>The part that went to the items' cost (stock still on hand), and the part that went to cost of goods.</summary>
    public decimal ToInventory { get; set; }
    public decimal ToCostOfGoods { get; set; }
    public Guid? JournalEntryId { get; set; }
    public DateTime PostedAtUtc { get; set; }
}

public enum LandedCostStatus
{
    Draft,
    Posted,
}

/// <summary>How a landed cost voucher spreads its charges over the received lines.</summary>
public enum LandedCostBasis
{
    /// <summary>By each line's value (pieces times cost).</summary>
    Value,
    /// <summary>By pieces.</summary>
    Quantity,
    /// <summary>By each line's weight (pieces times the item's weight).</summary>
    Weight,
}

public enum LandedCostChargeKind
{
    Freight,
    Duty,
    Insurance,
    Other,
}

/// <summary>
/// Freight, duty, insurance and other charges paid for goods already received on one or more purchase orders, spread over
/// the received lines. Posting raises the average cost of what is still on hand and puts the rest in cost of goods
/// (Dr Inventory and Cost of goods, Cr Landed costs to allocate); the carrier's or customs' bill credits payables and
/// debits that clearing account. Server only.
/// </summary>
public class LandedCostVoucher : Entity, ILocalOnly, IVersioned
{
    public Guid StoreId { get; set; }
    public long Number { get; set; }
    public DateOnly Date { get; set; }
    public LandedCostBasis Basis { get; set; }
    public LandedCostStatus Status { get; set; }
    /// <summary>The carrier's or agent's bill number, for people to find it.</summary>
    public string? Reference { get; set; }
    public string? Note { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? PostedAtUtc { get; set; }
    public string? PostedBy { get; set; }
    public Guid? JournalEntryId { get; set; }
    public Guid Version { get; set; }
    public List<LandedCostCharge> Charges { get; set; } = [];
    public List<LandedCostReceipt> Receipts { get; set; } = [];
    public List<LandedCostAllocation> Allocations { get; set; } = [];
}

/// <summary>One charge of a landed cost voucher.</summary>
public class LandedCostCharge : Entity, ILocalOnly
{
    public Guid LandedCostVoucherId { get; set; }
    public LandedCostChargeKind Kind { get; set; }
    public string? Description { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>A received purchase order whose lines a landed cost voucher is spread over.</summary>
public class LandedCostReceipt : Entity, ILocalOnly
{
    public Guid LandedCostVoucherId { get; set; }
    public Guid PurchaseOrderId { get; set; }
}

/// <summary>
/// The share of a landed cost voucher one received line took, worked out when the voucher is posted: how much went to
/// the item's cost (the pieces still on hand) and how much to cost of goods (the pieces already gone).
/// </summary>
public class LandedCostAllocation : Entity, ILocalOnly
{
    public Guid LandedCostVoucherId { get; set; }
    public Guid PurchaseOrderLineId { get; set; }
    public Guid? ItemId { get; set; }
    public string Plu { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal Value { get; set; }
    public decimal Weight { get; set; }
    public decimal Share { get; set; }
    public decimal OnHand { get; set; }
    public decimal ToInventory { get; set; }
    public decimal ToCostOfGoods { get; set; }
}
