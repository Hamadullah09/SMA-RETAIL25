using Retail25.Domain.Common;

namespace Retail25.Domain.Receivables;

public enum AREntryType
{
    Charge = 0,
    Payment = 1,
    LateCharge = 2,
    Refund = 3,
    Void = 4,
    Adjustment = 5,
}

/// <summary>
/// Append-only accounts-receivable ledger (guide p.56–58). Invoice.BalanceDue is derived
/// from this ledger. Supports penalty-first allocation and distribute-payment.
/// </summary>
public sealed class ARLedgerEntry : Entity
{
    public ARLedgerEntry()
    {
    }

    public long CustomerId { get; set; }

    public long InvoiceId { get; set; }

    public AREntryType EntryType { get; set; }

    /// <summary>Signed: positive for charges, negative for payments/refunds.</summary>
    public decimal Amount { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
