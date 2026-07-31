using Retail25.Domain.Common;

namespace Retail25.Domain.Inventory;

public enum FiscalYearStatus
{
    Open = 0,
    Closed = 1,
}

/// <summary>
/// An accounting year and whether it has been closed (guide p.29).
/// <para>
/// Closing is a checkpoint, not a deletion. The legacy close cleared histories and rolled this
/// year's monthly figures into last year's; here nothing is destroyed — the year's sales are rolled
/// up into <see cref="SalesHistoryArchive"/> rows and a zero-quantity ledger checkpoint is written
/// per item, so the ledger still replays to the same on-hand and every question about a closed year
/// is still answerable from the transactions themselves.
/// </para>
/// </summary>
public sealed class FiscalYear : AggregateRoot, IAuditable
{
    public static readonly Error EndsBeforeItStarts = new(
        "fiscal_year.ends_before_it_starts",
        "A fiscal year cannot end before it begins.");

    public static readonly Error AlreadyClosed = new(
        "fiscal_year.already_closed",
        "That year has already been closed.");

    public static readonly Error NotClosed = new(
        "fiscal_year.not_closed",
        "That year is not closed.");

    public static readonly Error Overlaps = new(
        "fiscal_year.overlaps",
        "That period overlaps a year that already exists.");

    public static readonly Error EarlierYearStillOpen = new(
        "fiscal_year.earlier_year_still_open",
        "An earlier year is still open. Close the years in order.");

    private FiscalYear()
    {
    }

    public Guid LocationId { get; set; }

    /// <summary>The year as people say it — 2026 for the year that mostly falls in 2026.</summary>
    public int Year { get; set; }

    public DateOnly StartsOn { get; set; }

    public DateOnly EndsOn { get; set; }

    public FiscalYearStatus Status { get; set; } = FiscalYearStatus.Open;

    public DateTimeOffset? ClosedAt { get; set; }

    public Guid? ClosedBy { get; set; }

    /// <summary>How many archive rows the close wrote. Zero is a legitimate answer for a quiet year.</summary>
    public int ArchivedRows { get; set; }

    public decimal ArchivedNetSales { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    public bool Contains(DateOnly date) => date >= StartsOn && date <= EndsOn;

    public static Result<FiscalYear> Create(Guid locationId, int year, DateOnly startsOn, DateOnly endsOn, string? notes = null)
    {
        if (endsOn < startsOn)
        {
            return Result.Failure<FiscalYear>(EndsBeforeItStarts);
        }

        return Result.Success(new FiscalYear
        {
            LocationId = locationId,
            Year = year,
            StartsOn = startsOn,
            EndsOn = endsOn,
            Status = FiscalYearStatus.Open,
            Notes = notes?.Trim(),
        });
    }

    /// <summary>
    /// A calendar year, which is what most shops run on and what the legacy system assumed.
    /// </summary>
    public static Result<FiscalYear> Calendar(Guid locationId, int year)
        => Create(locationId, year, new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));

    public Result Close(DateTimeOffset at, Guid? closedBy, int archivedRows, decimal archivedNetSales)
    {
        if (Status == FiscalYearStatus.Closed)
        {
            return Result.Failure(AlreadyClosed.With("year", Year));
        }

        Status = FiscalYearStatus.Closed;
        ClosedAt = at;
        ClosedBy = closedBy;
        ArchivedRows = archivedRows;
        ArchivedNetSales = archivedNetSales;
        return Result.Success();
    }

    /// <summary>
    /// Reopens a closed year.
    /// <para>
    /// Possible on purpose: doc 03 asks for an "undo the year-end close" recovery story, and closing
    /// the wrong year an hour before the accountant arrives is a real thing that happens. It is safe
    /// precisely because the close destroys nothing — reopening drops the archive rows and the
    /// checkpoints, and the ledger they were derived from is untouched.
    /// </para>
    /// </summary>
    public Result Reopen()
    {
        if (Status != FiscalYearStatus.Closed)
        {
            return Result.Failure(NotClosed.With("year", Year));
        }

        Status = FiscalYearStatus.Open;
        ClosedAt = null;
        ClosedBy = null;
        ArchivedRows = 0;
        ArchivedNetSales = 0m;
        return Result.Success();
    }
}

/// <summary>
/// One month of one item's trading, frozen at year-end close (guide p.29).
/// <para>
/// This is what the legacy system's "roll monthly to last-year" produced, except that it accumulates
/// rather than overwriting: every closed year keeps its own rows, so "how did this line do three
/// Decembers ago" stays answerable instead of being overwritten each January.
/// </para>
/// <para>
/// Derived from <c>SaleLine</c> rather than from the stock ledger because the ledger records
/// movement, not money — and a year-end report is about what was sold and what it made.
/// </para>
/// </summary>
public sealed class SalesHistoryArchive : Entity
{
    public SalesHistoryArchive()
    {
    }

    public Guid FiscalYearId { get; set; }

    public Guid LocationId { get; set; }

    public int Year { get; set; }

    /// <summary>1–12. Calendar month of the business date, not of the fiscal year's own numbering.</summary>
    public int Month { get; set; }

    public Guid ProductId { get; set; }

    /// <summary>Kept so a deleted or renamed item still reads correctly years later.</summary>
    public string StockCodeSnapshot { get; set; } = string.Empty;

    public string NameSnapshot { get; set; } = string.Empty;

    public Guid? DepartmentId { get; set; }

    public decimal QuantitySold { get; set; }

    /// <summary>Net of discounts and before tax.</summary>
    public decimal NetSales { get; set; }

    public decimal CostOfGoodsSold { get; set; }

    public decimal GrossMargin => NetSales - CostOfGoodsSold;

    public int TransactionCount { get; set; }

    public DateTimeOffset ArchivedAt { get; set; }
}
