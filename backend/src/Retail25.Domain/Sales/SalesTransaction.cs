using Retail25.Domain.Common;

namespace Retail25.Domain.Sales;

public enum TransactionStatus
{
    Completed = 0,
    Voided = 1,
    /// <summary>A reversing transaction created to void an earlier sale.</summary>
    Reversal = 2,
}

/// <summary>
/// A completed sale, written once and never mutated. Voids do not edit history: they create a
/// reversing transaction and flip the original's status, so the ledger always replays to the same
/// numbers. Every monetary field is a snapshot taken at completion, which is what lets a reprint
/// months later show the taxes that were in force on the day (guide p.56).
/// </summary>
public sealed class SalesTransaction : AggregateRoot, IAuditable
{
    public SalesTransaction()
    {
    }

    /// <summary>Sequential per location, drawn from a database sequence so two stations cannot collide.</summary>
    public long TransactionNumber { get; set; }

    public long LocationId { get; set; }

    public long StationId { get; set; }

    public long StaffId { get; set; }

    public long? CustomerId { get; set; }

    public long? DrawerSessionId { get; set; }

    /// <summary>The store's own trading day, derived from its time zone and day-start (not the server clock).</summary>
    public DateOnly BusinessDate { get; set; }

    public decimal Subtotal { get; set; }

    public decimal DiscountTotal { get; set; }

    public decimal AddOnChargeTotal { get; set; }

    public decimal Tax1Total { get; set; }

    public decimal Tax2Total { get; set; }

    public decimal GrandTotal { get; set; }

    /// <summary>The penny given up or gained by rounding the cash portion to the smallest coin (guide p.84).</summary>
    public decimal RoundingAdjustment { get; set; }

    public decimal ChangeGiven { get; set; }

    /// <summary>COGS frozen from AvgCost at sale time so margin reports stay stable (guide p.14).</summary>
    public decimal CostOfGoodsSold { get; set; }

    public int LoyaltyPointsEarned { get; set; }

    public int LoyaltyPointsRedeemed { get; set; }

    public TransactionStatus Status { get; set; } = TransactionStatus.Completed;

    /// <summary>
    /// A practice sale rung by a level-0 trainee (guide p.82). The whole POS flow runs and the
    /// transaction is written, but it moves no stock, no drawer, no loyalty and no money — and every
    /// report excludes it by default, so training on a live till cannot quietly corrupt the numbers
    /// the shop is run on. Set server-side from the staff member's access level, never by the client.
    /// </summary>
    public bool IsTraining { get; set; }

    /// <summary>On the original sale: the reversal that voided it. On a reversal: null.</summary>
    public long? VoidedByTransactionId { get; set; }

    /// <summary>On a reversal: the sale it reverses.</summary>
    public long? ReversesTransactionId { get; set; }

    public string? VoidReason { get; set; }

    public long? VoidApprovedByStaffId { get; set; }

    /// <summary>Set when the sale was settled wholly or partly on account (guide p.51).</summary>
    public long? InvoiceId { get; set; }

    public int ReprintCount { get; set; }

    public DateTimeOffset CompletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public bool IsVoided => Status == TransactionStatus.Voided;

    public Result Void(long reversalTransactionId, long approvedByStaffId, string? reason, DateTimeOffset now)
    {
        if (Status != TransactionStatus.Completed)
        {
            return Result.Failure(new Error("sale.not_voidable", "Only a completed sale can be voided."));
        }

        Status = TransactionStatus.Voided;
        VoidedByTransactionId = reversalTransactionId;
        VoidApprovedByStaffId = approvedByStaffId;
        VoidReason = reason;
        ModifiedAt = now;
        return Result.Success();
    }

    public void RecordReprint(DateTimeOffset now)
    {
        ReprintCount++;
        ModifiedAt = now;
    }
}
