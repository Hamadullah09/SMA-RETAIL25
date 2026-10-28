namespace RetailPlus.Core.Rules;

/// <summary>Hours worked from time clock entries.</summary>
public static class TimeClock
{
    /// <summary>Hours between two times, to the hundredth of an hour.</summary>
    public static decimal Hours(DateTime from, DateTime to) =>
        to <= from ? 0m : Math.Round((decimal)(to - from).TotalHours, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The hours of a shift on each day it touches: a shift from 22:00 to 02:00 is two hours on each day. Days without
    /// hours are left out.
    /// </summary>
    public static IReadOnlyList<(DateOnly Day, decimal Hours)> SplitByDay(DateTime clockIn, DateTime clockOut)
    {
        var result = new List<(DateOnly, decimal)>();
        var start = clockIn;
        while (start < clockOut)
        {
            var midnight = start.Date.AddDays(1);
            var end = clockOut < midnight ? clockOut : midnight;
            var hours = Hours(start, end);
            if (hours > 0) result.Add((DateOnly.FromDateTime(start), hours));
            start = end;
        }
        return result;
    }

    /// <summary>
    /// The time a user was in, from their shifts: shifts that overlap (two tills that each punched the user in while
    /// offline) count once; shifts that only meet stay apart, so the hours of shifts that do not overlap are unchanged.
    /// </summary>
    public static IReadOnlyList<(DateTime In, DateTime Out)> Merge(IEnumerable<(DateTime In, DateTime Out)> shifts)
    {
        var merged = new List<(DateTime In, DateTime Out)>();
        foreach (var shift in shifts.Where(s => s.Out > s.In).OrderBy(s => s.In))
        {
            if (merged.Count > 0 && shift.In < merged[^1].Out)
            {
                if (shift.Out > merged[^1].Out) merged[^1] = (merged[^1].In, shift.Out);
            }
            else merged.Add(shift);
        }
        return merged;
    }

    /// <summary>The Monday a day's week starts on.</summary>
    public static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
}
