using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Staff;

namespace Retail25.Application.Staff;

public sealed record StaffRowDto(
    Guid Id,
    string StaffCode,
    string FullName,
    int AccessLevel,
    bool IsActive,
    bool IsClockedIn,
    DateTimeOffset? ClockedInAt);

/// <summary>What the punch-clock widget shows: whether you are on, and since when.</summary>
public sealed record TimeClockStateDto(
    Guid? EntryId,
    Guid StaffId,
    string StaffName,
    bool IsClockedIn,
    DateTimeOffset? ClockedInAt,
    decimal HoursSoFar,
    decimal HoursToday);

public sealed record TimeClockEntryDto(
    Guid Id,
    Guid StaffId,
    string StaffName,
    DateTimeOffset ClockIn,
    DateTimeOffset? ClockOut,
    decimal? HoursWorked);

public sealed record CommissionRuleDto(
    Guid Id,
    Guid StaffId,
    Guid? ProductId,
    string? ProductName,
    Guid? DepartmentId,
    string? DepartmentName,
    CommissionType CommissionType,
    decimal Value,
    decimal? MaxCommission,
    bool IsActive);

[RequiresPermission(PermissionKeys.Staff.Read)]
public sealed record BrowseStaffQuery(Guid LocationId, bool IncludeInactive = false)
    : IRequest<IReadOnlyList<StaffRowDto>>;

/// <summary>Where the signed-in person stands right now. Their own state, so no elevated permission.</summary>
[RequiresPermission(PermissionKeys.Staff.TimeClock)]
public sealed record GetMyTimeClockQuery(Guid LocationId) : IRequest<Result<TimeClockStateDto>>;

[RequiresPermission(PermissionKeys.Staff.TimeClock)]
public sealed record ClockInCommand(Guid LocationId) : IRequest<Result<TimeClockStateDto>>;

[RequiresPermission(PermissionKeys.Staff.TimeClock)]
public sealed record ClockOutCommand(Guid LocationId) : IRequest<Result<TimeClockStateDto>>;

/// <summary>
/// Anyone's punches for a window. Reading someone else's hours is a supervisor's job, so this is
/// gated above the self-service clock.
/// </summary>
[RequiresPermission(PermissionKeys.Reports.Hours)]
public sealed record BrowseTimeClockQuery(
    Guid LocationId,
    DateOnly From,
    DateOnly To,
    Guid? StaffId = null) : IRequest<IReadOnlyList<TimeClockEntryDto>>;

/// <summary>
/// Corrects a punch — the forgotten clock-out, the shift started an hour late. Separate permission
/// from clocking yourself in, because editing hours is editing what someone gets paid.
/// </summary>
[RequiresPermission(PermissionKeys.Staff.TimeClockEdit)]
public sealed record AmendTimeClockEntryCommand(
    Guid EntryId,
    DateTimeOffset ClockIn,
    DateTimeOffset? ClockOut) : IRequest<Result<TimeClockEntryDto>>;

[RequiresPermission(PermissionKeys.Staff.TimeClockEdit)]
public sealed record DeleteTimeClockEntryCommand(Guid EntryId) : IRequest<Result>;

[RequiresPermission(PermissionKeys.Staff.Read)]
public sealed record ListCommissionRulesQuery(Guid StaffId) : IRequest<IReadOnlyList<CommissionRuleDto>>;

[RequiresPermission(PermissionKeys.Staff.Write)]
public sealed record SaveCommissionRuleCommand(
    Guid? Id,
    Guid StaffId,
    CommissionType CommissionType,
    decimal Value,
    Guid? ProductId = null,
    Guid? DepartmentId = null,
    decimal? MaxCommission = null,
    bool IsActive = true) : IRequest<Result<CommissionRuleDto>>;

[RequiresPermission(PermissionKeys.Staff.Write)]
public sealed record DeleteCommissionRuleCommand(Guid Id) : IRequest<Result>;

public sealed class StaffHandlers :
    IRequestHandler<BrowseStaffQuery, IReadOnlyList<StaffRowDto>>,
    IRequestHandler<GetMyTimeClockQuery, Result<TimeClockStateDto>>,
    IRequestHandler<ClockInCommand, Result<TimeClockStateDto>>,
    IRequestHandler<ClockOutCommand, Result<TimeClockStateDto>>,
    IRequestHandler<BrowseTimeClockQuery, IReadOnlyList<TimeClockEntryDto>>,
    IRequestHandler<AmendTimeClockEntryCommand, Result<TimeClockEntryDto>>,
    IRequestHandler<DeleteTimeClockEntryCommand, Result>,
    IRequestHandler<ListCommissionRulesQuery, IReadOnlyList<CommissionRuleDto>>,
    IRequestHandler<SaveCommissionRuleCommand, Result<CommissionRuleDto>>,
    IRequestHandler<DeleteCommissionRuleCommand, Result>
{
    public static readonly Error NoStaffProfile = new(
        "staff.no_profile",
        "This sign-in is not linked to a staff record, so it cannot use the time clock.");

    public static readonly Error AlreadyClockedIn = new(
        "staff.already_clocked_in",
        "You are already clocked in.");

    public static readonly Error NotClockedIn = new(
        "staff.not_clocked_in",
        "You are not clocked in.");

    public static readonly Error EntryNotFound = new("staff.entry_not_found", "No such time-clock entry.");

    public static readonly Error EndsBeforeItStarts = new(
        "staff.ends_before_it_starts",
        "A shift cannot end before it began.");

    public static readonly Error RuleNotFound = new("commission.rule_not_found", "No such commission rule.");

    public static readonly Error DuplicateRule = new(
        "commission.duplicate_rule",
        "This person already has a rule for that item or department.");

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTime _clock;

    public StaffHandlers(IApplicationDbContext db, ICurrentUser currentUser, IDateTime clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<IReadOnlyList<StaffRowDto>> Handle(BrowseStaffQuery request, CancellationToken ct)
    {
        var query = _db.StaffProfiles.AsNoTracking();

        if (!request.IncludeInactive)
        {
            query = query.Where(s => s.IsActive);
        }

        var staff = await query.OrderBy(s => s.StaffCode).ToListAsync(ct);
        var ids = staff.Select(s => s.Id).ToList();

        var open = await _db.TimeClockEntries.AsNoTracking()
            .Where(e => ids.Contains(e.StaffId) && e.ClockOut == null && e.LocationId == request.LocationId)
            .ToDictionaryAsync(e => e.StaffId, e => e.ClockIn, ct);

        return staff.Select(s => new StaffRowDto(
            s.Id,
            s.StaffCode,
            s.FullName,
            s.AccessLevel,
            s.IsActive,
            open.ContainsKey(s.Id),
            open.TryGetValue(s.Id, out var at) ? at : null)).ToList();
    }

    public async Task<Result<TimeClockStateDto>> Handle(GetMyTimeClockQuery request, CancellationToken ct)
    {
        var staff = await MeAsync(ct);

        return staff is null
            ? Result.Failure<TimeClockStateDto>(NoStaffProfile)
            : Result.Success(await StateAsync(staff, request.LocationId, ct));
    }

    public async Task<Result<TimeClockStateDto>> Handle(ClockInCommand request, CancellationToken ct)
    {
        var staff = await MeAsync(ct);

        if (staff is null)
        {
            return Result.Failure<TimeClockStateDto>(NoStaffProfile);
        }

        // Checked across every location, not just this one: someone who forgot to clock out at
        // another store is still on the clock, and opening a second shift would pay them twice.
        var open = await _db.TimeClockEntries.FirstOrDefaultAsync(e => e.StaffId == staff.Id && e.ClockOut == null, ct);

        if (open is not null)
        {
            return Result.Failure<TimeClockStateDto>(AlreadyClockedIn.With("since", open.ClockIn));
        }

        _db.TimeClockEntries.Add(TimeClockEntry.ClockInAt(staff.Id, request.LocationId, _clock.Now));
        await _db.SaveChangesAsync(ct);

        return Result.Success(await StateAsync(staff, request.LocationId, ct));
    }

    public async Task<Result<TimeClockStateDto>> Handle(ClockOutCommand request, CancellationToken ct)
    {
        var staff = await MeAsync(ct);

        if (staff is null)
        {
            return Result.Failure<TimeClockStateDto>(NoStaffProfile);
        }

        var open = await _db.TimeClockEntries.FirstOrDefaultAsync(e => e.StaffId == staff.Id && e.ClockOut == null, ct);

        if (open is null)
        {
            return Result.Failure<TimeClockStateDto>(NotClockedIn);
        }

        open.ClockOutAt(_clock.Now);
        await _db.SaveChangesAsync(ct);

        return Result.Success(await StateAsync(staff, request.LocationId, ct));
    }

    public async Task<IReadOnlyList<TimeClockEntryDto>> Handle(BrowseTimeClockQuery request, CancellationToken ct)
    {
        var (from, to) = DayRangeUtc(request.From, request.To);

        var query = _db.TimeClockEntries.AsNoTracking()
            .Where(e => e.LocationId == request.LocationId && e.ClockIn >= from && e.ClockIn <= to);

        if (request.StaffId is { } staffId)
        {
            query = query.Where(e => e.StaffId == staffId);
        }

        var entries = await query.OrderByDescending(e => e.ClockIn).ToListAsync(ct);
        var names = await StaffNamesAsync(entries.Select(e => e.StaffId), ct);

        return entries.Select(e => new TimeClockEntryDto(
            e.Id,
            e.StaffId,
            names.GetValueOrDefault(e.StaffId, "—"),
            e.ClockIn,
            e.ClockOut,
            e.HoursWorked)).ToList();
    }

    public async Task<Result<TimeClockEntryDto>> Handle(AmendTimeClockEntryCommand request, CancellationToken ct)
    {
        var entry = await _db.TimeClockEntries.FirstOrDefaultAsync(e => e.Id == request.EntryId, ct);

        if (entry is null)
        {
            return Result.Failure<TimeClockEntryDto>(EntryNotFound);
        }

        if (request.ClockOut is { } out_ && out_ < request.ClockIn)
        {
            return Result.Failure<TimeClockEntryDto>(EndsBeforeItStarts);
        }

        entry.ClockIn = request.ClockIn;

        if (request.ClockOut is { } clockOut)
        {
            entry.ClockOutAt(clockOut);
        }
        else
        {
            // Reopening a shift: the hours have to go with it, or the report would keep counting a
            // figure that no longer has an end time behind it.
            entry.ClockOut = null;
            entry.HoursWorked = null;
        }

        await _db.SaveChangesAsync(ct);

        var names = await StaffNamesAsync([entry.StaffId], ct);

        return Result.Success(new TimeClockEntryDto(
            entry.Id, entry.StaffId, names.GetValueOrDefault(entry.StaffId, "—"),
            entry.ClockIn, entry.ClockOut, entry.HoursWorked));
    }

    public async Task<Result> Handle(DeleteTimeClockEntryCommand request, CancellationToken ct)
    {
        var entry = await _db.TimeClockEntries.FirstOrDefaultAsync(e => e.Id == request.EntryId, ct);

        if (entry is null)
        {
            return Result.Failure(EntryNotFound);
        }

        _db.TimeClockEntries.Remove(entry);
        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<IReadOnlyList<CommissionRuleDto>> Handle(ListCommissionRulesQuery request, CancellationToken ct)
    {
        var rules = await _db.CommissionRules.AsNoTracking()
            .Where(r => r.StaffId == request.StaffId)
            .ToListAsync(ct);

        return await DescribeAsync(rules, ct);
    }

    public async Task<Result<CommissionRuleDto>> Handle(SaveCommissionRuleCommand request, CancellationToken ct)
    {
        CommissionRule rule;

        if (request.Id is { } id)
        {
            var existing = await _db.CommissionRules.FirstOrDefaultAsync(r => r.Id == id, ct);

            if (existing is null)
            {
                return Result.Failure<CommissionRuleDto>(RuleNotFound);
            }

            var updated = existing.Update(request.CommissionType, request.Value, request.MaxCommission, request.IsActive);

            if (updated.IsFailure)
            {
                return Result.Failure<CommissionRuleDto>(updated.Error);
            }

            rule = existing;
        }
        else
        {
            // Checked before the insert so the operator gets "there is already a rule for that" and
            // not a unique-constraint violation from the database.
            //
            // Compared in memory rather than in the predicate: a staff-wide rule has both scope
            // columns null, and `column == null` is NULL in SQL, never true — so the whole check
            // would silently pass and let a second staff-wide rule through.
            var existingScopes = await _db.CommissionRules.AsNoTracking()
                .Where(r => r.StaffId == request.StaffId)
                .Select(r => new { r.ProductId, r.DepartmentId })
                .ToListAsync(ct);

            var clash = existingScopes.Any(
                s => s.ProductId == request.ProductId && s.DepartmentId == request.DepartmentId);

            if (clash)
            {
                return Result.Failure<CommissionRuleDto>(DuplicateRule);
            }

            var created = CommissionRule.Create(
                request.StaffId, request.CommissionType, request.Value,
                request.ProductId, request.DepartmentId, request.MaxCommission);

            if (created.IsFailure)
            {
                return Result.Failure<CommissionRuleDto>(created.Error);
            }

            rule = created.Value;
            _db.CommissionRules.Add(rule);
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success((await DescribeAsync([rule], ct))[0]);
    }

    public async Task<Result> Handle(DeleteCommissionRuleCommand request, CancellationToken ct)
    {
        var rule = await _db.CommissionRules.FirstOrDefaultAsync(r => r.Id == request.Id, ct);

        if (rule is null)
        {
            return Result.Failure(RuleNotFound);
        }

        // Removed outright rather than soft-deleted: what was already earned lives on the commission
        // ledger with the rate frozen into it, so deleting the rule cannot restate anyone's pay.
        _db.CommissionRules.Remove(rule);
        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    /// <summary>
    /// A day range as UTC instants. Same shape as the reports use — a local-offset
    /// <c>DateTimeOffset</c> is rejected outright by <c>timestamptz</c>.
    /// </summary>
    public static (DateTimeOffset From, DateTimeOffset To) DayRangeUtc(DateOnly from, DateOnly to)
        => (new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero));

    private async Task<StaffProfile?> MeAsync(CancellationToken ct)
        => _currentUser.StaffId is { } id
            ? await _db.StaffProfiles.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct)
            : null;

    private async Task<TimeClockStateDto> StateAsync(StaffProfile staff, Guid locationId, CancellationToken ct)
    {
        var open = await _db.TimeClockEntries.AsNoTracking()
            .FirstOrDefaultAsync(e => e.StaffId == staff.Id && e.ClockOut == null, ct);

        var now = _clock.Now;
        var (dayStart, dayEnd) = DayRangeUtc(((IDateTime)_clock).Today(), ((IDateTime)_clock).Today());

        var closedToday = await _db.TimeClockEntries.AsNoTracking()
            .Where(e => e.StaffId == staff.Id && e.ClockIn >= dayStart && e.ClockIn <= dayEnd && e.HoursWorked != null)
            .SumAsync(e => e.HoursWorked ?? 0m, ct);

        var soFar = open is null ? 0m : decimal.Round((decimal)(now - open.ClockIn).TotalHours, 2, MidpointRounding.AwayFromZero);

        return new TimeClockStateDto(
            open?.Id,
            staff.Id,
            staff.FullName,
            open is not null,
            open?.ClockIn,
            soFar,

            // Today's total includes the shift still running, so the widget does not read as zero
            // for someone who has been on since eight this morning.
            decimal.Round(closedToday + soFar, 2, MidpointRounding.AwayFromZero));
    }

    private async Task<Dictionary<Guid, string>> StaffNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();

        if (distinct.Count == 0)
        {
            return [];
        }

        return await _db.StaffProfiles.AsNoTracking()
            .Where(s => distinct.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
    }

    private async Task<IReadOnlyList<CommissionRuleDto>> DescribeAsync(
        IReadOnlyList<CommissionRule> rules, CancellationToken ct)
    {
        var productIds = rules.Select(r => r.ProductId).OfType<Guid>().Distinct().ToList();
        var departmentIds = rules.Select(r => r.DepartmentId).OfType<Guid>().Distinct().ToList();

        var products = productIds.Count == 0
            ? []
            : await _db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => $"{p.StockCode} — {p.Name}", ct);

        var departments = departmentIds.Count == 0
            ? []
            : await _db.Departments.AsNoTracking()
                .Where(d => departmentIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Name, ct);

        // Most specific first, which is the order they are applied in — so the list reads the way
        // the calculator resolves it.
        return rules
            .OrderByDescending(r => r.Specificity)
            .ThenByDescending(r => r.Value)
            .Select(r => new CommissionRuleDto(
                r.Id,
                r.StaffId,
                r.ProductId,
                r.ProductId is { } pid ? products.GetValueOrDefault(pid) : null,
                r.DepartmentId,
                r.DepartmentId is { } did ? departments.GetValueOrDefault(did) : null,
                r.CommissionType,
                r.Value,
                r.MaxCommission,
                r.IsActive))
            .ToList();
    }
}
