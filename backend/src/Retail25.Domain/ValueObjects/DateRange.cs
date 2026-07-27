using Retail25.Domain.Common;

namespace Retail25.Domain.ValueObjects;

/// <summary>
/// An inclusive range of business dates. Used for sale-pricing windows, report periods,
/// effective-dated tax configuration and staff hour queries.
/// </summary>
public readonly record struct DateRange
{
    public static readonly Error Inverted = new("date_range.inverted", "The end date cannot precede the start date.");

    private DateRange(DateOnly start, DateOnly end)
    {
        Start = start;
        End = end;
    }

    public DateOnly Start { get; }

    public DateOnly End { get; }

    public int DayCount => End.DayNumber - Start.DayNumber + 1;

    public static Result<DateRange> Create(DateOnly start, DateOnly end)
        => end < start
            ? Result.Failure<DateRange>(Inverted.With("start", start).With("end", end))
            : Result.Success(new DateRange(start, end));

    /// <summary>Inclusive on both ends, matching how a retailer reads "on sale 1st to 7th".</summary>
    public bool Contains(DateOnly date) => date >= Start && date <= End;

    public bool Overlaps(DateRange other) => Start <= other.End && other.Start <= End;

    public override string ToString() => $"{Start:yyyy-MM-dd}..{End:yyyy-MM-dd}";
}
