namespace RetailPlus.Core.Model;

/// <summary>
/// One line of the audit log: who did what, when and where. Changes to the important tables are written by the DbContext
/// itself (with the changed values before and after); sign-ins, approvals, voids, End of Day, backups and imports are
/// written by the code that does them.
/// </summary>
public class AuditEntry : Entity, IStationStamped
{
    public Guid StoreId { get; set; }
    public int StationNumber { get; set; }
    /// <summary>When it happened, in UTC.</summary>
    public DateTime AtUtc { get; set; }
    /// <summary>The signed-in user; null before anyone signed in, or for a failed sign-in by an unknown name.</summary>
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    /// <summary>What happened: Added, Changed, Deleted, or one of the actions in <c>AuditActions</c> (Sign in, Void sale…).</summary>
    public string Action { get; set; } = "";
    /// <summary>The table of the row concerned (Item, Client, UserAccount…), when there is one.</summary>
    public string? EntityName { get; set; }
    public Guid? EntityId { get; set; }
    /// <summary>The changed properties before the change, as a JSON object; null for added rows.</summary>
    public string? Before { get; set; }
    /// <summary>The changed properties after the change, as a JSON object; null for deleted rows.</summary>
    public string? After { get; set; }
    /// <summary>A short description for the list (the item's code and name, the reason given…).</summary>
    public string? Details { get; set; }
}

/// <summary>
/// A shift on the time clock: punched in, and punched out (null while the user is still in). A user has at most one open
/// entry. Times are the store's local time, like every business time. A supervisor may correct an entry; the reason
/// stays on the entry and the change is audited.
/// </summary>
public class TimeClockEntry : Entity, IStationStamped
{
    public Guid StoreId { get; set; }
    public int StationNumber { get; set; }
    public Guid UserId { get; set; }
    /// <summary>The user's name when the entry was made, for the hours report.</summary>
    public string UserName { get; set; } = "";
    public DateTime ClockIn { get; set; }
    public DateTime? ClockOut { get; set; }
    /// <summary>Why a supervisor changed or added the entry; null for entries made by punching only.</summary>
    public string? EditReason { get; set; }
    public string? EditedBy { get; set; }
    public Guid? EditedById { get; set; }
    public DateTime? EditedAtUtc { get; set; }

    public bool IsOpen => ClockOut is null;
}

/// <summary>Which sales lines a commission rule covers; a narrower scope wins over a wider one.</summary>
public enum CommissionScope
{
    AllItems,
    Department,
    Category,
    Item,
}

/// <summary>How a commission rule's <see cref="CommissionRule.Rate"/> is read.</summary>
public enum CommissionBasis
{
    /// <summary>Rate percent of the line's net amount (after line discounts).</summary>
    PercentOfNetSales,
    /// <summary>Rate in money for every unit sold.</summary>
    AmountPerUnit,
}

/// <summary>
/// How much commission salespeople earn on what they sell, for everyone or one user, on all items or one department,
/// category or item, between two dates. Sales lines no rule covers keep the commission the item's own rate gave them
/// when they were sold (Retail Plus's COMMISSION column). See <c>Rules.Commissions</c> for which rule wins.
/// </summary>
public class CommissionRule : Entity
{
    public string Name { get; set; } = "";
    /// <summary>The salesperson the rule is for; null for everyone.</summary>
    public Guid? UserId { get; set; }
    public CommissionScope Scope { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? ItemId { get; set; }
    public CommissionBasis Basis { get; set; }
    /// <summary>Percent for <see cref="CommissionBasis.PercentOfNetSales"/>, money per unit for <see cref="CommissionBasis.AmountPerUnit"/>.</summary>
    public decimal Rate { get; set; }
    /// <summary>First day the rule applies (store local date); null from the beginning.</summary>
    public DateOnly? EffectiveFrom { get; set; }
    /// <summary>Last day the rule applies; null with no end.</summary>
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
}
