using Retail25.Domain.Common;

namespace Retail25.Domain.Receivables;

/// <summary>
/// Late-charge accrual policy (guide p.56, p.84). Applied by a nightly Hangfire job.
/// Payment applies to penalty first, then principal. Next penalty accrues from LastPaymentOn.
/// </summary>
public sealed class LateChargePolicy : AggregateRoot, IAuditable
{
    public LateChargePolicy()
    {
    }

    public Guid LocationId { get; set; }

    /// <summary>Monthly interest rate as a percentage (e.g. 1.5 for 1.5%).</summary>
    public decimal MonthlyRate { get; set; }

    /// <summary>Days after invoice date before late charges begin.</summary>
    public int GracePeriodDays { get; set; }

    public bool IsEnabled { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}
