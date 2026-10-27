namespace RetailPlus.Core.Model;

public enum SaleStatus
{
    Completed,
    Voided,
}

/// <summary>A saved sale (the original wrote its lines to SALESnnn.DBF, one file per station).</summary>
public class Sale : Entity, IFlaggable, IStationStamped
{
    public long InvoiceNumber { get; set; }
    public Guid StoreId { get; set; }
    public int StationNumber { get; set; }
    public Guid LocationId { get; set; }
    public Location? Location { get; set; }
    /// <summary>The store's local time, as are all business times; <see cref="Entity.CreatedAtUtc"/> is UTC.</summary>
    public DateTime SoldAt { get; set; }
    public string? Staff { get; set; }
    public Guid? ClientId { get; set; }
    public Client? Client { get; set; }
    public string? ClientName { get; set; }
    public string? CustomerNumber { get; set; }
    public SaleStatus Status { get; set; }
    public bool IsOnAccount { get; set; }
    /// <summary>Sum of line totals after line discounts, before the subtotal discount and taxes.</summary>
    public decimal Subtotal { get; set; }
    public decimal SubtotalDiscount { get; set; }
    public decimal Tax1 { get; set; }
    public decimal Tax2 { get; set; }
    /// <summary>Cash rounding to the minimum tender.</summary>
    public decimal Rounding { get; set; }
    public decimal Total { get; set; }
    public decimal Cogs { get; set; }
    public string? Tax1Name { get; set; }
    public decimal Tax1Rate { get; set; }
    public string? Tax2Name { get; set; }
    public decimal Tax2Rate { get; set; }
    public bool TaxInclusive { get; set; }
    public bool Tax2Compound { get; set; }
    public decimal PointsEarned { get; set; }
    public decimal PointsRedeemed { get; set; }
    public string? Notes { get; set; }
    public DateTime? VoidedAt { get; set; }
    public string? VoidedBy { get; set; }
    public bool IsFlagged { get; set; }
    public List<SaleLine> Lines { get; set; } = [];
    public List<SalePayment> Payments { get; set; } = [];
}

public enum SaleLineKind
{
    Sale,
    Return,
    Credit,
    Coupon,
    GiftCertificate,
    BottleReturn,
    TradeIn,
    Text,
}

/// <summary>One line of a sale (one record of SALESnnn.DBF).</summary>
public class SaleLine : Entity
{
    public Guid SaleId { get; set; }
    public int LineNumber { get; set; }
    public SaleLineKind Kind { get; set; }
    public Guid? ItemId { get; set; }
    public string? Plu { get; set; }
    public string Description { get; set; } = "";
    /// <summary>Negative for returns and credits.</summary>
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LineTotal { get; set; }
    public decimal Cogs { get; set; }
    public string? Department { get; set; }
    public bool Taxable1 { get; set; }
    public bool Taxable2 { get; set; }
    public decimal Tax1 { get; set; }
    public decimal Tax2 { get; set; }
    public string? SerialNumber { get; set; }
    public Guid? VariantId { get; set; }
    public string? SupplierName { get; set; }
    public decimal Commission { get; set; }
    /// <summary>For returns: put the item back into stock.</summary>
    public bool Restock { get; set; }
    /// <summary>The gift card issued or reloaded by this line (an item of type gift card).</summary>
    public string? GiftCardNumber { get; set; }
    /// <summary>The RFID tags rung up on this line, one per piece.</summary>
    public List<SaleLineTag> Tags { get; set; } = [];
}

/// <summary>An RFID tag sold (or returned) on a sale line; a line of three tagged pieces has three.</summary>
public class SaleLineTag : Entity
{
    public Guid SaleLineId { get; set; }
    public string Epc { get; set; } = "";
}

/// <summary>A payment on a sale; split tender gives two rows.</summary>
public class SalePayment : Entity
{
    public Guid SaleId { get; set; }
    public string TenderType { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal Tendered { get; set; }
    public decimal Change { get; set; }
    public string? AuthCode { get; set; }
    /// <summary>The gift card number for a gift card payment.</summary>
    public string? Reference { get; set; }
    /// <summary>
    /// For a tender in a foreign currency: the currency, what the customer handed over in it, and the rate used
    /// (units of the currency per unit of home currency, see <see cref="FxRate"/>). The amounts above are in home
    /// currency; change is always given in home currency.
    /// </summary>
    public string? Currency { get; set; }
    public decimal ForeignAmount { get; set; }
    public decimal ExchangeRate { get; set; }
}

/// <summary>Cash drawer totals of one station between two End of Day closings (one record of TOTALnnn.DBF).</summary>
public class DrawerSession : Entity, IStationStamped
{
    public Guid StoreId { get; set; }
    public int StationNumber { get; set; }
    public Guid LocationId { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime? LastSaleAt { get; set; }
    public decimal OpeningFloat { get; set; }
    public decimal PayIns { get; set; }
    public decimal PayOuts { get; set; }
    public int CustomerCount { get; set; }
    public decimal NetSales { get; set; }
    public decimal Tax1Total { get; set; }
    public decimal Tax2Total { get; set; }
    public decimal NoTaxSales { get; set; }
    public decimal Cogs { get; set; }
    public decimal CustomerCredits { get; set; }
    public decimal BottleReturns { get; set; }
    public decimal TotalReceipts { get; set; }
    public bool IsFlagged { get; set; }
    public List<DrawerSessionTender> Tenders { get; set; } = [];
    public List<DrawerMovement> Movements { get; set; } = [];
}

public class DrawerSessionTender : Entity
{
    public Guid DrawerSessionId { get; set; }
    public string TenderType { get; set; } = "";
    public decimal Amount { get; set; }
    /// <summary>For a foreign-currency tender: the amount in that currency (the cash of that currency in the drawer).</summary>
    public decimal ForeignAmount { get; set; }
}

public enum DrawerMovementKind
{
    OpeningFloat,
    PayIn,
    PayOut,
    NoSale,
}

public class DrawerMovement : Entity
{
    public Guid DrawerSessionId { get; set; }
    public DrawerMovementKind Kind { get; set; }
    public decimal Amount { get; set; }
    public DateTime At { get; set; }
    public string? Reason { get; set; }
    public string? User { get; set; }
}

/// <summary>A sale put away with Suspend, to be recalled later (SUSPEND.DBF).</summary>
public class SuspendedSale : Entity, IStationStamped
{
    public Guid StoreId { get; set; }
    public int StationNumber { get; set; }
    public DateTime SuspendedAt { get; set; }
    public string? Label { get; set; }
    public string PayloadJson { get; set; } = "";
}

/// <summary>A customer order / back order (BACKORD.DBF).</summary>
public class CustomerOrder : Entity, IFlaggable, IStationStamped
{
    public Guid StoreId { get; set; }
    public int StationNumber { get; set; }
    public Guid LocationId { get; set; }
    public Guid? ClientId { get; set; }
    public string? Name { get; set; }
    public string? Company { get; set; }
    public string? Phone { get; set; }
    public DateOnly? OrderDate { get; set; }
    public string? SoldBy { get; set; }
    public long InvoiceNumber { get; set; }
    public string? CustomerNumber { get; set; }
    public Guid? ItemId { get; set; }
    public string? Plu { get; set; }
    public string Description { get; set; } = "";
    public string? Size { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Quantity { get; set; }
    public bool Paid { get; set; }
    public bool IsFlagged { get; set; }
}

/// <summary>A returned merchandise authorization, to or from a customer or supplier (RMA.DBF).</summary>
public class Rma : Entity, IFlaggable, IStationStamped
{
    public Guid StoreId { get; set; }
    public int StationNumber { get; set; }
    public string? Plu { get; set; }
    public string? Description { get; set; }
    public string? Size { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Company { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerRmaNumber { get; set; }
    public DateOnly? CustomerRmaDate { get; set; }
    public bool IsCustomerRma { get; set; }
    public string? Supplier { get; set; }
    public string? SupplierFirstName { get; set; }
    public string? SupplierLastName { get; set; }
    public string? SupplierPhone { get; set; }
    public string? SupplierEmail { get; set; }
    public string? SupplierRmaNumber { get; set; }
    public DateOnly? SupplierRmaDate { get; set; }
    public bool IsSupplierRma { get; set; }
    public DateOnly? SetDate { get; set; }
    public string? SetType { get; set; }
    public bool IsActive { get; set; }
    public string? Notes { get; set; }
    public bool IsFlagged { get; set; }
}

public enum TenderKind
{
    Cash,
    Card,
    Cheque,
    OnAccount,
    GiftCard,
    Other,
}

/// <summary>An accepted method of payment (Tools / Payment Options).</summary>
public class TenderType : Entity
{
    public string Name { get; set; } = "";
    public TenderKind Kind { get; set; }
    public int SortOrder { get; set; }
    public bool OpensDrawer { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>
    /// A foreign currency taken by this tender (one of the <see cref="FxRate"/> currencies); null for home currency.
    /// The Pay window converts with the rate, and change is given in home currency.
    /// </summary>
    public string? Currency { get; set; }
}

/// <summary>An out-of-state / out-of-province tax configuration (up to 12).</summary>
public class TaxConfiguration : Entity
{
    public string Name { get; set; } = "";
    public string? Tax1Name { get; set; }
    public decimal Tax1Rate { get; set; }
    public string? Tax2Name { get; set; }
    public decimal Tax2Rate { get; set; }
}

/// <summary>Foreign exchange rate: how many units of the currency one unit of local currency buys.</summary>
public class FxRate : Entity
{
    public string Currency { get; set; } = "";
    public decimal Rate { get; set; }
}

/// <summary>A programmable POS key. Panel 0 holds the 25 main keys; panel n holds the sub-keys of category key n.</summary>
public class FastKey : Entity
{
    public int Panel { get; set; }
    public int KeyNumber { get; set; }
    public string? Name { get; set; }
    public string? Plu { get; set; }
    public decimal FixedPrice { get; set; }
    public bool IsCredit { get; set; }
    public bool Taxable1 { get; set; }
    public bool Taxable2 { get; set; }
    public bool IsCategory { get; set; }
    public string? Color { get; set; }
}

/// <summary>Staff sales and commission for a period (STAFF.DBF).</summary>
public class StaffPeriod : Entity
{
    public string UserName { get; set; } = "";
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public string? FromTime { get; set; }
    public string? ToTime { get; set; }
    public decimal Sales { get; set; }
    public decimal Commission { get; set; }
    public decimal Hours { get; set; }
}

/// <summary>Why an item's on-hand count changed.</summary>
public enum StockMovementKind
{
    /// <summary>Stock the item started with: entered with a new item or brought over by the import.</summary>
    Opening,
    Sale,
    /// <summary>A customer return put back into stock.</summary>
    Return,
    /// <summary>A voided sale or return, undone.</summary>
    Void,
    /// <summary>A purchase order shipment received.</summary>
    Receipt,
    /// <summary>Adjust Stock, or matrix counts changed in the item editor.</summary>
    Adjustment,
    /// <summary>On-hand counts set from an inventory CSV file.</summary>
    Import,
    /// <summary>A posted stock count: counted minus expected.</summary>
    Count,
    /// <summary>Shipped to another location or store on a transfer.</summary>
    TransferOut,
    /// <summary>Received from another location or store on a transfer.</summary>
    TransferIn,
    /// <summary>Cases broken into units: out of the case item.</summary>
    CaseBreakOut,
    /// <summary>Cases broken into units: into the unit item.</summary>
    CaseBreakIn,
}

/// <summary>
/// One change to an item's on-hand count. Every change writes a row, so an item's stock is the sum of its movements
/// and the server can add up the movements of every till instead of trusting each till's count.
/// </summary>
public class StockMovement : Entity, IStationStamped
{
    public Guid StoreId { get; set; }
    public int StationNumber { get; set; }
    public Guid ItemId { get; set; }
    /// <summary>The matrix line, when the movement was for one.</summary>
    public Guid? VariantId { get; set; }
    public StockMovementKind Kind { get; set; }
    /// <summary>Store local time.</summary>
    public DateTime At { get; set; }
    /// <summary>Positive in, negative out.</summary>
    public decimal Quantity { get; set; }
    public decimal OnHandAfter { get; set; }
    /// <summary>
    /// The cost of one unit when the stock moved, where it is known (receipts, adjustments, counts, transfers, case
    /// breaks); 0 when it was not recorded. The accounting export values the movement with it.
    /// </summary>
    public decimal UnitCost { get; set; }
    /// <summary>The document behind the movement: an invoice or purchase order number.</summary>
    public long? DocumentNumber { get; set; }
    public string? Reason { get; set; }
    public string? User { get; set; }
}
