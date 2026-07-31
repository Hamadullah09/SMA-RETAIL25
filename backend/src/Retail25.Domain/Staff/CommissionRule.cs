using Retail25.Domain.Common;

namespace Retail25.Domain.Staff;

public enum CommissionType
{
    /// <summary>A percentage of the line's net takings.</summary>
    Percentage = 0,

    /// <summary>A fixed amount per unit sold — so three of an item pays three times.</summary>
    Fixed = 1,

    /// <summary>A percentage of the line's margin. Pays nothing on a line sold at or below cost.</summary>
    PercentOfProfit = 2,
}

/// <summary>
/// Per-item commission rules (guide p.33, p.76). Staff can have different commission structures
/// for different products or departments.
/// </summary>
public sealed class CommissionRule : Entity, IAuditable
{
    public static readonly Error ValueRequired = new(
        "commission.value_required",
        "A commission rule has to pay something.");

    public static readonly Error TooSpecific = new(
        "commission.too_specific",
        "A rule applies to an item or to a department, not both.");

    public static readonly Error PercentageOutOfRange = new(
        "commission.percentage_out_of_range",
        "A commission percentage has to be between 0 and 100.");

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

    /// <summary>
    /// How narrowly this rule aims. Higher wins when several rules could apply to one line: an
    /// item-specific rate is a deliberate exception to the department rate, which is itself a
    /// deliberate exception to whatever the person earns on everything else.
    /// </summary>
    public int Specificity => ProductId is not null ? 2 : DepartmentId is not null ? 1 : 0;

    public static Result<CommissionRule> Create(
        Guid staffId,
        CommissionType commissionType,
        decimal value,
        Guid? productId = null,
        Guid? departmentId = null,
        decimal? maxCommission = null)
    {
        if (productId is not null && departmentId is not null)
        {
            return Result.Failure<CommissionRule>(TooSpecific);
        }

        var validated = Validate(commissionType, value);

        if (validated.IsFailure)
        {
            return Result.Failure<CommissionRule>(validated.Error);
        }

        return Result.Success(new CommissionRule
        {
            StaffId = staffId,
            ProductId = productId,
            DepartmentId = departmentId,
            CommissionType = commissionType,
            Value = value,
            MaxCommission = maxCommission,
            IsActive = true,
        });
    }

    public Result Update(CommissionType commissionType, decimal value, decimal? maxCommission, bool isActive)
    {
        var validated = Validate(commissionType, value);

        if (validated.IsFailure)
        {
            return validated;
        }

        CommissionType = commissionType;
        Value = value;
        MaxCommission = maxCommission;
        IsActive = isActive;
        return Result.Success();
    }

    private static Result Validate(CommissionType commissionType, decimal value)
    {
        if (value <= 0m)
        {
            return Result.Failure(ValueRequired);
        }

        // A percentage above 100 pays out more than the sale brought in. Almost always a typo — a
        // rate meant as 5 entered as 500 — and the sort that is only noticed at payroll.
        if (commissionType is CommissionType.Percentage or CommissionType.PercentOfProfit && value > 100m)
        {
            return Result.Failure(PercentageOutOfRange.With("value", value));
        }

        return Result.Success();
    }
}
