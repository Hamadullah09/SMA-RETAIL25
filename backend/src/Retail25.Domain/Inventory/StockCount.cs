using Retail25.Domain.Common;

namespace Retail25.Domain.Inventory;

public enum StockCountStatus
{
    InProgress = 0,
    Posted = 1,
    Cancelled = 2,
}

/// <summary>
/// A stock-count session (guide p.22). Used for batch on-hand adjustments from a CSV or
/// manual count. Variance report generated after posting.
/// <para>
/// Counting and posting are deliberately separate. A count is gathered over hours by people with
/// clipboards; nothing moves until someone looks at the variances and decides they are real.
/// </para>
/// </summary>
public sealed class StockCount : AggregateRoot, IAuditable
{
    public static readonly Error NotInProgress = new(
        "count.not_in_progress",
        "This count has already been posted or cancelled.");

    public static readonly Error NothingCounted = new(
        "count.nothing_counted",
        "Nothing has been counted yet.");

    private StockCount()
    {
    }

    public long CountNumber { get; set; }

    public long LocationId { get; set; }

    public StockCountStatus Status { get; set; } = StockCountStatus.InProgress;

    public string? Notes { get; set; }

    /// <summary>Optional narrowing — a count of one department rather than the whole shop.</summary>
    public long? DepartmentId { get; set; }

    public DateTimeOffset? PostedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public static StockCount Start(long locationId, long countNumber, long? departmentId = null, string? notes = null)
        => new()
        {
            CountNumber = countNumber,
            LocationId = locationId,
            DepartmentId = departmentId,
            Status = StockCountStatus.InProgress,
            Notes = notes?.Trim(),
        };

    public Result EnsureOpen() => Status == StockCountStatus.InProgress ? Result.Success() : Result.Failure(NotInProgress);

    public Result Post(DateTimeOffset at, bool hasLines)
    {
        if (Status != StockCountStatus.InProgress)
        {
            return Result.Failure(NotInProgress);
        }

        if (!hasLines)
        {
            return Result.Failure(NothingCounted);
        }

        Status = StockCountStatus.Posted;
        PostedAt = at;
        return Result.Success();
    }

    public Result Cancel()
    {
        if (Status != StockCountStatus.InProgress)
        {
            return Result.Failure(NotInProgress);
        }

        Status = StockCountStatus.Cancelled;
        return Result.Success();
    }
}

/// <summary>
/// One counted item.
/// <para>
/// <see cref="SystemQtyAtCount"/> is the on-hand figure at the moment the line was entered, not at
/// the moment the count is posted. That is what makes the variance meaningful: it is the difference
/// between what the system believed and what the person with the clipboard saw, and re-reading
/// on-hand at posting time would silently absorb every sale that happened while the count was
/// running.
/// </para>
/// </summary>
public sealed class StockCountLine : Entity, IAuditable
{
    public static readonly Error NegativeCount = new(
        "count.negative_quantity",
        "A counted quantity cannot be negative.");

    public StockCountLine()
    {
    }

    public long StockCountId { get; set; }

    public long ProductId { get; set; }

    public long? VariantId { get; set; }

    public string StockCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public decimal CountedQty { get; set; }

    public decimal SystemQtyAtCount { get; set; }

    /// <summary>Frozen so the variance can be valued even if the item is repriced afterwards.</summary>
    public decimal UnitCost { get; set; }

    public string? Notes { get; set; }

    public decimal Variance => CountedQty - SystemQtyAtCount;

    public decimal VarianceValue => Variance * UnitCost;

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public static Result<StockCountLine> Create(
        long countId, long productId, string stockCode, string productName,
        decimal countedQty, decimal systemQty, decimal unitCost, string? notes = null)
    {
        if (countedQty < 0m)
        {
            return Result.Failure<StockCountLine>(NegativeCount.With("counted", countedQty));
        }

        return Result.Success(new StockCountLine
        {
            StockCountId = countId,
            ProductId = productId,
            StockCode = stockCode,
            ProductName = productName,
            CountedQty = countedQty,
            SystemQtyAtCount = systemQty,
            UnitCost = unitCost,
            Notes = notes?.Trim(),
        });
    }

    /// <summary>
    /// Re-counting an item already on the sheet replaces the figure rather than adding a second row
    /// — two people counting the same shelf is a correction, not two shelves.
    /// </summary>
    public Result Recount(decimal countedQty, decimal systemQty, string? notes)
    {
        if (countedQty < 0m)
        {
            return Result.Failure(NegativeCount.With("counted", countedQty));
        }

        CountedQty = countedQty;
        SystemQtyAtCount = systemQty;
        Notes = notes?.Trim() ?? Notes;
        return Result.Success();
    }
}
