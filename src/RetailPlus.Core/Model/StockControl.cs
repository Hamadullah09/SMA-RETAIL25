using System.Linq.Expressions;

namespace RetailPlus.Core.Model;

public enum StockCountStatus
{
    Open,
    Posted,
    Cancelled,
}

/// <summary>
/// A stock count (stock take) of one location: items are counted by scanning or typing their codes, by reading their
/// RFID tags or from a CSV file, the differences are reviewed, and posting makes each counted item's on-hand count the
/// counted quantity, with one <see cref="StockMovementKind.Count"/> movement per item that changed.
/// </summary>
public class StockCount : Entity, IStationStamped
{
    public Guid StoreId { get; set; }
    /// <summary>The station the count was started at.</summary>
    public int StationNumber { get; set; }
    public long Number { get; set; }
    public Guid LocationId { get; set; }
    public Location? Location { get; set; }
    public StockCountStatus Status { get; set; }
    /// <summary>Store local time, as are the other business times.</summary>
    public DateTime StartedAt { get; set; }
    public string? StartedBy { get; set; }
    public DateTime? PostedAt { get; set; }
    public string? PostedBy { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }
    /// <summary>Posting set the items that were not counted to zero (otherwise they were left alone).</summary>
    public bool UncountedSetToZero { get; set; }
    public string? Note { get; set; }
    public List<StockCountLine> Lines { get; set; } = [];
    /// <summary>
    /// A count of one zone, or of one bin (zone and bin code), instead of the whole location: the count sheet lists the
    /// items kept there, and "set the items not counted to zero" reaches only them.
    /// </summary>
    public string? Zone { get; set; }
    public string? BinCode { get; set; }
}

/// <summary>
/// One counted item (or matrix line) of a stock count. <see cref="Expected"/> is the on-hand count when the item was
/// first counted, so sales made while the count goes on do not show as a difference: posting adds the difference
/// (counted minus expected) to the item's stock.
/// </summary>
public class StockCountLine : Entity
{
    public Guid StockCountId { get; set; }
    public Guid ItemId { get; set; }
    public Guid? VariantId { get; set; }
    /// <summary>Stock code and description when counted, for the count sheet and the variance report.</summary>
    public string Plu { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>On hand when the item was first counted (or when posting, for items set to zero).</summary>
    public decimal Expected { get; set; }
    public decimal Counted { get; set; }
    /// <summary>Counted minus expected.</summary>
    public decimal Variance { get; set; }
    /// <summary>Average cost (last cost when none) when the item was first counted.</summary>
    public decimal UnitCost { get; set; }
    /// <summary>Variance times unit cost: the value lost (negative) or found.</summary>
    public decimal VarianceCost { get; set; }
    public DateTime CountedAt { get; set; }
    /// <summary>Added by posting with "set the items not counted to zero".</summary>
    public bool NotCounted { get; set; }
    /// <summary>The batch counted, for an item that tracks batches (each batch is a line of its own).</summary>
    public Guid? BatchId { get; set; }
    public string? BatchNumber { get; set; }
}

/// <summary>An RFID tag read during a stock count, so the same piece is never counted twice.</summary>
public class StockCountTag : Entity
{
    public Guid StockCountId { get; set; }
    public Guid StockCountLineId { get; set; }
    public string Epc { get; set; } = "";
}

public enum StockTransferStatus
{
    Draft,
    /// <summary>Taken out of the sending location's stock; in transit until received.</summary>
    Shipped,
    Received,
    Cancelled,
}

/// <summary>
/// Goods sent from one location to another: two locations of one store, or a location of another store. Shipping takes
/// the quantities out of the sending location (<see cref="StockMovementKind.TransferOut"/>); receiving, on whichever
/// database has the transfer, puts what arrived into the receiving location's item with the same stock code
/// (<see cref="StockMovementKind.TransferIn"/>), creating that item when the location does not have it yet.
/// </summary>
public class StockTransfer : Entity, IStationStamped
{
    /// <summary>The sending store.</summary>
    public Guid StoreId { get; set; }
    /// <summary>The station the transfer was made at.</summary>
    public int StationNumber { get; set; }
    /// <summary>The receiving store (the same as <see cref="StoreId"/> for a move between two locations of one store).</summary>
    public Guid ToStoreId { get; set; }
    public long Number { get; set; }
    public Guid FromLocationId { get; set; }
    public Location? FromLocation { get; set; }
    public Guid ToLocationId { get; set; }
    public Location? ToLocation { get; set; }
    public StockTransferStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ShippedAt { get; set; }
    public string? ShippedBy { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public string? ReceivedBy { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? Note { get; set; }
    public List<StockTransferLine> Lines { get; set; } = [];

    /// <summary>
    /// The rule for who has a transfer: <b>a transfer belongs to both the sending and the receiving store</b>. The sending
    /// store makes and ships it; the receiving store must see it to receive it. Synchronisation sends a transfer (with its
    /// lines) to the tills of both stores, and store filters on the server use <see cref="OfStore"/>.
    /// </summary>
    public static bool BelongsTo(StockTransfer transfer, Guid storeId) => transfer.StoreId == storeId || transfer.ToStoreId == storeId;

    /// <summary><see cref="BelongsTo"/> as a query filter: the transfers a store sends or receives.</summary>
    public static Expression<Func<StockTransfer, bool>> OfStore(Guid storeId) => t => t.StoreId == storeId || t.ToStoreId == storeId;
}

/// <summary>
/// One item of a transfer, by stock code. The sending item's details are copied onto the line, because the receiving
/// store's database may not have the sending store's items: they are what a missing item is created from.
/// </summary>
public class StockTransferLine : Entity
{
    public Guid StockTransferId { get; set; }
    public int LineNumber { get; set; }
    /// <summary>The item at the sending location (no foreign key: the receiving database may not have it).</summary>
    public Guid SourceItemId { get; set; }
    public string Plu { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Barcode { get; set; }
    public string? Size { get; set; }
    public string? Department { get; set; }
    public ItemType ItemType { get; set; }
    public bool Taxable1 { get; set; }
    public bool Taxable2 { get; set; }
    public decimal UnitCost { get; set; }
    public decimal Price { get; set; }
    public decimal QuantityShipped { get; set; }
    /// <summary>What arrived; below <see cref="QuantityShipped"/> for a short receipt, above it for an over receipt.</summary>
    public decimal QuantityReceived { get; set; }
    /// <summary>The receiving location's item the goods went into.</summary>
    public Guid? DestinationItemId { get; set; }
    /// <summary>The receiving location did not have the item, so receiving created it from the line.</summary>
    public bool CreatedAtDestination { get; set; }
    /// <summary>
    /// For an item that tracks batches, the batch sent (each batch is a line of its own): its id at the sending location,
    /// and what the receiving location makes its own batch from (the same number, expiry and made-on date).
    /// </summary>
    public Guid? SourceBatchId { get; set; }
    public string? BatchNumber { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public DateOnly? MadeOn { get; set; }
}
