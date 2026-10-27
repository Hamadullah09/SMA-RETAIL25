using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>Hours one user worked on one day.</summary>
public sealed record HoursRow(Guid UserId, string UserName, DateOnly Day, decimal Hours);

/// <summary>A salesperson's sales and commission for a period. <see cref="Key"/> tells salespeople apart (their id, else their name).</summary>
public sealed record CommissionRow(string Key, Guid? StaffId, string Staff, int Sales, decimal Units, decimal NetSales, decimal FromRules, decimal FromItems)
{
    public decimal Commission => FromRules + FromItems;
}

/// <summary>One sales line with the commission it earned and the rule that decided it (none: the item's own rate).</summary>
public sealed record CommissionDetail(string Key, string Staff, DateTime SoldAt, long InvoiceNumber, string? Plu, string Description,
    decimal Quantity, decimal NetAmount, string? Rule, decimal Commission);

/// <summary>Staff: the time clock (punches, supervisor corrections, hours) and commission rules and commission.</summary>
public sealed class StaffService(DataContext data)
{
    // Time clock -----------------------------------------------------------------------------------------------------

    /// <summary>The user's open entry (punched in, not out yet), or null.</summary>
    public async Task<TimeClockEntry?> OpenEntryAsync(Guid userId)
    {
        await using var db = data.Create();
        return await db.TimeClockEntries.AsNoTracking().Where(t => t.UserId == userId && t.ClockOut == null).OrderByDescending(t => t.ClockIn).FirstOrDefaultAsync();
    }

    /// <summary>Everyone punched in now, earliest first.</summary>
    public async Task<List<TimeClockEntry>> WhoIsInAsync()
    {
        await using var db = data.Create();
        return await db.TimeClockEntries.AsNoTracking().Where(t => t.ClockOut == null).OrderBy(t => t.ClockIn).ToListAsync();
    }

    /// <summary>Punches the user in, or out when they are in. Returns the entry (open after a punch in).</summary>
    public async Task<TimeClockEntry> PunchAsync(Guid userId)
    {
        await using var db = data.Create();
        var open = await db.TimeClockEntries.Where(t => t.UserId == userId && t.ClockOut == null).OrderByDescending(t => t.ClockIn).FirstOrDefaultAsync();
        return open is null ? await PunchInAsync(userId) : await PunchOutAsync(userId);
    }

    /// <summary>Punches a user in. A user may be in only once at a time, and needs the time clock permission.</summary>
    public async Task<TimeClockEntry> PunchInAsync(Guid userId, DateTime? at = null)
    {
        await using var db = data.Create();
        var user = await PuncherAsync(db, userId);
        if (await db.TimeClockEntries.Where(t => t.UserId == userId && t.ClockOut == null).Select(t => (DateTime?)t.ClockIn).FirstOrDefaultAsync() is { } since)
            throw new BusinessRuleException($"{user.UserName} is already punched in, since {since.ToString("g", CultureInfo.CurrentCulture)}.");
        var entry = new TimeClockEntry { UserId = userId, UserName = user.UserName, ClockIn = Minute(at ?? DateTime.Now) };
        db.TimeClockEntries.Add(entry);
        db.Audit(AuditActions.TimeClock, $"{user.UserName} punched in", nameof(TimeClockEntry), entry.Id, userId: user.Id, userName: user.UserName);
        await db.SaveChangesAsync();
        return entry;
    }

    /// <summary>Punches a user out of their open entry.</summary>
    public async Task<TimeClockEntry> PunchOutAsync(Guid userId, DateTime? at = null)
    {
        await using var db = data.Create();
        var user = await PuncherAsync(db, userId);
        var entry = await db.TimeClockEntries.Where(t => t.UserId == userId && t.ClockOut == null).OrderByDescending(t => t.ClockIn).FirstOrDefaultAsync()
                    ?? throw new BusinessRuleException($"{user.UserName} is not punched in.");
        var when = Minute(at ?? DateTime.Now);
        entry.ClockOut = when < entry.ClockIn ? entry.ClockIn : when;
        db.Audit(AuditActions.TimeClock, $"{user.UserName} punched out after {TimeClock.Hours(entry.ClockIn, entry.ClockOut.Value):0.##} hours",
            nameof(TimeClockEntry), entry.Id, userId: user.Id, userName: user.UserName);
        await db.SaveChangesAsync();
        return entry;
    }

    private static async Task<UserAccount> PuncherAsync(RetailPlusDbContext db, Guid userId)
    {
        var user = await db.UserAccounts.AsNoTracking().Include(u => u.Role).ThenInclude(r => r!.Permissions).SingleOrDefaultAsync(u => u.Id == userId)
                   ?? throw new BusinessRuleException("Sign in as a user to use the time clock.");
        if (!SetupService.PermissionsOf(user).Contains(Permissions.TimeClockPunch))
            throw new BusinessRuleException($"{user.UserName} may not use the time clock.");
        return user;
    }

    /// <summary>Entries that started in the period (store local dates, inclusive), for one user or everyone.</summary>
    public async Task<List<TimeClockEntry>> GetEntriesAsync(DateOnly from, DateOnly to, Guid? userId = null)
    {
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        await using var db = data.Create();
        var q = db.TimeClockEntries.AsNoTracking().Where(t => (t.ClockIn >= start && t.ClockIn < end) || (t.ClockOut == null && t.ClockIn < end));
        if (data.StoreScope is { } store) q = q.Where(t => t.StoreId == store);
        if (userId is Guid id) q = q.Where(t => t.UserId == id);
        return await q.OrderBy(t => t.UserName).ThenBy(t => t.ClockIn).ToListAsync();
    }

    /// <summary>
    /// A supervisor corrects an entry (a forgotten punch, a wrong time). Needs <see cref="Permissions.TimeClockEdit"/> and
    /// a reason, which stays on the entry; the change is audited.
    /// </summary>
    public async Task<TimeClockEntry> EditEntryAsync(Guid entryId, DateTime clockIn, DateTime? clockOut, string? reason, SessionInfo editor)
    {
        RequireEdit(editor, reason);
        await using var db = data.Create();
        var entry = await db.TimeClockEntries.SingleOrDefaultAsync(t => t.Id == entryId) ?? throw new BusinessRuleException("That entry no longer exists.");
        var before = Times(entry);
        await CheckTimesAsync(db, entry.UserId, entry.UserName, Minute(clockIn), clockOut is { } o ? Minute(o) : null, entry.Id);
        entry.ClockIn = Minute(clockIn);
        entry.ClockOut = clockOut is { } c ? Minute(c) : null;
        Stamp(entry, reason!, editor);
        db.Audit(AuditActions.TimeClockEdit, $"{entry.UserName}, {entry.ClockIn.ToString("d", CultureInfo.CurrentCulture)}: {entry.EditReason}",
            nameof(TimeClockEntry), entry.Id, before, Times(entry));
        await db.SaveChangesAsync();
        return entry;
    }

    /// <summary>A supervisor adds a shift that was never punched, with a reason.</summary>
    public async Task<TimeClockEntry> AddEntryAsync(Guid userId, DateTime clockIn, DateTime clockOut, string? reason, SessionInfo editor)
    {
        RequireEdit(editor, reason);
        await using var db = data.Create();
        var user = await db.UserAccounts.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId) ?? throw new BusinessRuleException("That user no longer exists.");
        await CheckTimesAsync(db, userId, user.UserName, Minute(clockIn), Minute(clockOut), null);
        var entry = new TimeClockEntry { UserId = userId, UserName = user.UserName, ClockIn = Minute(clockIn), ClockOut = Minute(clockOut) };
        Stamp(entry, reason!, editor);
        db.TimeClockEntries.Add(entry);
        db.Audit(AuditActions.TimeClockEdit, $"{entry.UserName}, {entry.ClockIn.ToString("d", CultureInfo.CurrentCulture)} added: {entry.EditReason}",
            nameof(TimeClockEntry), entry.Id, null, Times(entry));
        await db.SaveChangesAsync();
        return entry;
    }

    private static void RequireEdit(SessionInfo editor, string? reason)
    {
        if (!editor.Can(Permissions.TimeClockEdit)) throw new BusinessRuleException($"{editor.UserName} may not change time clock entries.");
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Say why the entry is changed.");
    }

    private static void Stamp(TimeClockEntry entry, string reason, SessionInfo editor)
    {
        entry.EditReason = reason.Trim().Length > 200 ? reason.Trim()[..200] : reason.Trim();
        entry.EditedBy = editor.UserName;
        entry.EditedById = editor.UserId;
        entry.EditedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Out after in, no overlap with the user's other shifts, and at most one open shift.</summary>
    private static async Task CheckTimesAsync(RetailPlusDbContext db, Guid userId, string userName, DateTime clockIn, DateTime? clockOut, Guid? except)
    {
        if (clockOut is { } o && o <= clockIn) throw new BusinessRuleException("The time out must be after the time in.");
        var others = db.TimeClockEntries.Where(t => t.UserId == userId && t.Id != except);
        if (clockOut is null && await others.AnyAsync(t => t.ClockOut == null))
            throw new BusinessRuleException($"{userName} already has an open entry.");
        var end = clockOut ?? DateTime.MaxValue;
        if (await others.AnyAsync(t => t.ClockIn < end && (t.ClockOut == null || t.ClockOut > clockIn)))
            throw new BusinessRuleException($"Those times overlap another entry of {userName}.");
    }

    private static Dictionary<string, object?> Times(TimeClockEntry e) => new()
    {
        [nameof(TimeClockEntry.ClockIn)] = e.ClockIn,
        [nameof(TimeClockEntry.ClockOut)] = e.ClockOut,
    };

    private static DateTime Minute(DateTime at) => new(at.Year, at.Month, at.Day, at.Hour, at.Minute, 0, at.Kind);

    /// <summary>
    /// Hours worked per user per day in the period, from finished shifts: a shift over midnight counts on both days.
    /// </summary>
    public async Task<List<HoursRow>> HoursByDayAsync(DateOnly from, DateOnly to, Guid? userId = null)
    {
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        await using var db = data.Create();
        var q = db.TimeClockEntries.AsNoTracking().Where(t => t.ClockOut != null && t.ClockIn < end && t.ClockOut > start);
        if (data.StoreScope is { } store) q = q.Where(t => t.StoreId == store);
        if (userId is Guid id) q = q.Where(t => t.UserId == id);
        var entries = await q.ToListAsync();
        var names = await db.UserAccounts.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.UserName);
        return entries
            .SelectMany(e => TimeClock.SplitByDay(e.ClockIn, e.ClockOut!.Value).Select(d => (e.UserId, d.Day, d.Hours)))
            .Where(d => d.Day >= from && d.Day <= to)
            .GroupBy(d => (d.UserId, d.Day))
            .Select(g => new HoursRow(g.Key.UserId, names.GetValueOrDefault(g.Key.UserId) ?? entries.First(e => e.UserId == g.Key.UserId).UserName, g.Key.Day, g.Sum(d => d.Hours)))
            .OrderBy(r => r.UserName, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Day)
            .ToList();
    }

    // Commission -----------------------------------------------------------------------------------------------------

    public async Task<List<CommissionRule>> GetRulesAsync()
    {
        await using var db = data.Create();
        return await db.CommissionRules.AsNoTracking().OrderBy(r => r.Name).ToListAsync();
    }

    /// <summary>What a rule can be for: the departments, categories and the items named by rules, with their ids.</summary>
    public async Task<(List<Department> Departments, List<Category> Categories, Dictionary<Guid, string> Items)> GetRuleChoicesAsync()
    {
        await using var db = data.Create();
        var departments = await db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync();
        var categories = await db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        var items = await db.Items.AsNoTracking().Where(i => db.CommissionRules.Any(r => r.ItemId == i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.Plu + " " + i.Name);
        return (departments, categories, items);
    }

    /// <summary>Adds or changes a commission rule. Needs <see cref="Permissions.TimeClockEdit"/>.</summary>
    public async Task<CommissionRule> SaveRuleAsync(CommissionRule rule, SessionInfo editor)
    {
        if (!editor.Can(Permissions.TimeClockEdit)) throw new BusinessRuleException($"{editor.UserName} may not change commission rules.");
        if (string.IsNullOrWhiteSpace(rule.Name)) throw new BusinessRuleException("Give the rule a name.");
        if (rule.Rate < 0) throw new BusinessRuleException("The rate cannot be below zero.");
        if (rule.Basis == CommissionBasis.PercentOfNetSales && rule.Rate > 100) throw new BusinessRuleException("A percentage is at most 100.");
        if (rule.EffectiveFrom is { } f && rule.EffectiveTo is { } t && t < f) throw new BusinessRuleException("The last day is before the first day.");
        switch (rule.Scope)
        {
            case CommissionScope.Department when rule.DepartmentId is null: throw new BusinessRuleException("Choose the department.");
            case CommissionScope.Category when rule.CategoryId is null: throw new BusinessRuleException("Choose the category.");
            case CommissionScope.Item when rule.ItemId is null: throw new BusinessRuleException("Choose the item.");
        }
        await using var db = data.Create();
        var row = rule.Id == Guid.Empty ? null : await db.CommissionRules.SingleOrDefaultAsync(r => r.Id == rule.Id);
        if (row is null) db.CommissionRules.Add(row = new CommissionRule());
        row.Name = rule.Name.Trim();
        row.UserId = rule.UserId;
        row.Scope = rule.Scope;
        row.DepartmentId = rule.Scope == CommissionScope.Department ? rule.DepartmentId : null;
        row.CategoryId = rule.Scope == CommissionScope.Category ? rule.CategoryId : null;
        row.ItemId = rule.Scope == CommissionScope.Item ? rule.ItemId : null;
        row.Basis = rule.Basis;
        row.Rate = rule.Rate;
        row.EffectiveFrom = rule.EffectiveFrom;
        row.EffectiveTo = rule.EffectiveTo;
        row.IsActive = rule.IsActive;
        await db.SaveChangesAsync();
        return row;
    }

    public async Task DeleteRuleAsync(Guid id, SessionInfo editor)
    {
        if (!editor.Can(Permissions.TimeClockEdit)) throw new BusinessRuleException($"{editor.UserName} may not change commission rules.");
        await using var db = data.Create();
        var row = await db.CommissionRules.SingleOrDefaultAsync(r => r.Id == id);
        if (row is null) return;
        db.CommissionRules.Remove(row);
        await db.SaveChangesAsync();
    }

    /// <summary>Tells salespeople apart: by user id, or by name for sales without one (imported).</summary>
    public static string StaffKey(Guid? staffId, string? staff) => staffId?.ToString() ?? "name:" + (staff ?? "").Trim().ToUpperInvariant();

    /// <summary>
    /// The commission of every sales line in the period (completed sales, sale and return lines), with the rule that
    /// decided it. <paramref name="locationId"/> limits it to one location.
    /// </summary>
    public async Task<List<CommissionDetail>> CommissionLinesAsync(DateOnly from, DateOnly to, Guid? locationId = null, string? staffKey = null)
    {
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        await using var db = data.Create();
        var sales = db.Sales.AsNoTracking().Where(s => s.Status == SaleStatus.Completed && s.SoldAt >= start && s.SoldAt < end);
        if (locationId is Guid loc) sales = sales.Where(s => s.LocationId == loc);
        if (data.StoreScope is { } store) sales = sales.Where(s => s.StoreId == store);
        var rows = await (
            from l in db.SaleLines.AsNoTracking().Where(l => l.Kind == SaleLineKind.Sale || l.Kind == SaleLineKind.Return)
            join s in sales on l.SaleId equals s.Id
            join i in db.Items.AsNoTracking() on l.ItemId equals (Guid?)i.Id into items
            from i in items.DefaultIfEmpty()
            select new
            {
                s.StaffId, s.Staff, s.SoldAt, s.InvoiceNumber, l.LineNumber, l.ItemId, l.Plu, l.Description, l.Department, l.Quantity, l.LineTotal, l.Commission,
                DepartmentId = i == null ? null : i.DepartmentId, CategoryId = i == null ? null : i.CategoryId,
            }).ToListAsync();
        var rules = await db.CommissionRules.AsNoTracking().Where(r => r.IsActive).ToListAsync();
        var departments = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Name, d => d.Id, StringComparer.OrdinalIgnoreCase);
        var names = await db.UserAccounts.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.UserName);
        var result = new List<CommissionDetail>(rows.Count);
        foreach (var r in rows.OrderBy(r => r.SoldAt).ThenBy(r => r.InvoiceNumber).ThenBy(r => r.LineNumber))
        {
            var key = StaffKey(r.StaffId, r.Staff);
            if (staffKey is not null && key != staffKey) continue;
            var department = r.DepartmentId ?? (r.Department is { } d && departments.TryGetValue(d, out var id) ? id : null);
            var line = new CommissionLine(r.StaffId, r.Staff, DateOnly.FromDateTime(r.SoldAt), r.ItemId, department, r.CategoryId, r.Quantity, r.LineTotal, r.Commission);
            var rule = Commissions.RuleFor(rules, line);
            var staff = r.StaffId is Guid sid && names.TryGetValue(sid, out var n) ? n : r.Staff ?? "(none)";
            result.Add(new CommissionDetail(key, staff, r.SoldAt, r.InvoiceNumber, r.Plu, r.Description, r.Quantity, r.LineTotal, rule?.Name, Commissions.Apply(rule, line)));
        }
        return result;
    }

    /// <summary>Each salesperson's sales and commission in the period, the biggest seller first.</summary>
    public async Task<List<CommissionRow>> CommissionByStaffAsync(DateOnly from, DateOnly to, Guid? locationId = null)
    {
        var lines = await CommissionLinesAsync(from, to, locationId);
        return lines.GroupBy(l => l.Key)
            .Select(g => new CommissionRow(g.Key, Guid.TryParse(g.Key, out var id) ? id : null, g.Last().Staff,
                g.Select(l => (l.SoldAt, l.InvoiceNumber)).Distinct().Count(), g.Sum(l => l.Quantity), g.Sum(l => l.NetAmount),
                g.Where(l => l.Rule is not null).Sum(l => l.Commission), g.Where(l => l.Rule is null).Sum(l => l.Commission)))
            .OrderByDescending(r => r.NetSales)
            .ToList();
    }
}
