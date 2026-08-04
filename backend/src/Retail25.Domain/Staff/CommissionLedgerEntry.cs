using Retail25.Domain.Common;

namespace Retail25.Domain.Staff;

/// <summary>
/// One line's worth of commission earned, written when a sale completes (guide p.33, p.76).
/// <para>
/// Append-only, like every other ledger in this system. A commission report that recalculates from
/// today's rules would restate what someone was already paid the moment a rule changes — so what was
/// earned is recorded once, with the rule that produced it, and never derived again.
/// </para>
/// </summary>
public sealed class CommissionLedgerEntry : Entity
{
    public CommissionLedgerEntry()
    {
    }

    public long StaffId { get; set; }

    public long LocationId { get; set; }

    public long TransactionId { get; set; }

    public long SaleLineId { get; set; }

    public long ProductId { get; set; }

    /// <summary>Kept for the report so a deleted or renamed item still reads correctly.</summary>
    public string StockCodeSnapshot { get; set; } = string.Empty;

    public long? DepartmentId { get; set; }

    /// <summary>Which rule produced this. Null once the rule itself has been deleted.</summary>
    public long? CommissionRuleId { get; set; }

    public CommissionType CommissionType { get; set; }

    /// <summary>The rule's value as it stood at the moment of the sale.</summary>
    public decimal RateApplied { get; set; }

    /// <summary>Net of discounts and before tax — what the line actually brought in.</summary>
    public decimal LineNet { get; set; }

    /// <summary>Frozen cost, so a percent-of-profit entry stays reproducible.</summary>
    public decimal LineCost { get; set; }

    public decimal Quantity { get; set; }

    public decimal Amount { get; set; }

    /// <summary>True when <see cref="CommissionRule.MaxCommission"/> cut the figure down.</summary>
    public bool WasCapped { get; set; }

    public DateOnly BusinessDate { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
