namespace RetailPlus.Core.Model;

/// <summary>A stock location: one inventory, identified by a three-character code (the original's location folder).</summary>
public class Location
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string? Name { get; set; }
}

/// <summary>Entry of the Department List (DEPT.DBF).</summary>
public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Entry of the Category List (CATEGORY.DBF).</summary>
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Item types from the Detail tab; stored as their names.</summary>
public enum ItemType
{
    Standard,
    Matrix,
    Serialized,
    Kit,
    GiftCard,
    NonStock,
    Rental,
    Service,
    Shipping,
    Admission,
}

/// <summary>
/// An inventory item of one location (one record of &lt;LOC&gt;INV.DBF). Source field names are given in brackets
/// where they differ from the property name.
/// </summary>
public class Item : ISoftDeletable, IFlaggable, IVersioned
{
    public int Id { get; set; }
    public int LocationId { get; set; }
    public Location? Location { get; set; }

    /// <summary>Stock code [PLU].</summary>
    public string Plu { get; set; } = "";
    /// <summary>Item name [PROD].</summary>
    public string Name { get; set; } = "";
    /// <summary>The product barcode (UPC-A, EAN-13, EAN-8), the maker's or an in-store one; see <see cref="Rules.Upc"/>.</summary>
    public string? Barcode { get; set; }
    public ItemType ItemType { get; set; }
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public string? Brand { get; set; }
    /// <summary>[BSIZE]</summary>
    public string? Size { get; set; }
    /// <summary>Items per package [PKG].</summary>
    public int PackQuantity { get; set; }
    /// <summary>Net weight (or volume) of one unit, in <see cref="WeightUnit"/>: 90 g, 1.5 L [PWEIGHT].</summary>
    public decimal Weight { get; set; }
    /// <summary>mg, g, kg, ml, L, oz or lb (see <see cref="Rules.Weights"/>).</summary>
    public string? WeightUnit { get; set; }
    /// <summary>How far a piece may weigh more or less than <see cref="Weight"/>, in <see cref="WeightToleranceUnit"/> [TOLRANCE].</summary>
    public decimal WeightTolerance { get; set; }
    /// <summary>mg or g.</summary>
    public string? WeightToleranceUnit { get; set; }
    /// <summary>Supplier shown on the item; the original stores the name [SUPPLIER].</summary>
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    /// <summary>The supplier's stock code [SUPPCODE].</summary>
    public string? SupplierCode { get; set; }

    // Taxes and handling
    /// <summary>[TX1]</summary>
    public bool Taxable1 { get; set; }
    /// <summary>[TX2]</summary>
    public bool Taxable2 { get; set; }
    /// <summary>[BINLOC]</summary>
    public string? BinLocation { get; set; }
    /// <summary>Always read the weigh scale [SCALE].</summary>
    public bool ReadScale { get; set; }
    /// <summary>[WEBDELETE]; web-shop flag, exact meaning to confirm.</summary>
    public bool WebDelete { get; set; }

    // Stock and ordering
    /// <summary>[ONHAND]</summary>
    public decimal OnHand { get; set; }
    /// <summary>[BASTOCK]</summary>
    public decimal BaseStock { get; set; }
    /// <summary>[ROP]</summary>
    public decimal ReorderPoint { get; set; }
    /// <summary>[ROQ]</summary>
    public decimal ReorderQuantity { get; set; }
    /// <summary>[ONORDER]</summary>
    public decimal OnOrder { get; set; }
    /// <summary>Date the last order was posted [ORDATE].</summary>
    public DateOnly? LastOrderDate { get; set; }
    /// <summary>Date the last shipment was posted [LASTSHIP].</summary>
    public DateOnly? LastReceivedDate { get; set; }
    /// <summary>Quantity received in the last shipment [RECVD].</summary>
    public decimal LastReceivedQuantity { get; set; }
    /// <summary>Quantity on customer orders [BACKORD].</summary>
    public decimal CustomerOrdered { get; set; }
    /// <summary>On-hand snapshot taken at year-end [B4YREND].</summary>
    public decimal OnHandBeforeYearEnd { get; set; }
    /// <summary>[LASTUPD]; maintained by the system, exact meaning (last sold / last changed) to confirm.</summary>
    public DateOnly? LastUpdated { get; set; }

    // Cost and pricing
    public decimal LastCost { get; set; }
    /// <summary>[AVGCOST]</summary>
    public decimal AverageCost { get; set; }
    /// <summary>Regular price [UNITPRICE1].</summary>
    public decimal Price1 { get; set; }
    public decimal Price2 { get; set; }
    public decimal Price3 { get; set; }
    public decimal Price4 { get; set; }
    /// <summary>Quantity at which Price2 applies [PRICEBRK1].</summary>
    public decimal BreakQuantity1 { get; set; }
    public decimal BreakQuantity2 { get; set; }
    public decimal BreakQuantity3 { get; set; }
    /// <summary>Gross margin in percent [MARGIN].</summary>
    public decimal MarginPercent { get; set; }
    /// <summary>[SALESTART]</summary>
    public DateOnly? SaleStart { get; set; }
    /// <summary>[SALEEND]</summary>
    public DateOnly? SaleEnd { get; set; }
    /// <summary>[SALEPRICE]; the manual calls it the sale discount. Amount vs percent to confirm.</summary>
    public decimal SalePrice { get; set; }
    /// <summary>Bonus pricing on, e.g. buy 2 get 1 [TWOFORONE].</summary>
    public bool BonusPricing { get; set; }
    /// <summary>[BONUSBUY]</summary>
    public int BonusBuyQuantity { get; set; }
    /// <summary>[BONUSGET]</summary>
    public int BonusGetQuantity { get; set; }

    // Special tab
    /// <summary>Substitute item's stock code [SUBCODE].</summary>
    public string? SubstituteCode { get; set; }
    /// <summary>Tag-along item's stock code [TAGALONG].</summary>
    public string? TagAlongCode { get; set; }
    /// <summary>Parent (case) item's stock code [PARENT].</summary>
    public string? ParentCode { get; set; }
    public decimal ShipWeight { get; set; }
    /// <summary>Total THC in grams [THCWEIGHT].</summary>
    public decimal ThcWeight { get; set; }
    public decimal Commission { get; set; }
    /// <summary>[MAXCOMM]</summary>
    public decimal MaxCommission { get; set; }
    /// <summary>[COMMTYPE]</summary>
    public string? CommissionType { get; set; }
    /// <summary>Photo path, relative to the photo folder [PICPATH].</summary>
    public string? PhotoPath { get; set; }
    /// <summary>Product notes / catalogue text [NOTES].</summary>
    public string? Notes { get; set; }
    /// <summary>Message on the point-of-sale screen [SALESMSG].</summary>
    public string? PosMessage { get; set; }
    /// <summary>Message on the invoice [INVOICEMSG].</summary>
    public string? InvoiceMessage { get; set; }

    public bool IsFlagged { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public List<ItemSupplier> Suppliers { get; set; } = [];
    public List<ItemMatrixColumn> MatrixColumns { get; set; } = [];
    public List<ItemVariant> Variants { get; set; } = [];
    public List<KitComponent> KitComponents { get; set; } = [];
    public List<ItemSerial> Serials { get; set; } = [];
    public List<ItemPeriodSales> PeriodSales { get; set; } = [];
}

/// <summary>One entry of the item's supplier list, in order of preference [SUPLIST memo].</summary>
public class ItemSupplier
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    /// <summary>1 = preferred supplier.</summary>
    public int Rank { get; set; }
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    /// <summary>Supplier name as stored in the source, kept even when no Supplier row matches.</summary>
    public string SupplierName { get; set; } = "";
    public string? SupplierStockCode { get; set; }
    public decimal Cost { get; set; }
    /// <summary>Fourth value of a SUPLIST entry; believed to be the case quantity.</summary>
    public decimal CaseQuantity { get; set; }
}

/// <summary>A matrix column heading, 1 to 3 per item [first segment of the MATRIX memo].</summary>
public class ItemMatrixColumn
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public int Ordinal { get; set; }
    public string Heading { get; set; } = "";
}

/// <summary>One matrix line (colour/size/style combination) with its own stock count [MATRIX memo rows].</summary>
public class ItemVariant
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public int Ordinal { get; set; }
    public string? Value1 { get; set; }
    public string? Value2 { get; set; }
    public string? Value3 { get; set; }
    /// <summary>Scannable stock code, when the first matrix column is headed CODE.</summary>
    public string? Code { get; set; }
    public decimal OnHand { get; set; }
}

/// <summary>One component of a kit [KIT memo].</summary>
public class KitComponent
{
    public int Id { get; set; }
    public int KitItemId { get; set; }
    public int Ordinal { get; set; }
    /// <summary>Component stock code as stored in the source.</summary>
    public string ComponentPlu { get; set; } = "";
    public int? ComponentItemId { get; set; }
    public Item? ComponentItem { get; set; }
    public string? Description { get; set; }
    public decimal Quantity { get; set; }
}

/// <summary>
/// The product's picture, kept in the database so every station shows it without a shared folder. The app stores a
/// JPEG of at most 800 pixels a side. (Pictures imported from Retail Plus stay a file path in <see cref="Item.PhotoPath"/>.)
/// </summary>
public class ItemPicture
{
    public int ItemId { get; set; }
    public byte[] Data { get; set; } = [];
    public DateTime UpdatedAt { get; set; }
}

/// <summary>A serial number of a serialized item [SERNO memo].</summary>
public class ItemSerial
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public string SerialNumber { get; set; } = "";
    /// <summary>For example "IN STOCK".</summary>
    public string? Status { get; set; }
    public DateOnly? StatusDate { get; set; }
}

public enum SalesPeriodKind
{
    Month = 1,
    Week = 2,
}

/// <summary>
/// Sales statistics of an item for one month or week of a year. Replaces the monthly columns, LASTYR and WEEKLY
/// of the source; year-to-date figures are computed from these rows.
/// </summary>
public class ItemPeriodSales
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public int Year { get; set; }
    public SalesPeriodKind Kind { get; set; }
    /// <summary>Month 1–12 or week 1–54.</summary>
    public int Period { get; set; }
    public decimal Units { get; set; }
    public decimal Gross { get; set; }
}
