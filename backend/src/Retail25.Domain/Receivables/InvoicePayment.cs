using Retail25.Domain.Common;

namespace Retail25.Domain.Receivables;

/// <summary>
/// A payment against an invoice (guide p.58). Supports partial payments and back-dating.
/// Penalty-first allocation is explicit, not inferred.
/// </summary>
public sealed class InvoicePayment : Entity, IAuditable
{
    public InvoicePayment()
    {
    }

    public Guid InvoiceId { get; set; }

    public decimal Amount { get; set; }

    /// <summary>Portion applied to late-charge penalty (guide p.58: penalty first).</summary>
    public decimal AppliedToPenalty { get; set; }

    /// <summary>Portion applied to the invoice principal.</summary>
    public decimal AppliedToPrincipal { get; set; }

    public Guid TenderTypeId { get; set; }

    /// <summary>Back-datable payment date (guide p.58).</summary>
    public DateOnly PaidOn { get; set; }

    /// <summary>True if this payment was distributed via the distribute-payment command.</summary>
    public bool WasDistributed { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}
