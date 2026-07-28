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

    /// <summary>
    /// Records a movement. The sign carries the direction, so the drawer's expected cash is the
    /// plain sum of its entries and no reader needs to know what each entry type means.
    /// </summary>
    /// <param name="drawerSessionId">Session the movement belongs to.</param>
    /// <param name="entryType">What kind of movement.</param>
    /// <param name="signedAmount">Positive brings cash in, negative takes it out.</param>
    /// <param name="staffId">Who did it.</param>
    /// <param name="occurredAt">When.</param>
    /// <param name="reason">Why — required for pay-ins and pay-outs, which are otherwise unexplained.</param>
    public static DrawerLedgerEntry Create(
        Guid drawerSessionId,
        DrawerEntryType entryType,
        decimal signedAmount,
        Guid staffId,
        DateTimeOffset occurredAt,
        string? reason = null) => new()
        {
            DrawerSessionId = drawerSessionId,
            EntryType = entryType,
            Amount = signedAmount,
            StaffId = staffId,
            OccurredAt = occurredAt,
            Reason = reason,
        };
}
