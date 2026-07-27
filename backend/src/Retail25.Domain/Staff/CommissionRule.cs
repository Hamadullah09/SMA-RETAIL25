using Retail25.Domain.Common;

namespace Retail25.Domain.Staff;

public enum CommissionType
{
    Percentage = 0,
    Fixed = 1,
    PercentOfProfit = 2,
}

/// <summary>
/// Per-item commission rules (guide p.33, p.76). Staff can have different commission structures
/// for different products or departments.
/// </summary>
public sealed class CommissionRule : Entity, IAuditable
{
    private CommissionRule()
    {
    }

    public Guid StaffId { get; set; }

    /// <summary>Optional: specific product this rule applies to. Null = applies to all.</summary>
    public Guid? ProductId { get; set; }

    /// <summary>Optional: specific department this rule applies to.</summary>
    public Guid? DepartmentId { get; set; }

    public CommissionType CommissionType { get; set; }

    /// <summary>Value meaning depends on CommissionType: %, fixed amount, or % of profit.</summary>
    public decimal Value { get; set; }

    /// <summary>Maximum commission per item (guide p.33).</summary>
    public decimal? MaxCommission { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}
