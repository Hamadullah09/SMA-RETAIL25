using Retail25.Domain.Common;

namespace Retail25.Domain.Receivables;

public enum InvoiceStatus
{
    Open = 0,
    Paid = 1,
    Void = 2,
}

/// <summary>
/// Accounts-receivable invoice (guide p.53–58). Created when an "On Account" tender is used.
/// Supports partial payments, late charges, and distribute-payment across invoices.
/// </summary>
public sealed class Invoice : AggregateRoot, IAuditable
{
    private Invoice()
    {
    }

    public long InvoiceNumber { get; set; }

    public Guid CustomerId { get; set; }

    public Guid TransactionId { get; set; }

    public DateOnly IssuedOn { get; set; }

    public DateOnly DueOn { get; set; }

    public decimal InvoiceTotal { get; set; }

    /// <summary>Accrued late charges (guide p.56).</summary>
    public decimal PenaltyAccrued { get; set; }

    /// <summary>Derived from AR ledger entries. Rebuildable by replay.</summary>
    public decimal BalanceDue { get; set; }

    public DateOnly? LastPaymentOn { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Open;

    public Guid StaffId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}
