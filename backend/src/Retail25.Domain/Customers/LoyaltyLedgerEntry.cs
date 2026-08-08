using Retail25.Domain.Common;

namespace Retail25.Domain.Customers;

public enum LoyaltyEntryType
{
    Earned = 0,
    Redeemed = 1,
    ReturnClawback = 2,
    Manual = 3,
}

/// <summary>
/// Append-only loyalty points ledger (guide p.83–84). Customer.RewardPoints is derived from this.
/// Returns claw back points at the original earn rate (decision P5).
/// </summary>
public sealed class LoyaltyLedgerEntry : Entity
{
    public LoyaltyLedgerEntry()
    {
    }

    public long CustomerId { get; set; }

    public long? TransactionId { get; set; }

    public LoyaltyEntryType EntryType { get; set; }

    /// <summary>Signed: positive for earned, negative for redeemed/clawback.</summary>
    public int Points { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public static LoyaltyLedgerEntry Earn(long customerId, long transactionId, int points, DateTimeOffset at)
    {
        return new LoyaltyLedgerEntry
        {
            CustomerId = customerId,
            TransactionId = transactionId,
            EntryType = LoyaltyEntryType.Earned,
            Points = points,
            OccurredAt = at,
        };
    }

    public static LoyaltyLedgerEntry Redeem(long customerId, int points, DateTimeOffset at)
    {
        return new LoyaltyLedgerEntry
        {
            CustomerId = customerId,
            EntryType = LoyaltyEntryType.Redeemed,
            Points = -points,
            OccurredAt = at,
        };
    }

    public static LoyaltyLedgerEntry Clawback(long customerId, long transactionId, int points, DateTimeOffset at)
    {
        return new LoyaltyLedgerEntry
        {
            CustomerId = customerId,
            TransactionId = transactionId,
            EntryType = LoyaltyEntryType.ReturnClawback,
            Points = -points,
            OccurredAt = at,
        };
    }

    /// <summary>
    /// A supervisor's manual correction — a goodwill grant, a fix for a miscounted sale. Signed:
    /// positive to grant, negative to take back.
    /// </summary>
    public static LoyaltyLedgerEntry Manual(long customerId, int points, DateTimeOffset at)
    {
        return new LoyaltyLedgerEntry
        {
            CustomerId = customerId,
            EntryType = LoyaltyEntryType.Manual,
            Points = points,
            OccurredAt = at,
        };
    }
}
