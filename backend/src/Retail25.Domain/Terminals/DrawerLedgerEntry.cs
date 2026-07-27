using Retail25.Domain.Common;

namespace Retail25.Domain.Terminals;

public enum DrawerEntryType
{
    OpeningFloat = 0,
    Sale = 1,
    Refund = 2,
    PayIn = 3,
    PayOut = 4,
    NoSalePop = 5,
    Correction = 6,
}

/// <summary>
/// Append-only ledger for cash drawer movements (guide p.10–11). Used to calculate expected
/// cash at close and to produce the drawer report.
/// </summary>
public sealed class DrawerLedgerEntry : Entity
{
    private DrawerLedgerEntry()
    {
    }

    public Guid DrawerSessionId { get; set; }

    public DrawerEntryType EntryType { get; set; }

    /// <summary>Signed amount: positive for float/sale/pay-in, negative for refund/pay-out.</summary>
    public decimal Amount { get; set; }

    public string? Reason { get; set; }

    public Guid StaffId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
