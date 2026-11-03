using RetailPlus.Core.Model;

namespace RetailPlus.Core.Rules;

/// <summary>A shift as worked on one day: when it starts and ends (the end may be the next morning).</summary>
public sealed record ScheduledShift(DateTime Start, DateTime End, int BreakMinutes, int GraceMinutes)
{
    /// <summary>The minutes the shift expects, its break left out.</summary>
    public int ScheduledMinutes => Math.Max(0, (int)(End - Start).TotalMinutes - BreakMinutes);

    /// <summary>The shift on <paramref name="day"/>; an end at or before the start is the next day.</summary>
    public static ScheduledShift On(DateOnly day, TimeOnly start, TimeOnly end, int breakMinutes, int graceMinutes)
    {
        var from = day.ToDateTime(start);
        var to = day.ToDateTime(end);
        if (to <= from) to = to.AddDays(1);
        return new ScheduledShift(from, to, breakMinutes, graceMinutes);
    }
}

/// <summary>What the time clock says of one day: minutes worked, late, early and over, and the first and last punch.</summary>
public sealed record DayWork(int WorkedMinutes, int LateMinutes, int EarlyMinutes, int OvertimeMinutes, DateTime? FirstIn, DateTime? LastOut)
{
    public static DayWork None { get; } = new(0, 0, 0, 0, null, null);
}

/// <summary>
/// Attendance from time clock shifts: which day a shift belongs to, lateness and leaving early against the day's shift
/// with its grace minutes, overtime beyond the shift (all of it on a day off or a holiday), and the days a leave request
/// takes.
/// </summary>
public static class AttendanceRules
{
    /// <summary>Hours before a shift starts from which a punch counts for that shift's day.</summary>
    public const int EarlyPunchHours = 4;

    /// <summary>
    /// Where a day begins for the time clock: <see cref="EarlyPunchHours"/> before its shift, or midnight without one. A
    /// punch belongs to the day whose beginning is the last one before it, so a night shift stays on the day it started.
    /// </summary>
    public static DateTime DayBegins(DateOnly day, ScheduledShift? shift) =>
        shift is null ? day.ToDateTime(TimeOnly.MinValue) : shift.Start.AddHours(-EarlyPunchHours);

    /// <summary>
    /// Works out a day from the shifts punched on it. <paramref name="workingDay"/> false (a day off or a holiday) makes all
    /// the time worked overtime; a working day without shift times expects <paramref name="standardMinutes"/>. One long
    /// punch without a break has the shift's break taken off; overtime below <paramref name="overtimeMinimum"/> minutes
    /// does not count.
    /// </summary>
    public static DayWork Evaluate(ScheduledShift? shift, bool workingDay, int standardMinutes, IEnumerable<(DateTime In, DateTime Out)> punched, int overtimeMinimum)
    {
        var merged = TimeClock.Merge(punched);
        if (merged.Count == 0) return DayWork.None;
        int worked = merged.Sum(m => (int)(m.Out - m.In).TotalMinutes);
        if (merged.Count == 1 && shift is not null && shift.BreakMinutes > 0 && worked > shift.BreakMinutes + 240)
            worked -= shift.BreakMinutes;
        var firstIn = merged[0].In;
        var lastOut = merged[^1].Out;
        int late = 0, early = 0;
        if (workingDay && shift is not null)
        {
            int lateBy = (int)(firstIn - shift.Start).TotalMinutes;
            if (lateBy > shift.GraceMinutes) late = lateBy;
            int earlyBy = (int)(shift.End - lastOut).TotalMinutes;
            if (earlyBy > shift.GraceMinutes) early = earlyBy;
        }
        int expected = !workingDay ? 0 : shift?.ScheduledMinutes ?? standardMinutes;
        int over = worked - expected;
        if (over < Math.Max(1, overtimeMinimum)) over = 0;
        return new DayWork(Math.Max(0, worked), late, early, over, firstIn, lastOut);
    }

    /// <summary>True when the shift days include the day of the week.</summary>
    public static bool Works(WorkDays days, DayOfWeek day) => (days & Flag(day)) != 0;

    public static WorkDays Flag(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => WorkDays.Monday,
        DayOfWeek.Tuesday => WorkDays.Tuesday,
        DayOfWeek.Wednesday => WorkDays.Wednesday,
        DayOfWeek.Thursday => WorkDays.Thursday,
        DayOfWeek.Friday => WorkDays.Friday,
        DayOfWeek.Saturday => WorkDays.Saturday,
        _ => WorkDays.Sunday,
    };

    /// <summary>The first <paramref name="count"/> days of the week from Monday: the working week of a rule set (6 is Monday to Saturday).</summary>
    public static WorkDays FirstDays(int count)
    {
        var days = WorkDays.None;
        for (int i = 0; i < Math.Clamp(count, 0, 7); i++) days |= (WorkDays)(1 << i);
        return days;
    }

    /// <summary>
    /// The days a leave request takes: the days from one date to another that count (working days, or every day), less
    /// half a day for a half first or last day. A one-day request with a half day is half a day.
    /// </summary>
    public static decimal LeaveDays(DateOnly from, DateOnly to, bool firstDayHalf, bool lastDayHalf, Func<DateOnly, bool> counts)
    {
        if (to < from) return 0;
        decimal days = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (!counts(d)) continue;
            bool half = (d == from && firstDayHalf) || (d == to && lastDayHalf);
            days += half ? 0.5m : 1m;
        }
        return days;
    }
}
