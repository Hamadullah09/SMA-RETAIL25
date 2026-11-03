using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>
/// One employee's day: what it was (present, absent, on leave, a holiday, a day off, still to come), the shift it was
/// measured against, the first and last punch, minutes worked, late, left early and over, the leave taken (a day or half
/// a day) and whether an approved correction decided it.
/// </summary>
public sealed record AttendanceDay(Guid EmployeeId, DateOnly Date, AttendanceStatus Status, string? Shift, DateTime? ShiftStart, DateTime? ShiftEnd,
    DateTime? FirstIn, DateTime? LastOut, int WorkedMinutes, int LateMinutes, int EarlyMinutes, int OvertimeMinutes,
    string? Holiday, Guid? LeaveTypeId, string? LeaveCode, decimal LeaveDays, bool LeavePaid, bool Corrected);

/// <summary>An employee on the roster of a week: their usual shift, the week's shift (none: the usual one) and the week's day changes.</summary>
public sealed record RosterRow(Guid EmployeeId, string Code, string Name, Guid? StoreId, Guid? UsualShiftId, Guid? WeekShiftId, IReadOnlyList<RosterDay> Days);

/// <summary>
/// Shifts, the roster, and attendance worked out from the time clock (<see cref="TimeClockEntry"/> of the employee's user):
/// present, late (with the minutes), left early, absent, on leave, holiday, day off, and overtime; plus corrections by
/// hand, each with a reason and approved by a second person. Nothing is stored per day: the days are worked out when
/// asked, so a late punch or an approved leave shows at once.
/// </summary>
public sealed class AttendanceService(DataContext data)
{
    // Shifts ---------------------------------------------------------------------------------------------------------------

    /// <summary>The shifts of the store scope (and those for every store), by name.</summary>
    public async Task<List<Shift>> GetShiftsAsync()
    {
        await using var db = data.Create();
        var q = db.Shifts.AsNoTracking();
        if (data.StoreScope is Guid store) q = q.Where(s => s.StoreId == null || s.StoreId == store);
        return await q.OrderBy(s => s.Name).ToListAsync();
    }

    public async Task<Shift> SaveShiftAsync(Shift shift)
    {
        var name = shift.Name?.Trim() ?? "";
        if (name.Length is 0 or > 60) throw new BusinessRuleException("A shift needs a name of up to 60 characters.");
        if (shift.Days == WorkDays.None) throw new BusinessRuleException("Choose the days the shift is worked.");
        var length = ScheduledShift.On(new DateOnly(2026, 1, 5), shift.StartTime, shift.EndTime, 0, 0);
        int minutes = (int)(length.End - length.Start).TotalMinutes;
        if (shift.BreakMinutes < 0 || shift.BreakMinutes >= minutes) throw new BusinessRuleException("The break must be shorter than the shift.");
        if (shift.GraceMinutes is < 0 or > 240) throw new BusinessRuleException("Grace is 0 to 240 minutes.");
        await using var db = data.Create();
        if (shift.StoreId is Guid store && !await db.Stores.AnyAsync(s => s.Id == store)) throw new BusinessRuleException("That store no longer exists.");
        var row = shift.Id == Guid.Empty ? null : await db.Shifts.SingleOrDefaultAsync(s => s.Id == shift.Id);
        if (row is null) db.Shifts.Add(row = new Shift());
        row.Name = name;
        row.StartTime = new TimeOnly(shift.StartTime.Hour, shift.StartTime.Minute);
        row.EndTime = new TimeOnly(shift.EndTime.Hour, shift.EndTime.Minute);
        row.BreakMinutes = shift.BreakMinutes;
        row.GraceMinutes = shift.GraceMinutes;
        row.Days = shift.Days & WorkDays.Everyday;
        row.StoreId = shift.StoreId;
        row.IsActive = shift.IsActive;
        await db.SaveChangesAsync();
        return row;
    }

    public async Task DeleteShiftAsync(Guid id)
    {
        await using var db = data.Create();
        var row = await db.Shifts.SingleOrDefaultAsync(s => s.Id == id) ?? throw new BusinessRuleException("That shift no longer exists.");
        if (await db.Employees.AnyAsync(e => e.ShiftId == id) || await db.RosterWeeks.AnyAsync(w => w.ShiftId == id) || await db.RosterDays.AnyAsync(d => d.ShiftId == id))
            throw new BusinessRuleException($"{row.Name} is on the roster; make it inactive instead.");
        db.Shifts.Remove(row);
        await db.SaveChangesAsync();
    }

    // Roster ---------------------------------------------------------------------------------------------------------------

    /// <summary>The week (from its Monday) for every employee of the store scope who works in it.</summary>
    public async Task<List<RosterRow>> GetRosterAsync(DateOnly week)
    {
        var start = TimeClock.WeekStart(week);
        var end = start.AddDays(6);
        await using var db = data.Create();
        var employees = await EmployeesAsync(db, start, end, null);
        var ids = employees.Select(e => e.Id).ToList();
        var weeks = await db.RosterWeeks.AsNoTracking().Where(w => w.WeekStart == start && ids.Contains(w.EmployeeId)).ToDictionaryAsync(w => w.EmployeeId, w => w.ShiftId);
        var days = (await db.RosterDays.AsNoTracking().Where(d => d.Date >= start && d.Date <= end && ids.Contains(d.EmployeeId)).ToListAsync())
            .ToLookup(d => d.EmployeeId);
        return [.. employees.Select(e => new RosterRow(e.Id, e.Code, e.FullName, e.StoreId, e.ShiftId, weeks.TryGetValue(e.Id, out var s) ? s : null,
            [.. days[e.Id].OrderBy(d => d.Date)]))];
    }

    /// <summary>Sets the shift of each employee for a week; no shift takes the week back to their usual one.</summary>
    public async Task SetRosterAsync(DateOnly week, IReadOnlyCollection<(Guid EmployeeId, Guid? ShiftId)> assignments)
    {
        var start = TimeClock.WeekStart(week);
        await using var db = data.Create();
        var shifts = await db.Shifts.AsNoTracking().ToDictionaryAsync(s => s.Id);
        var ids = assignments.Select(a => a.EmployeeId).ToList();
        var known = (await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).Select(e => e.Id).ToListAsync()).ToHashSet();
        var rows = await db.RosterWeeks.Where(w => w.WeekStart == start && ids.Contains(w.EmployeeId)).ToDictionaryAsync(w => w.EmployeeId);
        foreach (var (employee, shift) in assignments)
        {
            if (!known.Contains(employee)) throw new BusinessRuleException("An employee on the roster no longer exists.");
            if (shift is Guid sid && (!shifts.TryGetValue(sid, out var s) || !s.IsActive)) throw new BusinessRuleException("A shift on the roster no longer exists or is inactive.");
            rows.TryGetValue(employee, out var row);
            if (shift is null)
            {
                if (row is not null) db.RosterWeeks.Remove(row);
            }
            else if (row is null) db.RosterWeeks.Add(new RosterWeek { EmployeeId = employee, WeekStart = start, ShiftId = shift.Value });
            else row.ShiftId = shift.Value;
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Another shift, or a day off (no shift), for one employee on one day; replaces the day's change if there is one.</summary>
    public async Task<RosterDay> SaveRosterDayAsync(RosterDay day)
    {
        await using var db = data.Create();
        if (!await db.Employees.AnyAsync(e => e.Id == day.EmployeeId)) throw new BusinessRuleException("That employee no longer exists.");
        if (day.ShiftId is Guid sid && !await db.Shifts.AnyAsync(s => s.Id == sid)) throw new BusinessRuleException("That shift no longer exists.");
        if (day.Date == default) throw new BusinessRuleException("Give the day.");
        var row = await db.RosterDays.SingleOrDefaultAsync(d => d.EmployeeId == day.EmployeeId && d.Date == day.Date);
        if (row is null) db.RosterDays.Add(row = new RosterDay { EmployeeId = day.EmployeeId, Date = day.Date });
        row.ShiftId = day.ShiftId;
        row.Note = HrService.Trimmed(day.Note) is { } note ? HrService.Cut(note, 200) : null;
        await db.SaveChangesAsync();
        return row;
    }

    public async Task DeleteRosterDayAsync(Guid id)
    {
        await using var db = data.Create();
        var row = await db.RosterDays.SingleOrDefaultAsync(d => d.Id == id) ?? throw new BusinessRuleException("That change of the roster no longer exists.");
        db.RosterDays.Remove(row);
        await db.SaveChangesAsync();
    }

    public async Task<List<RosterDay>> GetRosterDaysAsync(DateOnly from, DateOnly to)
    {
        await using var db = data.Create();
        var q = db.RosterDays.AsNoTracking().Where(d => d.Date >= from && d.Date <= to);
        if (data.StoreScope is Guid store) q = q.Where(d => db.Employees.Any(e => e.Id == d.EmployeeId && e.StoreId == store));
        return await q.OrderBy(d => d.Date).ToListAsync();
    }

    // Attendance -------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Every day from one date to another of the employees asked for (default: everyone of the store scope employed then),
    /// worked out from the time clock, the roster, holidays, approved leave and approved corrections. Days before an
    /// employee joined or after they left are left out; days from <paramref name="today"/> on without punches are still
    /// to come.
    /// </summary>
    public async Task<List<AttendanceDay>> DaysAsync(DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employeeIds = null, DateOnly? today = null)
    {
        if (to < from) (from, to) = (to, from);
        if (to.DayNumber - from.DayNumber > 400) throw new BusinessRuleException("Attendance is worked out for at most 400 days at a time.");
        await using var db = data.Create();
        var employees = await EmployeesAsync(db, from, to, employeeIds);
        return await DaysAsync(db, employees, from, to, today ?? DateOnly.FromDateTime(DateTime.Now));
    }

    /// <summary>The employees of the store scope (or those asked for) employed at some time between two days.</summary>
    internal async Task<List<Employee>> EmployeesAsync(RetailPlusDbContext db, DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? ids)
    {
        var q = db.Employees.AsNoTracking().Where(e => e.JoiningDate <= to && (e.LeavingDate == null || e.LeavingDate >= from));
        if (ids is not null)
        {
            var list = ids.ToList();
            q = q.Where(e => list.Contains(e.Id));
        }
        else if (data.StoreScope is Guid store) q = q.Where(e => e.StoreId == store);
        return await q.OrderBy(e => e.FullName).ThenBy(e => e.Code).ToListAsync();
    }

    internal static async Task<List<AttendanceDay>> DaysAsync(RetailPlusDbContext db, List<Employee> employees, DateOnly from, DateOnly to, DateOnly today)
    {
        var result = new List<AttendanceDay>();
        if (employees.Count == 0) return result;
        var calendar = await HrCalendar.LoadAsync(db, employees, from.AddDays(-1), to.AddDays(1));
        var start = from.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(2).ToDateTime(TimeOnly.MinValue);
        var users = employees.Where(e => e.UserAccountId is not null).Select(e => e.UserAccountId!.Value).Distinct().ToList();
        var ids = employees.Select(e => e.Id).ToList();
        var entries = new List<TimeClockEntry>();
        var leave = new List<LeaveRequest>();
        var corrections = new List<AttendanceCorrection>();
        foreach (var chunk in users.Chunk(500))
        {
            var list = chunk.ToList();
            entries.AddRange(await db.TimeClockEntries.AsNoTracking()
                .Where(t => list.Contains(t.UserId) && t.ClockIn < end && (t.ClockOut == null || t.ClockOut > start)).ToListAsync());
        }
        foreach (var chunk in ids.Chunk(500))
        {
            var list = chunk.ToList();
            leave.AddRange(await db.LeaveRequests.AsNoTracking()
                .Where(r => list.Contains(r.EmployeeId) && r.Status == LeaveRequestStatus.Approved && r.FromDate <= to && r.ToDate >= from).ToListAsync());
            corrections.AddRange(await db.AttendanceCorrections.AsNoTracking()
                .Where(c => list.Contains(c.EmployeeId) && c.Approval == HrApproval.Approved && c.Date >= from && c.Date <= to).ToListAsync());
        }
        var types = await db.LeaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id);
        var punchesOf = entries.ToLookup(e => e.UserId);
        var leaveOf = leave.ToLookup(r => r.EmployeeId);
        var correctionOf = corrections.GroupBy(c => (c.EmployeeId, c.Date)).ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.DecidedAtUtc).First());

        foreach (var employee in employees)
        {
            var rules = calendar.RulesOf(employee);
            int overtimeMinimum = rules?.OvertimeMinimumMinutes ?? 30;
            int standard = calendar.StandardMinutes(employee);
            var plans = new Dictionary<DateOnly, (DayPlan Plan, ScheduledShift? Shift)>();
            for (var d = from.AddDays(-1); d <= to.AddDays(1); d = d.AddDays(1))
            {
                var plan = calendar.PlanOn(employee, d);
                plans[d] = (plan, plan.Shift is { } s ? ScheduledShift.On(d, s.StartTime, s.EndTime, s.BreakMinutes, s.GraceMinutes) : null);
            }
            DateTime Begins(DateOnly d) => AttendanceRules.DayBegins(d, plans.TryGetValue(d, out var p) ? p.Shift : null);

            // Each shift punched belongs to the day whose beginning comes last before it was punched in.
            var byDay = new Dictionary<DateOnly, List<TimeClockEntry>>();
            if (employee.UserAccountId is Guid user)
                foreach (var entry in punchesOf[user])
                {
                    var day = DateOnly.FromDateTime(entry.ClockIn);
                    if (entry.ClockIn < Begins(day)) day = day.AddDays(-1);
                    else if (entry.ClockIn >= Begins(day.AddDays(1))) day = day.AddDays(1);
                    (byDay.TryGetValue(day, out var list) ? list : byDay[day] = []).Add(entry);
                }

            for (var d = from; d <= to; d = d.AddDays(1))
            {
                if (!employee.IsEmployedOn(d)) continue;
                var (plan, shift) = plans[d];
                var punched = byDay.GetValueOrDefault(d) ?? [];
                var work = AttendanceRules.Evaluate(shift, plan.Expected, standard,
                    punched.Where(p => p.ClockOut is not null).Select(p => (p.ClockIn, p.ClockOut!.Value)), overtimeMinimum);
                bool openPunch = punched.Any(p => p.ClockOut is null);
                var firstIn = work.FirstIn ?? (openPunch ? punched.Where(p => p.ClockOut is null).Min(p => p.ClockIn) : null);
                int late = work.LateMinutes;
                if (work.FirstIn is null && openPunch && plan.Expected && shift is not null)
                {
                    int by = (int)(firstIn!.Value - shift.Start).TotalMinutes;
                    late = by > shift.GraceMinutes ? by : 0;
                }

                // Approved leave on a day it counts for.
                LeaveRequest? request = null;
                LeaveType? type = null;
                decimal leaveDays = 0;
                foreach (var r in leaveOf[employee.Id])
                {
                    if (r.FromDate > d || r.ToDate < d || !types.TryGetValue(r.LeaveTypeId, out var t)) continue;
                    if (!calendar.CountsForLeave(employee, t, d)) continue;
                    request = r;
                    type = t;
                    leaveDays = (d == r.FromDate && r.FirstDayHalf) || (d == r.ToDate && r.LastDayHalf) ? 0.5m : 1m;
                    break;
                }

                bool present = work.WorkedMinutes > 0 || openPunch;
                AttendanceStatus status;
                int worked = work.WorkedMinutes;
                if (plan.Holiday is not null && type is null) status = AttendanceStatus.Holiday;
                else if (!plan.Working && type is null) status = AttendanceStatus.Off;
                else if (leaveDays == 1m) status = AttendanceStatus.Leave;
                else if (present) status = AttendanceStatus.Present;
                else if (leaveDays > 0) status = AttendanceStatus.Leave;
                else if (d >= today) status = AttendanceStatus.Scheduled;
                else if (!employee.TracksAttendance)
                {
                    status = AttendanceStatus.Present;
                    worked = shift?.ScheduledMinutes ?? standard;
                }
                else status = AttendanceStatus.Absent;
                bool atWork = status == AttendanceStatus.Present;
                var day = new AttendanceDay(employee.Id, d, status, plan.Shift?.Name, shift?.Start, shift?.End, firstIn, work.LastOut, worked,
                    atWork ? late : 0, atWork ? work.EarlyMinutes : 0, work.OvertimeMinutes, plan.Holiday, type?.Id, type?.Code, leaveDays, type?.IsPaid ?? false, false);
                if (correctionOf.TryGetValue((employee.Id, d), out var c))
                    day = day with
                    {
                        Status = c.Status, WorkedMinutes = c.WorkedMinutes, LateMinutes = c.LateMinutes, EarlyMinutes = c.EarlyMinutes,
                        OvertimeMinutes = c.OvertimeMinutes, Corrected = true,
                        LeaveDays = c.Status == AttendanceStatus.Leave ? Math.Max(day.LeaveDays, 1m) : day.LeaveDays,
                    };
                result.Add(day);
            }
        }
        return result;
    }

    // Corrections ----------------------------------------------------------------------------------------------------------

    public async Task<List<AttendanceCorrection>> GetCorrectionsAsync(DateOnly? from = null, DateOnly? to = null, HrApproval? approval = null, Guid? employeeId = null)
    {
        await using var db = data.Create();
        var q = db.AttendanceCorrections.AsNoTracking();
        if (from is { } f) q = q.Where(c => c.Date >= f);
        if (to is { } t) q = q.Where(c => c.Date <= t);
        if (approval is { } a) q = q.Where(c => c.Approval == a);
        if (employeeId is Guid id) q = q.Where(c => c.EmployeeId == id);
        if (data.StoreScope is Guid store) q = q.Where(c => db.Employees.Any(e => e.Id == c.EmployeeId && e.StoreId == store));
        return await q.OrderByDescending(c => c.Date).ThenBy(c => c.CreatedAtUtc).ToListAsync();
    }

    /// <summary>
    /// Asks for a day's attendance to be corrected, with a reason; it counts once approved. A correction still waiting for
    /// the same day is replaced. Needs <see cref="Permissions.HrAttendance"/>.
    /// </summary>
    public async Task<AttendanceCorrection> RequestCorrectionAsync(AttendanceCorrection correction, SessionInfo user)
    {
        if (!user.Can(Permissions.HrAttendance)) throw new BusinessRuleException($"{user.UserName} may not correct attendance.");
        if (string.IsNullOrWhiteSpace(correction.Reason)) throw new BusinessRuleException("Say why the day is corrected.");
        if (correction.Date > DateOnly.FromDateTime(DateTime.Today)) throw new BusinessRuleException("A day still to come cannot be corrected.");
        if (correction.Status == AttendanceStatus.Scheduled) throw new BusinessRuleException("A corrected day says what it was.");
        foreach (var minutes in new[] { correction.WorkedMinutes, correction.LateMinutes, correction.EarlyMinutes, correction.OvertimeMinutes })
            if (minutes is < 0 or > 1440) throw new BusinessRuleException("Minutes are 0 to 1,440 a day.");
        await using var db = data.Create();
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == correction.EmployeeId) ?? throw new BusinessRuleException("That employee no longer exists.");
        if (!employee.IsEmployedOn(correction.Date)) throw new BusinessRuleException($"{employee.FullName} did not work for the company on {correction.Date:d}.");
        var row = await db.AttendanceCorrections.FirstOrDefaultAsync(c => c.EmployeeId == correction.EmployeeId && c.Date == correction.Date && c.Approval == HrApproval.Pending);
        if (row is null) db.AttendanceCorrections.Add(row = new AttendanceCorrection { EmployeeId = correction.EmployeeId, Date = correction.Date });
        bool present = correction.Status == AttendanceStatus.Present;
        row.Status = correction.Status;
        row.WorkedMinutes = correction.WorkedMinutes;
        row.LateMinutes = present ? correction.LateMinutes : 0;
        row.EarlyMinutes = present ? correction.EarlyMinutes : 0;
        row.OvertimeMinutes = correction.OvertimeMinutes;
        row.Reason = HrService.Cut(correction.Reason.Trim(), 200);
        row.Approval = HrApproval.Pending;
        row.RequestedBy = HrService.Cut(user.UserName, 50);
        row.RequestedById = user.UserId;
        db.Audit(AuditActions.AttendanceCorrection, $"{employee.Code} {employee.FullName}, {correction.Date.ToString("d", CultureInfo.CurrentCulture)}: {row.Status} asked, {row.Reason}",
            nameof(AttendanceCorrection), row.Id);
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>Approves or rejects a correction. Someone other than who asked for it decides, with <see cref="Permissions.HrAttendance"/>.</summary>
    public async Task<AttendanceCorrection> DecideCorrectionAsync(Guid id, bool approve, string? note, SessionInfo user)
    {
        if (!user.Can(Permissions.HrAttendance)) throw new BusinessRuleException($"{user.UserName} may not approve attendance corrections.");
        await using var db = data.Create();
        var row = await db.AttendanceCorrections.SingleOrDefaultAsync(c => c.Id == id) ?? throw new BusinessRuleException("That correction no longer exists.");
        if (row.Approval != HrApproval.Pending) throw new BusinessRuleException($"That correction was {row.Approval.ToString().ToLowerInvariant()} already.");
        if (row.RequestedById is Guid asker && asker == user.UserId) throw new BusinessRuleException("Someone else must decide on a correction you asked for.");
        var employee = await db.Employees.AsNoTracking().Where(e => e.Id == row.EmployeeId).Select(e => e.Code + " " + e.FullName).SingleAsync();
        row.Approval = approve ? HrApproval.Approved : HrApproval.Rejected;
        row.DecidedBy = HrService.Cut(user.UserName, 50);
        row.DecidedById = user.UserId;
        row.DecidedAtUtc = SyncColumns.Now();
        row.DecisionNote = HrService.Trimmed(note) is { } text ? HrService.Cut(text, 200) : null;
        db.Audit(AuditActions.AttendanceCorrection, $"{employee}, {row.Date.ToString("d", CultureInfo.CurrentCulture)}: {row.Approval}", nameof(AttendanceCorrection), row.Id);
        await db.SaveChangesAsync();
        return row;
    }
}
