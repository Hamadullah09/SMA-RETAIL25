using Retail25.Domain.Common;

namespace Retail25.Domain.Sales;

public enum TransactionStatus
{
    Completed = 0,
    Voided = 1,
}

/// <summary>
/// An immutable ledger entry representing a completed sale. One transaction per sale;
/// voids create a reversing transaction linked via <see cref="VoidedByTransactionId"/>.
/// Every monetary field is a snapshot frozen at sale time.
/// </summary>
public sealed class SalesTransaction : AggregateRoot, IAuditable
{
    public SalesTransaction()
    {
    }

    public long TransactionNumber { get; set; }

    public Guid LocationId { get; set; }

    public Guid StationId { get; set; }

    public Guid StaffId { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid? DrawerSessionId { get; set; }

    public decimal Subtotal { get; set; }

    public decimal DiscountTotal { get; set; }

    public decimal AddOnChargeTotal { get; set; }

    public decimal Tax1Total { get; set; }

    public decimal Tax2Total { get; set; }

    public decimal GrandTotal { get; set; }

    /// <summary>COGS captured at sale time from AvgCost (guide p.14).</summary>
    public decimal CostOfGoodsSold { get; set; }

    public int LoyaltyPointsEarned { get; set; }

    public int LoyaltyPointsRedeemed { get; set; }

    public TransactionStatus Status { get; set; } = TransactionStatus.Completed;

    /// <summary>Links a void to the original sale (guide p.14).</summary>
    public Guid? VoidedByTransactionId { get; set; }

    public DateTimeOffset CompletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}
