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

    /// <summary>
    /// Opens a drawer with its starting float (guide p.10, "F2 Float").
    /// </summary>
    /// <param name="stationId">Till the drawer belongs to.</param>
    /// <param name="staffId">Who counted the float in.</param>
    /// <param name="openingFloat">Cash placed in the drawer to start the shift.</param>
    /// <param name="openedAt">When.</param>
    public static Result<DrawerSession> Open(
        Guid stationId,
        Guid staffId,
        decimal openingFloat,
        DateTimeOffset openedAt)
    {
        if (openingFloat < 0m)
        {
            return Result.Failure<DrawerSession>(
                new Error("drawer.float_negative", "An opening float cannot be negative."));
        }

        return Result.Success(new DrawerSession
        {
            StationId = stationId,
            OpenedByStaffId = staffId,
            OpeningFloat = openingFloat,
            ExpectedCash = openingFloat,
            Status = DrawerSessionStatus.Open,
            OpenedAt = openedAt,
            CreatedAt = openedAt,
        });
    }

    /// <summary>
    /// Applies a cash movement to the expected balance. Called for every cash sale, refund, pay-in
    /// and pay-out, so the expected figure is always current rather than being reconstructed at
    /// close from a query that might miss something.
    /// </summary>
    /// <param name="signedAmount">Positive brings cash in, negative takes it out.</param>
    public Result RecordCashMovement(decimal signedAmount)
    {
        if (Status != DrawerSessionStatus.Open)
        {
            return Result.Failure(new Error("drawer.not_open", "This drawer session is already closed."));
        }

        ExpectedCash += signedAmount;
        return Result.Success();
    }

    /// <summary>
    /// Closes the drawer against a physical count (guide p.10, "F5 Save").
    /// <para>
    /// The variance is recorded rather than corrected. A drawer that is short by five is a fact the
    /// business needs to see; quietly adjusting the expected figure to match the count would erase
    /// exactly the signal the count exists to produce.
    /// </para>
    /// </summary>
    /// <param name="countedCash">What was physically counted.</param>
    /// <param name="closedAt">When.</param>
    public Result Close(decimal countedCash, DateTimeOffset closedAt)
    {
        if (Status != DrawerSessionStatus.Open)
        {
            return Result.Failure(new Error("drawer.not_open", "This drawer session is already closed."));
        }

        CountedCash = countedCash;
        Variance = countedCash - ExpectedCash;
        Status = DrawerSessionStatus.Closed;
        ClosedAt = closedAt;

        return Result.Success();
    }
}
