using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>
/// An employee's leave of one type in one year: the year's entitlement (for part of a year, its share), what has been
/// earned so far (for leave earned month by month), days carried in from the year before, adjustments by hand, days
/// taken (approved) and waiting for approval, and what is still available (null for leave without a limit).
/// </summary>
public sealed record LeaveBalance(Guid EmployeeId, Guid LeaveTypeId, string Code, string Name, bool Paid, bool Limited, decimal Entitled, decimal Earned,
    decimal CarriedIn, decimal Adjusted, decimal Taken, decimal Pending, decimal? Available);

/// <summary>What the leave request list is filtered by.</summary>
public sealed record LeaveQuery(LeaveRequestStatus? Status = null, Guid? EmployeeId = null, DateOnly? From = null, DateOnly? To = null);

/// <summary>
/// Leave: requests with half days, their approval by the employee's manager or by someone with <c>hr.leave</c>, balances
/// per year (entitlement, monthly earning, carry-over and its limit, adjustments), and what is left to pay out when someone
/// leaves. Leave years are calendar years. Approved leave shows in attendance; unpaid leave is taken off pay.
/// </summary>
public sealed class LeaveService(DataContext data)
{
    public async Task<List<LeaveRequest>> GetRequestsAsync(LeaveQuery? query = null)
    {
        query ??= new LeaveQuery();
        await using var db = data.Create();
        var q = db.LeaveRequests.AsNoTracking();
        if (query.Status is { } status) q = q.Where(r => r.Status == status);
        if (query.EmployeeId is Guid id) q = q.Where(r => r.EmployeeId == id);
        if (query.From is { } from) q = q.Where(r => r.ToDate >= from);
        if (query.To is { } to) q = q.Where(r => r.FromDate <= to);
        if (data.StoreScope is Guid store) q = q.Where(r => db.Employees.Any(e => e.Id == r.EmployeeId && e.StoreId == store));
        return await q.OrderByDescending(r => r.FromDate).ThenBy(r => r.CreatedAtUtc).ToListAsync();
    }

    /// <summary>
    /// Asks for leave. The days are counted (working days, or every day for leave that counts calendar days); the request
    /// must fit the employee (gender, service), not overlap other leave, and stay within what is available.
    /// </summary>
    public async Task<LeaveRequest> RequestAsync(LeaveRequest request, SessionInfo user)
    {
        if (request.ToDate < request.FromDate) throw new BusinessRuleException("The leave ends before it starts.");
        if (request.ToDate.DayNumber - request.FromDate.DayNumber > 366) throw new BusinessRuleException("A request is for at most a year.");
        if (request.FromDate == request.ToDate && request.FirstDayHalf && request.LastDayHalf) throw new BusinessRuleException("A one-day request is a whole day or a half day.");
        await using var db = data.Create();
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == request.EmployeeId) ?? throw new BusinessRuleException("That employee no longer exists.");
        var type = await db.LeaveTypes.AsNoTracking().SingleOrDefaultAsync(t => t.Id == request.LeaveTypeId) ?? throw new BusinessRuleException("That leave type no longer exists.");
        var calendar = await HrCalendar.LoadAsync(db, [employee], request.FromDate, request.ToDate);
        CheckFits(employee, type, calendar, request.FromDate, request.ToDate);
        decimal days = AttendanceRules.LeaveDays(request.FromDate, request.ToDate, request.FirstDayHalf, request.LastDayHalf, d => calendar.CountsForLeave(employee, type, d));
        if (days <= 0) throw new BusinessRuleException("Those days are not working days, so there is no leave to take.");
        if (await db.LeaveRequests.AnyAsync(r => r.EmployeeId == employee.Id && (r.Status == LeaveRequestStatus.Pending || r.Status == LeaveRequestStatus.Approved)
                && r.FromDate <= request.ToDate && r.ToDate >= request.FromDate))
            throw new BusinessRuleException($"{employee.FullName} already has leave asked for or approved on some of those days.");
        var row = new LeaveRequest
        {
            EmployeeId = employee.Id, LeaveTypeId = type.Id, FromDate = request.FromDate, ToDate = request.ToDate, FirstDayHalf = request.FirstDayHalf,
            LastDayHalf = request.LastDayHalf && request.ToDate != request.FromDate, Days = days, Reason = HrService.Trimmed(request.Reason) is { } reason ? HrService.Cut(reason, 200) : null,
            Status = LeaveRequestStatus.Pending, RequestedBy = HrService.Cut(user.UserName, 50), RequestedById = user.UserId,
        };
        if (row.FromDate == row.ToDate && request.LastDayHalf && !request.FirstDayHalf) row.FirstDayHalf = true;
        await CheckBalanceAsync(db, employee, type, row, null);
        db.LeaveRequests.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    private static void CheckFits(Employee employee, LeaveType type, HrCalendar calendar, DateOnly from, DateOnly to)
    {
        if (!type.IsActive) throw new BusinessRuleException($"{type.Name} is no longer in use.");
        if (type.Country != calendar.CountryOf(employee)) throw new BusinessRuleException($"{type.Name} is a leave type of {type.Country}, not of {employee.FullName}'s country.");
        if (type.OnlyFor is { } gender && employee.Gender != gender) throw new BusinessRuleException($"{type.Name} is for {gender.ToString().ToLowerInvariant()} employees.");
        if (!employee.IsEmployedOn(from) || !employee.IsEmployedOn(to)) throw new BusinessRuleException($"{employee.FullName} does not work for the company on all of those days.");
        if (type.MinimumServiceMonths > 0 && PayrollRules.CompletedMonths(employee.JoiningDate, from.AddDays(-1)) < type.MinimumServiceMonths)
            throw new BusinessRuleException($"{type.Name} may be taken after {type.MinimumServiceMonths} months of service.");
    }

    /// <summary>Refuses a request that takes more than is available in any year it falls in (leave without a limit is never refused).</summary>
    private static async Task CheckBalanceAsync(RetailPlusDbContext db, Employee employee, LeaveType type, LeaveRequest request, Guid? except)
    {
        if (type.DaysPerYear <= 0) return;
        for (int year = request.FromDate.Year; year <= request.ToDate.Year; year++)
        {
            var yearEnd = new DateOnly(year, 12, 31);
            var last = request.ToDate < yearEnd ? request.ToDate : yearEnd;
            var balances = await BalancesAsync(db, [employee], [type], year, last, except ?? request.Id);
            var balance = balances.Single();
            var portion = await PortionAsync(db, employee, type, request, year);
            if (portion > (balance.Available ?? decimal.MaxValue))
                throw new BusinessRuleException($"{employee.FullName} has {balance.Available ?? 0:0.#} days of {type.Name} available in {year}; the request takes {portion:0.#}.");
        }
    }

    /// <summary>The days of a request that fall in one year.</summary>
    private static async Task<decimal> PortionAsync(RetailPlusDbContext db, Employee employee, LeaveType type, LeaveRequest request, int year)
    {
        if (request.FromDate.Year == year && request.ToDate.Year == year) return request.Days;
        var from = request.FromDate.Year == year ? request.FromDate : new DateOnly(year, 1, 1);
        var to = request.ToDate.Year == year ? request.ToDate : new DateOnly(year, 12, 31);
        if (to < from) return 0;
        var calendar = await HrCalendar.LoadAsync(db, [employee], from, to);
        return AttendanceRules.LeaveDays(from, to, request.FirstDayHalf && from == request.FromDate, request.LastDayHalf && to == request.ToDate,
            d => calendar.CountsForLeave(employee, type, d));
    }

    /// <summary>
    /// Approves or rejects a request. The employee's manager (their user) or someone with <see cref="Permissions.HrLeave"/>
    /// decides, never the employee themselves; approval checks the balance again.
    /// </summary>
    public async Task<LeaveRequest> DecideAsync(Guid id, bool approve, string? note, SessionInfo user)
    {
        await using var db = data.Create();
        var row = await db.LeaveRequests.SingleOrDefaultAsync(r => r.Id == id) ?? throw new BusinessRuleException("That leave request no longer exists.");
        if (row.Status != LeaveRequestStatus.Pending) throw new BusinessRuleException($"That request was {row.Status.ToString().ToLowerInvariant()} already.");
        var employee = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == row.EmployeeId);
        if (!await MayDecideAsync(db, employee, user)) throw new BusinessRuleException($"{user.UserName} may not decide on {employee.FullName}'s leave.");
        var type = await db.LeaveTypes.AsNoTracking().SingleAsync(t => t.Id == row.LeaveTypeId);
        if (approve) await CheckBalanceAsync(db, employee, type, row, row.Id);
        row.Status = approve ? LeaveRequestStatus.Approved : LeaveRequestStatus.Rejected;
        row.DecidedBy = HrService.Cut(user.UserName, 50);
        row.DecidedById = user.UserId;
        row.DecidedAtUtc = SyncColumns.Now();
        row.DecisionNote = HrService.Trimmed(note) is { } text ? HrService.Cut(text, 200) : null;
        db.Audit(AuditActions.LeaveDecision, $"{employee.Code} {employee.FullName}: {type.Name} {Days(row)}, {row.Status}", nameof(LeaveRequest), row.Id);
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>True when the user may decide on the employee's leave: with <c>hr.leave</c>, or as the user of the employee's manager; never their own.</summary>
    public async Task<bool> MayDecideAsync(Guid employeeId, SessionInfo user)
    {
        await using var db = data.Create();
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == employeeId);
        return employee is not null && await MayDecideAsync(db, employee, user);
    }

    private static async Task<bool> MayDecideAsync(RetailPlusDbContext db, Employee employee, SessionInfo user)
    {
        if (employee.UserAccountId is Guid own && own == user.UserId) return false;
        if (user.Can(Permissions.HrLeave)) return true;
        if (employee.ManagerId is not Guid manager || user.UserId is not Guid me) return false;
        return await db.Employees.AnyAsync(e => e.Id == manager && e.UserAccountId == me);
    }

    /// <summary>Cancels a request still waiting or already approved; the employee's manager, <c>hr.leave</c> or the one who asked may.</summary>
    public async Task<LeaveRequest> CancelAsync(Guid id, SessionInfo user)
    {
        await using var db = data.Create();
        var row = await db.LeaveRequests.SingleOrDefaultAsync(r => r.Id == id) ?? throw new BusinessRuleException("That leave request no longer exists.");
        if (row.Status is not (LeaveRequestStatus.Pending or LeaveRequestStatus.Approved)) throw new BusinessRuleException($"That request was {row.Status.ToString().ToLowerInvariant()} already.");
        var employee = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == row.EmployeeId);
        bool asker = row.RequestedById is Guid by && by == user.UserId;
        if (!asker && !await MayDecideAsync(db, employee, user)) throw new BusinessRuleException($"{user.UserName} may not cancel {employee.FullName}'s leave.");
        bool wasApproved = row.Status == LeaveRequestStatus.Approved;
        row.Status = LeaveRequestStatus.Cancelled;
        row.DecidedBy = HrService.Cut(user.UserName, 50);
        row.DecidedById = user.UserId;
        row.DecidedAtUtc = SyncColumns.Now();
        if (wasApproved) db.Audit(AuditActions.LeaveDecision, $"{employee.Code} {employee.FullName}: approved leave {Days(row)} cancelled", nameof(LeaveRequest), row.Id);
        await db.SaveChangesAsync();
        return row;
    }

    private static string Days(LeaveRequest r) =>
        r.FromDate == r.ToDate ? r.FromDate.ToString("d", CultureInfo.CurrentCulture) : $"{r.FromDate.ToString("d", CultureInfo.CurrentCulture)} to {r.ToDate.ToString("d", CultureInfo.CurrentCulture)}";

    /// <summary>Adds days to (or, negative, takes them from) an employee's balance of a year, with a reason. Needs <see cref="Permissions.HrLeave"/>.</summary>
    public async Task<LeaveAdjustment> AdjustAsync(LeaveAdjustment adjustment, SessionInfo user)
    {
        if (!user.Can(Permissions.HrLeave)) throw new BusinessRuleException($"{user.UserName} may not change leave balances.");
        if (adjustment.Days == 0 || Math.Abs(adjustment.Days) > 366) throw new BusinessRuleException("An adjustment adds or takes away 0.5 to 366 days.");
        if (string.IsNullOrWhiteSpace(adjustment.Reason)) throw new BusinessRuleException("Say why the balance changes.");
        if (adjustment.Year is < 2000 or > 2100) throw new BusinessRuleException("Give the year of the balance.");
        await using var db = data.Create();
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == adjustment.EmployeeId) ?? throw new BusinessRuleException("That employee no longer exists.");
        var type = await db.LeaveTypes.AsNoTracking().SingleOrDefaultAsync(t => t.Id == adjustment.LeaveTypeId) ?? throw new BusinessRuleException("That leave type no longer exists.");
        var row = new LeaveAdjustment
        {
            EmployeeId = employee.Id, LeaveTypeId = type.Id, Year = adjustment.Year, Days = Math.Round(adjustment.Days, 2), Reason = HrService.Cut(adjustment.Reason.Trim(), 200),
            MadeBy = HrService.Cut(user.UserName, 50),
        };
        db.LeaveAdjustments.Add(row);
        db.Audit(AuditActions.LeaveDecision, $"{employee.Code} {employee.FullName}: {type.Name} {adjustment.Year} {row.Days:+0.##;-0.##} days, {row.Reason}", nameof(LeaveAdjustment), row.Id);
        await db.SaveChangesAsync();
        return row;
    }

    public async Task<List<LeaveAdjustment>> GetAdjustmentsAsync(Guid employeeId)
    {
        await using var db = data.Create();
        return await db.LeaveAdjustments.AsNoTracking().Where(a => a.EmployeeId == employeeId).OrderByDescending(a => a.Year).ThenByDescending(a => a.CreatedAtUtc).ToListAsync();
    }

    // Balances ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Every employee's balances of a year (those of the store scope, or the ones asked for), for the active leave types
    /// of their country that apply to them, as of <paramref name="asOf"/> (default: today, or the year's end for a past year).
    /// </summary>
    public async Task<List<LeaveBalance>> BalancesAsync(int year, IReadOnlyCollection<Guid>? employeeIds = null, DateOnly? asOf = null)
    {
        await using var db = data.Create();
        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);
        var employees = await new AttendanceService(data).EmployeesAsync(db, yearStart, yearEnd, employeeIds);
        var types = await db.LeaveTypes.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var day = asOf ?? (today < yearStart ? yearStart : today > yearEnd ? yearEnd : today);
        return await BalancesAsync(db, employees, types, year, day, null);
    }

    /// <summary>What is still available of a leave type on a day (null: no limit), for a final settlement.</summary>
    internal static async Task<decimal?> AvailableAsync(RetailPlusDbContext db, Employee employee, LeaveType type, DateOnly day) =>
        (await BalancesAsync(db, [employee], [type], day.Year, day, null)).SingleOrDefault()?.Available;

    internal static async Task<List<LeaveBalance>> BalancesAsync(RetailPlusDbContext db, IReadOnlyCollection<Employee> employees, IReadOnlyCollection<LeaveType> types,
        int year, DateOnly asOf, Guid? leaveOut)
    {
        var result = new List<LeaveBalance>();
        if (employees.Count == 0 || types.Count == 0) return result;
        var ids = employees.Select(e => e.Id).ToList();
        var yearEnd = new DateOnly(year, 12, 31);
        var requests = new List<LeaveRequest>();
        var adjustments = new List<LeaveAdjustment>();
        foreach (var chunk in ids.Chunk(500))
        {
            var list = chunk.ToList();
            requests.AddRange(await db.LeaveRequests.AsNoTracking()
                .Where(r => list.Contains(r.EmployeeId) && (r.Status == LeaveRequestStatus.Approved || r.Status == LeaveRequestStatus.Pending) && r.FromDate <= yearEnd).ToListAsync());
            adjustments.AddRange(await db.LeaveAdjustments.AsNoTracking().Where(a => list.Contains(a.EmployeeId) && a.Year <= year).ToListAsync());
        }
        if (leaveOut is Guid skip) requests.RemoveAll(r => r.Id == skip);
        var calendar = await HrCalendar.LoadAsync(db, employees, new DateOnly(year, 1, 1), yearEnd);
        var requestsOf = requests.ToLookup(r => (r.EmployeeId, r.LeaveTypeId));
        var adjustmentsOf = adjustments.ToLookup(a => (a.EmployeeId, a.LeaveTypeId));
        // Requests that run from one year into the next are split by counting their days in each year.
        var calendars = new Dictionary<Guid, HrCalendar>();

        foreach (var employee in employees)
        {
            var country = calendar.CountryOf(employee);
            foreach (var type in types.Where(t => t.Country == country && (t.OnlyFor is null || t.OnlyFor == employee.Gender)))
            {
                var own = requestsOf[(employee.Id, type.Id)].ToList();
                var adjusted = adjustmentsOf[(employee.Id, type.Id)].ToList();
                async Task<(decimal Approved, decimal Pending)> TakenAsync(int y)
                {
                    decimal approved = 0, pending = 0;
                    foreach (var r in own.Where(r => r.FromDate.Year <= y && r.ToDate.Year >= y))
                    {
                        decimal days = r.Days;
                        if (r.FromDate.Year != r.ToDate.Year)
                        {
                            var from = r.FromDate.Year == y ? r.FromDate : new DateOnly(y, 1, 1);
                            var to = r.ToDate.Year == y ? r.ToDate : new DateOnly(y, 12, 31);
                            if (!calendars.TryGetValue(r.Id, out var c)) calendars[r.Id] = c = await HrCalendar.LoadAsync(db, [employee], r.FromDate, r.ToDate);
                            days = AttendanceRules.LeaveDays(from, to, r.FirstDayHalf && from == r.FromDate, r.LastDayHalf && to == r.ToDate, d => c.CountsForLeave(employee, type, d));
                        }
                        if (r.Status == LeaveRequestStatus.Approved) approved += days;
                        else pending += days;
                    }
                    return (approved, pending);
                }

                if (type.DaysPerYear <= 0)
                {
                    var (taken, waiting) = await TakenAsync(year);
                    result.Add(new LeaveBalance(employee.Id, type.Id, type.Code, type.Name, type.IsPaid, false, 0, 0, 0, adjusted.Where(a => a.Year == year).Sum(a => a.Days),
                        taken, waiting, null));
                    continue;
                }
                // The balance runs on from the year the employee joined: what is left of a year is carried into the next, up to the limit.
                decimal carried = 0;
                for (int y = Math.Min(employee.JoiningDate.Year, year); y <= year; y++)
                {
                    var entitled = Entitlement(employee, type, y);
                    var adjust = adjusted.Where(a => a.Year == y).Sum(a => a.Days);
                    var (taken, waiting) = await TakenAsync(y);
                    if (y < year)
                    {
                        decimal left = entitled + carried + adjust - taken;
                        carried = Math.Min(type.CarryOverMaxDays, Math.Max(0, left));
                        continue;
                    }
                    var earned = type.AccruesMonthly ? Earned(employee, type, y, asOf) : entitled;
                    result.Add(new LeaveBalance(employee.Id, type.Id, type.Code, type.Name, type.IsPaid, true, entitled, earned, carried, adjust, taken, waiting,
                        earned + carried + adjust - taken - waiting));
                }
            }
        }
        return result;
    }

    /// <summary>A year's entitlement: the whole for a full year, else the share of the days employed, to the half day.</summary>
    internal static decimal Entitlement(Employee employee, LeaveType type, int year)
    {
        var start = new DateOnly(year, 1, 1);
        var end = new DateOnly(year, 12, 31);
        var from = employee.JoiningDate > start ? employee.JoiningDate : start;
        var to = employee.LeavingDate is { } left && left < end ? left : end;
        if (to < from) return 0;
        decimal share = (decimal)(to.DayNumber - from.DayNumber + 1) / (end.DayNumber - start.DayNumber + 1);
        return Math.Round(type.DaysPerYear * share * 2, MidpointRounding.AwayFromZero) / 2;
    }

    /// <summary>For leave earned month by month: a twelfth of the year's days for every whole month worked in the year by <paramref name="asOf"/>.</summary>
    internal static decimal Earned(Employee employee, LeaveType type, int year, DateOnly asOf)
    {
        var start = new DateOnly(year, 1, 1);
        var end = new DateOnly(year, 12, 31);
        var from = employee.JoiningDate > start ? employee.JoiningDate : start;
        var to = asOf < end ? asOf : end;
        if (employee.LeavingDate is { } left && left < to) to = left;
        if (to < from) return 0;
        int months = PayrollRules.CompletedMonths(from, to);
        return Math.Round(type.DaysPerYear * Math.Min(12, months) / 12m, 2, MidpointRounding.AwayFromZero);
    }
}
