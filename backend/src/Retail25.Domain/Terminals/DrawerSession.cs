using Retail25.Domain.Common;

namespace Retail25.Domain.Terminals;

public enum DrawerSessionStatus
{
    Open = 0,
    Closed = 1,
}

/// <summary>
/// Cash drawer session (guide p.10–11). Tracks float, pay-ins/outs, and closing totals.
/// The session creates a snapshot when closed with variance calculation.
/// </summary>
public sealed class DrawerSession : AggregateRoot, IAuditable
{
    private DrawerSession()
    {
    }

    public Guid StationId { get; set; }

    public Guid OpenedByStaffId { get; set; }

    public decimal OpeningFloat { get; set; }

    public DrawerSessionStatus Status { get; set; } = DrawerSessionStatus.Open;

    public DateTimeOffset OpenedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>Cash counted at close (guide p.11).</summary>
    public decimal? CountedCash { get; set; }

    /// <summary>Expected cash = OpeningFloat + CashSales - CashRefunds + PayIns - PayOuts.</summary>
    public decimal ExpectedCash { get; set; }

    /// <summary>Variance = CountedCash - ExpectedCash.</summary>
    public decimal Variance { get; set; }

    /// <summary>JSON-serialised totals per tender type (guide p.15).</summary>
    public string? TenderTotalsJson { get; set; }

    /// <summary>JSON-serialised net sales per department.</summary>
    public string? DepartmentNetSalesJson { get; set; }

    public decimal Tax1Collected { get; set; }

    public decimal Tax2Collected { get; set; }

    public decimal CostOfGoodsSold { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}
