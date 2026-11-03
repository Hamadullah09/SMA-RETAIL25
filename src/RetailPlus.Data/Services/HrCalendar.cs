using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>
/// What a day is for an employee by the roster: a working day or not, the shift (none for a working day of an employee
/// without shifts, who works the rule set's week), and the holiday falling on it.
/// </summary>
public readonly record struct DayPlan(bool Working, Shift? Shift, string? Holiday)
{
    /// <summary>A day the employee is expected to work: a working day that is not a holiday.</summary>
    public bool Expected => Working && Holiday is null;
}

/// <summary>
/// The working calendar of some employees over some days, read once: their shifts by roster week and day changes, the
/// holidays and payroll rules of their country. A day's shift is the roster's change for that day if there is one (no
/// shift: a day off), else the shift of the roster's week, else the employee's usual shift, worked on its days of the
/// week. An employee without any shift works the rule set's working week (Monday on), without shift times.
/// </summary>
internal sealed class HrCalendar
{
    private Dictionary<Guid, Shift> _shifts = [];
    private Dictionary<(Guid, DateOnly), Guid> _weeks = [];
    private Dictionary<(Guid, DateOnly), RosterDay> _days = [];
    private Dictionary<string, Dictionary<DateOnly, string>> _holidays = [];
    private Dictionary<string, PayrollRuleSet?> _rules = [];
    private Dictionary<Guid, string> _storeCountries = [];
    private string _companyCountry = HrService.DefaultCountry;

    public static async Task<HrCalendar> LoadAsync(RetailPlusDbContext db, IReadOnlyCollection<Employee> employees, DateOnly from, DateOnly to)
    {
        var calendar = new HrCalendar
        {
            _companyCountry = await HrService.CompanyCountryAsync(db),
            _storeCountries = await HrService.StoreCountriesAsync(db),
            _shifts = await db.Shifts.AsNoTracking().ToDictionaryAsync(s => s.Id),
        };
        var ids = employees.Select(e => e.Id).ToList();
        var firstWeek = TimeClock.WeekStart(from);
        // Read in chunks so a large staff list stays within the engines' parameter limits.
        foreach (var chunk in ids.Chunk(500))
        {
            var list = chunk.ToList();
            foreach (var w in await db.RosterWeeks.AsNoTracking().Where(w => list.Contains(w.EmployeeId) && w.WeekStart >= firstWeek && w.WeekStart <= to).ToListAsync())
                calendar._weeks[(w.EmployeeId, w.WeekStart)] = w.ShiftId;
            foreach (var d in await db.RosterDays.AsNoTracking().Where(d => list.Contains(d.EmployeeId) && d.Date >= from && d.Date <= to).ToListAsync())
                calendar._days[(d.EmployeeId, d.Date)] = d;
        }
        var countries = employees.Select(calendar.CountryOf).Distinct().ToList();
        foreach (var country in countries)
        {
            calendar._holidays[country] = await db.Holidays.AsNoTracking().Where(h => h.Country == country && h.Date >= from && h.Date <= to)
                .ToDictionaryAsync(h => h.Date, h => h.Name);
            calendar._rules[country] = await HrService.RuleSetForAsync(db, country, to) ?? await HrService.RuleSetForAsync(db, country, from);
        }
        return calendar;
    }

    public string CountryOf(Employee employee) =>
        employee.StoreId is Guid s && _storeCountries.TryGetValue(s, out var c) ? c : _companyCountry;

    /// <summary>The rule set of the employee's country in effect at the end of the days read (null when the country has none).</summary>
    public PayrollRuleSet? RulesOf(Employee employee) => _rules.GetValueOrDefault(CountryOf(employee));

    public Shift? Shift(Guid id) => _shifts.GetValueOrDefault(id);

    public DayPlan PlanOn(Employee employee, DateOnly day)
    {
        var holiday = _holidays.TryGetValue(CountryOf(employee), out var days) ? days.GetValueOrDefault(day) : null;
        if (_days.TryGetValue((employee.Id, day), out var change))
            return change.ShiftId is Guid sid && _shifts.TryGetValue(sid, out var changed) ? new DayPlan(true, changed, holiday) : new DayPlan(false, null, holiday);
        Guid? shiftId = _weeks.TryGetValue((employee.Id, TimeClock.WeekStart(day)), out var week) ? week : employee.ShiftId;
        if (shiftId is Guid id && _shifts.TryGetValue(id, out var shift))
            return AttendanceRules.Works(shift.Days, day.DayOfWeek) ? new DayPlan(true, shift, holiday) : new DayPlan(false, null, holiday);
        var week6 = AttendanceRules.FirstDays(RulesOf(employee)?.WorkingDaysPerWeek ?? 6);
        return new DayPlan(AttendanceRules.Works(week6, day.DayOfWeek), null, holiday);
    }

    /// <summary>The minutes of a working day without shift times: the rule set's hours a week over its working days.</summary>
    public int StandardMinutes(Employee employee)
    {
        var rules = RulesOf(employee);
        decimal hours = rules is null ? 8m : rules.HoursPerWeek / Math.Max(1, rules.WorkingDaysPerWeek);
        return (int)Math.Round(hours * 60m, MidpointRounding.AwayFromZero);
    }

    /// <summary>True when a day counts towards a leave request of the type: a day the employee is expected to work, or any day for calendar-day leave.</summary>
    public bool CountsForLeave(Employee employee, LeaveType type, DateOnly day) => type.CountsCalendarDays || PlanOn(employee, day).Expected;
}
