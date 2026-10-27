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

    /// <summary>The Monday a day's week starts on.</summary>
    public static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
}
