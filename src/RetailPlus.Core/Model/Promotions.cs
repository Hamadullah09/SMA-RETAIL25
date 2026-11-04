namespace RetailPlus.Core.Model;

/// <summary>What a promotion gives (see <c>Rules.PromotionEngine</c>).</summary>
public enum PromotionKind
{
    /// <summary>A percentage off each line it applies to.</summary>
    PercentOff,
    /// <summary>An amount off each unit it applies to.</summary>
    AmountOff,
    /// <summary>Buy X, get Y free or at a discount.</summary>
    BuyXGetY,
    /// <summary>A set of items together for one price.</summary>
    BundlePrice,
    /// <summary>Spend over an amount, get a discount on the sale.</summary>
    SpendOver,
}

/// <summary>Which items a promotion applies to.</summary>
public enum PromotionScope
{
    AllItems,
    Departments,
    Categories,
    Items,
}

/// <summary>What a promotion's target row is for.</summary>
public enum PromotionTargetRole
{
    /// <summary>The items the promotion applies to (what must be bought, for buy X get Y).</summary>
    Qualifies,
    /// <summary>Buy X get Y: the items given free or at a discount, when they are not the same as those bought.</summary>
    Reward,
    /// <summary>One part of a bundle, with its quantity.</summary>
    BundlePart,
}

/// <summary>
/// A promotion at the till: what it gives, to which items, when (dates, days of the week and a happy hour), at which
/// stores, how it ranks against others, and whether it needs a coupon code. Set on the server (or on the desktop of a
/// shop without one) and sent down to every till, where <c>Rules.PromotionEngine</c> applies it to the sale.
/// </summary>
public class Promotion : Entity, IVersioned
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public PromotionKind Kind { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>First day (store local); null from now on.</summary>
    public DateOnly? StartsOn { get; set; }
    /// <summary>Last day; null with no end.</summary>
    public DateOnly? EndsOn { get; set; }
    /// <summary>The days of the week it runs, one bit per <see cref="DayOfWeek"/> (bit 0 Sunday); 0 every day.</summary>
    public int Days { get; set; }
    /// <summary>A happy hour: from this time of day…</summary>
    public TimeOnly? StartTime { get; set; }
    /// <summary>…to this one (an end before the start runs past midnight).</summary>
    public TimeOnly? EndTime { get; set; }
    /// <summary>Breaks ties between promotions that give the same price: the higher first.</summary>
    public int Priority { get; set; }
    /// <summary>May be used together with other combinable promotions; when false it is used alone on a sale.</summary>
    public bool Combinable { get; set; }
    /// <summary>Runs at every store; otherwise only at the stores of <see cref="Stores"/>.</summary>
    public bool AllStores { get; set; } = true;
    public PromotionScope Scope { get; set; }
    /// <summary>PercentOff, and SpendOver given as a percentage.</summary>
    public decimal Percent { get; set; }
    /// <summary>AmountOff (per unit), and SpendOver given as an amount.</summary>
    public decimal Amount { get; set; }
    public int BuyQuantity { get; set; }
    public int GetQuantity { get; set; }
    /// <summary>Buy X get Y: the discount on the units given, in percent (100 = free).</summary>
    public decimal GetPercent { get; set; } = 100;
    public decimal BundlePrice { get; set; }
    /// <summary>SpendOver: the spend on the counted lines from which it applies.</summary>
    public decimal SpendThreshold { get; set; }
    /// <summary>When set, the promotion applies only once this code is entered at the till (upper case).</summary>
    public string? CouponCode { get; set; }
    /// <summary>How often the coupon may be used across every store; 0 no limit, 1 one use.</summary>
    public int CouponMaxUses { get; set; }
    public Guid Version { get; set; }
    public List<PromotionTarget> Targets { get; set; } = [];
    public List<PromotionStore> Stores { get; set; } = [];
}

/// <summary>
/// One department, category or stock code a promotion applies to, rewards or bundles. Items are named by stock code
/// because each store keeps its own row of an item.
/// </summary>
public class PromotionTarget : Entity
{
    public Guid PromotionId { get; set; }
    public PromotionTargetRole Role { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? CategoryId { get; set; }
    public string? Plu { get; set; }
    /// <summary>For a bundle part: how many of it the bundle holds.</summary>
    public decimal Quantity { get; set; } = 1;
}

/// <summary>A store a promotion runs at, when it does not run at every store.</summary>
public class PromotionStore : Entity
{
    public Guid PromotionId { get; set; }
    public Guid StoreId { get; set; }
}

/// <summary>
/// A coupon code used on a sale. Written at the till with the sale and sent to every till, so each one knows how often a
/// coupon was used; a void takes the use back.
/// </summary>
public class CouponUse : Entity, IStationStamped
{
    public Guid StoreId { get; set; }
    public int StationNumber { get; set; }
    /// <summary>No foreign key: a promotion is switched off, not deleted, once it was used.</summary>
    public Guid PromotionId { get; set; }
    public string Code { get; set; } = "";
    public Guid SaleId { get; set; }
    public long InvoiceNumber { get; set; }
    public Guid? ClientId { get; set; }
    /// <summary>Store local time.</summary>
    public DateTime UsedAt { get; set; }
}
