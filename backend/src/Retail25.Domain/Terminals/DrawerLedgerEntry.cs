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
/// Append-only cash movement (guide p.10–11). Expected cash at close is the sum of this stream, so
/// a drawer can always be reconstructed from first principles rather than trusted.
/// </summary>
public sealed class DrawerLedgerEntry : Entity
{
    public static readonly Error ReasonRequired = new("drawer.reason_required", "A pay-in or pay-out needs a reason.");

    public DrawerLedgerEntry()
    {
    }

    public long DrawerSessionId { get; set; }

    public DrawerEntryType EntryType { get; set; }

    /// <summary>Signed: positive for float, cash sales and pay-ins; negative for refunds and pay-outs.</summary>
    public decimal Amount { get; set; }

    public string? Reason { get; set; }

    public long? TransactionId { get; set; }

    public long StaffId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>A drawer pop with no sale attached still leaves a trace (guide p.11).</summary>
    public bool AffectsCashTotal => EntryType != DrawerEntryType.NoSalePop;

    public static DrawerLedgerEntry Create(
        long drawerSessionId,
        DrawerEntryType type,
        decimal signedAmount,
        long staffId,
        DateTimeOffset now,
        string? reason = null,
        long? transactionId = null)
        => new()
        {
            DrawerSessionId = drawerSessionId,
            EntryType = type,
            Amount = signedAmount,
            StaffId = staffId,
            OccurredAt = now,
            Reason = reason,
            TransactionId = transactionId,
        };

    public static Result<DrawerLedgerEntry> PayIn(long sessionId, decimal amount, string reason, long staffId, DateTimeOffset now)
    {
        if (amount <= 0m)
        {
            return Result.Failure<DrawerLedgerEntry>(DrawerSession.AmountInvalid.With("value", amount));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure<DrawerLedgerEntry>(ReasonRequired);
        }

        return Result.Success(Create(sessionId, DrawerEntryType.PayIn, amount, staffId, now, reason.Trim()));
    }

    public static Result<DrawerLedgerEntry> PayOut(long sessionId, decimal amount, string reason, long staffId, DateTimeOffset now)
    {
        if (amount <= 0m)
        {
            return Result.Failure<DrawerLedgerEntry>(DrawerSession.AmountInvalid.With("value", amount));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure<DrawerLedgerEntry>(ReasonRequired);
        }

        return Result.Success(Create(sessionId, DrawerEntryType.PayOut, -amount, staffId, now, reason.Trim()));
    }
}
