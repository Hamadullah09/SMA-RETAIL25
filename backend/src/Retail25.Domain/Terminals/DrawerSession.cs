using Retail25.Domain.Common;

namespace Retail25.Domain.Terminals;

public enum DrawerSessionStatus
{
    Open = 0,
    Closed = 1,
}

/// <summary>
/// A cash drawer from float to close (guide p.10–11, the legacy F10 menu).
/// <para>
/// Expected cash is never kept as a mutable running figure — it is derived from the append-only
/// <see cref="DrawerLedgerEntry"/> stream and frozen onto the session at close, next to the counted
/// cash and the variance between them. That is what makes a drawer report defensible.
/// </para>
/// </summary>
public sealed class DrawerSession : AggregateRoot, IAuditable
{
    public static readonly Error AlreadyClosed = new("drawer.already_closed", "This drawer session is already closed.");
    public static readonly Error AlreadyOpen = new("drawer.already_open", "This station already has an open drawer session.");
    public static readonly Error NotOpen = new("drawer.not_open", "There is no open drawer session at this station.");
    public static readonly Error AmountInvalid = new("drawer.amount_invalid", "A drawer movement must be greater than zero.");

    public DrawerSession()
    {
    }

    public long StationId { get; set; }

    public long LocationId { get; set; }

    public long OpenedByStaffId { get; set; }

    public long? ClosedByStaffId { get; set; }

    public decimal OpeningFloat { get; set; }

    public DrawerSessionStatus Status { get; set; } = DrawerSessionStatus.Open;

    public DateOnly BusinessDate { get; set; }

    public DateTimeOffset OpenedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>Cash physically counted at close (guide p.11).</summary>
    public decimal? CountedCash { get; set; }

    /// <summary>OpeningFloat + cash sales − cash refunds + pay-ins − pay-outs, replayed from the ledger.</summary>
    public decimal ExpectedCash { get; set; }

    /// <summary>CountedCash − ExpectedCash. Negative is a shortage.</summary>
    public decimal Variance { get; set; }

    /// <summary>Totals per tender type at close, serialised for the drawer report (guide p.15).</summary>
    public string? TenderTotalsJson { get; set; }

    public string? DepartmentNetSalesJson { get; set; }

    public decimal NetSales { get; set; }

    public decimal Tax1Collected { get; set; }

    public decimal Tax2Collected { get; set; }

    public decimal CostOfGoodsSold { get; set; }

    public int TransactionCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public bool IsOpen => Status == DrawerSessionStatus.Open;

    public static Result<DrawerSession> Open(
        long stationId,
        long locationId,
        long staffId,
        decimal openingFloat,
        DateOnly businessDate,
        DateTimeOffset now)
    {
        if (openingFloat < 0m)
        {
            return Result.Failure<DrawerSession>(AmountInvalid.With("value", openingFloat));
        }

        return Result.Success(new DrawerSession
        {
            StationId = stationId,
            LocationId = locationId,
            OpenedByStaffId = staffId,
            OpeningFloat = openingFloat,
            BusinessDate = businessDate,
            OpenedAt = now,
            Status = DrawerSessionStatus.Open,
        });
    }

    /// <summary>
    /// Closes the drawer against a physical count. The expected figure is passed in because it comes
    /// from replaying the ledger, which is the application's job rather than the aggregate's.
    /// </summary>
    public Result Close(
        decimal countedCash,
        decimal expectedCash,
        long staffId,
        DateTimeOffset now,
        string? tenderTotalsJson = null,
        string? departmentNetSalesJson = null)
    {
        if (Status == DrawerSessionStatus.Closed)
        {
            return Result.Failure(AlreadyClosed);
        }

        CountedCash = countedCash;
        ExpectedCash = expectedCash;
        Variance = countedCash - expectedCash;
        TenderTotalsJson = tenderTotalsJson;
        DepartmentNetSalesJson = departmentNetSalesJson;
        ClosedByStaffId = staffId;
        ClosedAt = now;
        Status = DrawerSessionStatus.Closed;
        ModifiedAt = now;
        return Result.Success();
    }

    /// <summary>Rolls sale totals onto the session so the close report does not have to re-aggregate.</summary>
    public void RecordSale(decimal netSales, decimal tax1, decimal tax2, decimal costOfGoodsSold)
    {
        NetSales += netSales;
        Tax1Collected += tax1;
        Tax2Collected += tax2;
        CostOfGoodsSold += costOfGoodsSold;
        TransactionCount++;
    }
}
