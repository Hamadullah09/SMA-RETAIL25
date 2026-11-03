namespace RetailPlus.Core.Model;

// Manufacturing and production (ERP phase E5). Bills of materials, routings and work centres are made on the server and
// go down to the tills; production orders belong to their store and travel both ways, because the shop floor starts and
// finishes their operations; the floor's reports go up. Planning (MRP) and quality are kept on the server only.

/// <summary>A place where work is done: a machine, a line or a team, at one location of a store.</summary>
public class WorkCentre : Entity, IStoreScoped
{
    public Guid StoreId { get; set; }
    public Guid LocationId { get; set; }
    public Location? Location { get; set; }
    /// <summary>A short code, unique in the store (for example CUT, OVEN1).</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>What an hour of the people working here costs.</summary>
    public decimal LabourCostPerHour { get; set; }
    /// <summary>What an hour of the machine costs (power, wear).</summary>
    public decimal MachineCostPerHour { get; set; }
    /// <summary>Overhead absorbed for each hour worked here (rent, supervision), on top of labour and machine.</summary>
    public decimal OverheadPerHour { get; set; }
    /// <summary>The hours it can work in a day.</summary>
    public decimal CapacityHoursPerDay { get; set; } = 8;
    /// <summary>The days it works in a week, for the capacity of a week.</summary>
    public int DaysPerWeek { get; set; } = 6;
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}

/// <summary>How a component reaches the order: issued by hand (scanned or from the list), or taken by itself when the order is completed.</summary>
public enum IssueMethod
{
    Manual,
    /// <summary>Taken out of stock when the order is completed, for the quantity made.</summary>
    Backflush,
}

/// <summary>
/// A bill of materials: what goes into making a product (a stock code, the same for every location), written for a
/// <see cref="YieldQuantity"/> of it and scaled to the quantity of an order. Components are stock codes too, found at the
/// order's material location when it is released; a component may have a bill of its own (several levels). A product can
/// have several versions (<see cref="Revision"/>); the one used is the active one in force on the day, the highest
/// revision first. Its routing (<see cref="Operations"/>) says where and how long it is worked.
/// </summary>
public class BillOfMaterials : Entity
{
    /// <summary>The product made: a stock code.</summary>
    public string ProductCode { get; set; } = "";
    /// <summary>The product's name when the bill was saved, for lists.</summary>
    public string ProductName { get; set; } = "";
    /// <summary>The bill's version number (1, 2, …) for this product.</summary>
    public int Revision { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    /// <summary>How much of the product one run of the bill makes (a batch of 50 loaves, 1 chair).</summary>
    public decimal YieldQuantity { get; set; } = 1;
    /// <summary>Overhead absorbed as a percentage of the material cost, besides the work centres' hourly overhead.</summary>
    public decimal OverheadPercent { get; set; }
    public string? Notes { get; set; }
    public List<BomLine> Lines { get; set; } = [];
    public List<BomByProduct> ByProducts { get; set; } = [];
    public List<RoutingOperation> Operations { get; set; } = [];

    /// <summary>True when the bill is in force on <paramref name="day"/>.</summary>
    public bool InForce(DateOnly day) => IsActive && (EffectiveFrom is null || EffectiveFrom <= day) && (EffectiveTo is null || EffectiveTo >= day);
}

/// <summary>One component of a bill of materials.</summary>
public class BomLine : Entity
{
    public Guid BillOfMaterialsId { get; set; }
    public int LineNumber { get; set; }
    public string ComponentCode { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>How much of the component the bill's yield quantity needs, before scrap.</summary>
    public decimal Quantity { get; set; }
    /// <summary>The share of the component expected to be lost, added on top (5 = 5 % more is issued).</summary>
    public decimal ScrapPercent { get; set; }
    public IssueMethod IssueMethod { get; set; }
    /// <summary>The routing operation (its sequence) the component is used at; null for the start of the order.</summary>
    public int? OperationSequence { get; set; }
}

/// <summary>
/// Something else the bill makes besides the product (bran from flour, offcuts sold as remnants). It takes a share of the
/// order's cost; the product takes what the by-products do not.
/// </summary>
public class BomByProduct : Entity
{
    public Guid BillOfMaterialsId { get; set; }
    public int LineNumber { get; set; }
    public string StockCode { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>How much of it the bill's yield quantity makes.</summary>
    public decimal Quantity { get; set; }
    /// <summary>The percentage of the order's cost it takes.</summary>
    public decimal CostSharePercent { get; set; }
}

/// <summary>An operation of a bill's routing: where it is done, in which order, and how long it takes.</summary>
public class RoutingOperation : Entity
{
    public Guid BillOfMaterialsId { get; set; }
    /// <summary>The order of the operations (10, 20, 30…), unique in the routing.</summary>
    public int Sequence { get; set; }
    public Guid WorkCentreId { get; set; }
    public WorkCentre? WorkCentre { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Once per order, before the first unit.</summary>
    public decimal SetupMinutes { get; set; }
    /// <summary>For each unit of the product.</summary>
    public decimal RunMinutesPerUnit { get; set; }
    public string? Instructions { get; set; }
}

public enum ProductionOrderStatus
{
    /// <summary>Made by a planner or by planning; nothing reserved yet.</summary>
    Planned,
    /// <summary>The bill was copied onto the order and the materials reserved; the shop floor can work on it.</summary>
    Released,
    /// <summary>The floor has started: an operation, an issue or a report.</summary>
    InProgress,
    /// <summary>Made: backflushed and its output received. What is left is closed.</summary>
    Completed,
    /// <summary>Its remaining cost was posted as a variance; nothing changes any more.</summary>
    Closed,
    Cancelled,
}

/// <summary>
/// An order to make a quantity of a product, at one store: materials come from <see cref="MaterialLocationId"/> and the
/// output goes to <see cref="OutputLocationId"/>. Releasing it copies the bill's lines and routing onto the order (so the
/// bill can change afterwards) and its standard cost. What was issued, reported and received is never kept on the order
/// itself but in its stock movements and reports, which tills add without overwriting one another.
/// </summary>
public class ProductionOrder : Entity, IStationStamped, IVersioned
{
    public Guid StoreId { get; set; }
    /// <summary>Where the order was made (the server's 1000 for the web).</summary>
    public int StationNumber { get; set; }
    public long Number { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public Guid? BillOfMaterialsId { get; set; }
    /// <summary>The bill's version when the order was made or released.</summary>
    public int BomRevision { get; set; }
    /// <summary>How much of the product to make.</summary>
    public decimal Quantity { get; set; }
    public Guid MaterialLocationId { get; set; }
    public Location? MaterialLocation { get; set; }
    public Guid OutputLocationId { get; set; }
    public Location? OutputLocation { get; set; }
    public DateOnly? PlannedStart { get; set; }
    public DateOnly? PlannedEnd { get; set; }
    /// <summary>1 (most urgent) to 9; 5 by default.</summary>
    public int Priority { get; set; } = 5;
    public ProductionOrderStatus Status { get; set; }
    /// <summary>The customer order it is made for (made to order); null when made to stock.</summary>
    public Guid? CustomerOrderId { get; set; }
    public string? Notes { get; set; }

    /// <summary>Standard material cost of one unit of the order (before by-products take their share), from the release.</summary>
    public decimal StandardMaterialCost { get; set; }
    public decimal StandardLabourCost { get; set; }
    public decimal StandardMachineCost { get; set; }
    public decimal StandardOverheadCost { get; set; }
    /// <summary>The bill's overhead percentage of material, copied at release.</summary>
    public decimal OverheadPercent { get; set; }

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public string? ReleasedBy { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CompletedBy { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? ClosedBy { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }
    public Guid Version { get; set; }
    public List<ProductionOrderLine> Lines { get; set; } = [];
    public List<ProductionOrderOperation> Operations { get; set; } = [];

    /// <summary>Standard cost of one unit of the order, all of it.</summary>
    public decimal StandardUnitCost => StandardMaterialCost + StandardLabourCost + StandardMachineCost + StandardOverheadCost;

    /// <summary>Planned, released or in progress: the order still needs materials and work.</summary>
    public bool IsOpen => Status is ProductionOrderStatus.Planned or ProductionOrderStatus.Released or ProductionOrderStatus.InProgress;
}

public enum ProductionLineKind
{
    /// <summary>Material that goes in.</summary>
    Component,
    /// <summary>Something else that comes out, with a share of the cost.</summary>
    ByProduct,
}

/// <summary>A line of a released order: a component (from the bill, scaled to the order) or a by-product.</summary>
public class ProductionOrderLine : Entity
{
    public Guid ProductionOrderId { get; set; }
    public int LineNumber { get; set; }
    public ProductionLineKind Kind { get; set; }
    public string StockCode { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>The item at the order's material location (components) or output location (by-products); null when it has none.</summary>
    public Guid? ItemId { get; set; }
    /// <summary>For one unit of the product: scrap included for a component.</summary>
    public decimal QuantityPerUnit { get; set; }
    /// <summary>For the order's quantity.</summary>
    public decimal QuantityPlanned { get; set; }
    public decimal ScrapPercent { get; set; }
    public IssueMethod IssueMethod { get; set; }
    public int? OperationSequence { get; set; }
    /// <summary>By-products: the percentage of the order's cost they take.</summary>
    public decimal CostSharePercent { get; set; }
    /// <summary>Components: the cost of one unit at release (average cost, or the rolled-up cost of a made component).</summary>
    public decimal StandardUnitCost { get; set; }
    /// <summary>Components: how much the release could set aside from stock (the rest was short).</summary>
    public decimal QuantityReserved { get; set; }
}

public enum OperationStatus
{
    Waiting,
    Started,
    Done,
}

/// <summary>An operation of a released order, copied from the routing with the work centre's hourly rates of the day.</summary>
public class ProductionOrderOperation : Entity
{
    public Guid ProductionOrderId { get; set; }
    public int Sequence { get; set; }
    public Guid? WorkCentreId { get; set; }
    public string WorkCentreName { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal SetupMinutes { get; set; }
    public decimal RunMinutesPerUnit { get; set; }
    public string? Instructions { get; set; }
    public decimal LabourRate { get; set; }
    public decimal MachineRate { get; set; }
    public decimal OverheadRate { get; set; }
    public OperationStatus Status { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    /// <summary>The standard minutes for making <paramref name="quantity"/> units.</summary>
    public decimal StandardMinutes(decimal quantity) => SetupMinutes + RunMinutesPerUnit * quantity;
}

/// <summary>
/// What the floor reports on an operation: the time worked, the good and scrapped quantity, and who did it. The labour,
/// machine and overhead it applies to the order are worked out at the operation's rates when it is reported.
/// </summary>
public class ProductionReport : Entity, IStationStamped
{
    public Guid StoreId { get; set; }
    public int StationNumber { get; set; }
    public Guid ProductionOrderId { get; set; }
    /// <summary>The operation's sequence.</summary>
    public int OperationSequence { get; set; }
    /// <summary>Store local time.</summary>
    public DateTime At { get; set; }
    public decimal Minutes { get; set; }
    public decimal GoodQuantity { get; set; }
    public decimal ScrapQuantity { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public decimal LabourCost { get; set; }
    public decimal MachineCost { get; set; }
    public decimal OverheadCost { get; set; }
    public string? Note { get; set; }
}

// Planning ---------------------------------------------------------------------------------------------------------

/// <summary>A planning (MRP) run for one store: what it looked at and when. Its suggestions stay until firmed or replaced.</summary>
public class MrpRun : Entity, IStoreScoped, ILocalOnly
{
    public Guid StoreId { get; set; }
    /// <summary>Where firmed orders make and receive (the store's stock at every location counts).</summary>
    public Guid LocationId { get; set; }
    public DateTime RunAtUtc { get; set; }
    public string? RunBy { get; set; }
    /// <summary>Demand due within this many days is planned.</summary>
    public int HorizonDays { get; set; }
    /// <summary>The lead time of suppliers without one of their own.</summary>
    public int DefaultLeadDays { get; set; }
    public int Suggestions { get; set; }
}

public enum SuggestionKind
{
    /// <summary>Make it: a production order.</summary>
    Make,
    /// <summary>Buy it: a purchase order.</summary>
    Buy,
}

public enum SuggestionStatus
{
    Open,
    /// <summary>Turned into a production order or a purchase order.</summary>
    Firmed,
    Dismissed,
    /// <summary>A later planning run replaced it.</summary>
    Superseded,
}

/// <summary>
/// What planning (or the release of an order short of material) suggests: make or buy a quantity of a stock code by a
/// date. It changes nothing until someone firms it.
/// </summary>
public class MrpSuggestion : Entity, IStoreScoped, ILocalOnly
{
    public Guid StoreId { get; set; }
    /// <summary>The run that made it; null for the shortage of a released order.</summary>
    public Guid? RunId { get; set; }
    public SuggestionKind Kind { get; set; }
    public SuggestionStatus Status { get; set; }
    public string StockCode { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Where it is needed: the location a firmed order makes or receives at.</summary>
    public Guid LocationId { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>When it must be there.</summary>
    public DateOnly NeedBy { get; set; }
    /// <summary>When to order it (buy) or start making it (make): <see cref="NeedBy"/> less the lead time.</summary>
    public DateOnly StartBy { get; set; }
    public int LeadDays { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public Guid? BillOfMaterialsId { get; set; }
    /// <summary>The level in the bills: 0 for what is sold or ordered, 1 for its components, and so on.</summary>
    public int Level { get; set; }
    /// <summary>What needs it, in words.</summary>
    public string? Reason { get; set; }
    /// <summary>The production order or purchase order it became.</summary>
    public Guid? FirmedId { get; set; }
    public string? FirmedDocument { get; set; }
    public DateTime? FirmedAtUtc { get; set; }
    public string? FirmedBy { get; set; }
}

/// <summary>How many days a supplier takes to deliver, for planning.</summary>
public class SupplierLeadTime : Entity, ILocalOnly
{
    public Guid SupplierId { get; set; }
    public int Days { get; set; }
}

// Quality ------------------------------------------------------------------------------------------------------------

public enum QualityCheckKind
{
    /// <summary>Passed or failed.</summary>
    PassFail,
    /// <summary>A measurement between a minimum and a maximum.</summary>
    Number,
    /// <summary>A note (a colour, a batch mark); passes when filled in.</summary>
    Text,
}

/// <summary>The checks made on a product (a stock code): on what production makes, on what suppliers deliver, or both.</summary>
public class QualityPlan : Entity, ILocalOnly
{
    public string ProductCode { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool ForProduction { get; set; } = true;
    public bool ForIncoming { get; set; }
    public string? Notes { get; set; }
    public List<QualityCheck> Checks { get; set; } = [];
}

public class QualityCheck : Entity, ILocalOnly
{
    public Guid QualityPlanId { get; set; }
    public int LineNumber { get; set; }
    public string Name { get; set; } = "";
    public QualityCheckKind Kind { get; set; }
    public decimal? Minimum { get; set; }
    public decimal? Maximum { get; set; }
    public string? Unit { get; set; }
    /// <summary>The inspection cannot be saved without it.</summary>
    public bool Required { get; set; } = true;
}

public enum InspectionKind
{
    /// <summary>On a production order's output.</summary>
    Production,
    /// <summary>On goods received on a purchase order.</summary>
    Incoming,
}

/// <summary>Where failed goods went.</summary>
public enum InspectionDisposition
{
    /// <summary>Left where they are (incoming goods are dealt with by purchasing).</summary>
    None,
    /// <summary>Moved to a hold location until someone decides.</summary>
    Hold,
    /// <summary>Thrown away (a production scrap movement).</summary>
    Scrap,
}

/// <summary>An inspection of a quantity of one stock code, with a result for each check of its plan.</summary>
public class QualityInspection : Entity, IStoreScoped, ILocalOnly
{
    public Guid StoreId { get; set; }
    public InspectionKind Kind { get; set; }
    public Guid? ProductionOrderId { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    /// <summary>The order's or purchase order's number.</summary>
    public long? DocumentNumber { get; set; }
    public Guid? QualityPlanId { get; set; }
    public string StockCode { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal QuantityInspected { get; set; }
    public decimal QuantityFailed { get; set; }
    public InspectionDisposition Disposition { get; set; }
    public Guid? HoldLocationId { get; set; }
    /// <summary>Every required check passed.</summary>
    public bool Passed { get; set; }
    /// <summary>Store local time.</summary>
    public DateTime InspectedAt { get; set; }
    public string? InspectedBy { get; set; }
    public Guid? InspectedById { get; set; }
    public string? Notes { get; set; }
    public List<QualityResult> Results { get; set; } = [];
}

/// <summary>The result of one check of an inspection (a copy of the check, so the plan can change later).</summary>
public class QualityResult : Entity, ILocalOnly
{
    public Guid QualityInspectionId { get; set; }
    public int LineNumber { get; set; }
    public string CheckName { get; set; } = "";
    public QualityCheckKind Kind { get; set; }
    public decimal? Minimum { get; set; }
    public decimal? Maximum { get; set; }
    public bool Required { get; set; }
    /// <summary>Pass/fail checks: the answer.</summary>
    public bool? PassFail { get; set; }
    public decimal? NumberValue { get; set; }
    public string? TextValue { get; set; }
    public bool Passed { get; set; }
}
