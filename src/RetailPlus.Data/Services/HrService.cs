using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>What the employee list is filtered by. <c>Active</c>: true for those still employed on <c>AsOf</c>, false for those who left.</summary>
public sealed record EmployeeQuery(string? Search = null, Guid? DepartmentId = null, Guid? DesignationId = null, bool? Active = null, DateOnly? AsOf = null);

/// <summary>A document of an employee without its content, for lists.</summary>
public sealed record EmployeeDocumentInfo(Guid Id, Guid EmployeeId, string Title, string FileName, string ContentType, long Size, DateTime UploadedAtUtc, string? UploadedBy);

/// <summary>
/// Human resources master data: the country rule sets (payroll rules, leave types, holidays) with Pakistan seeded, the
/// HR departments and designations, employee records and their documents. Everything is kept on the server. Lists are
/// limited to <see cref="DataContext.StoreScope"/> when it is set; employees of head office (no store) are seen only
/// without a store scope.
/// </summary>
public sealed class HrService(DataContext data)
{
    /// <summary>The store setting extra that names the country whose rule sets a store uses (two letters); without it, the ledger's country.</summary>
    public const string CountryExtra = "COUNTRY_RULES";
    public const string DefaultCountry = "PK";
    /// <summary>The largest document kept with an employee.</summary>
    public const int MaxDocumentBytes = 10 * 1024 * 1024;

    // Seed ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The note kept on Pakistan's seeded rule set and shown on the settings page: the figures change with every budget.
    /// </summary>
    public const string PakistanNote =
        "Seeded from the Finance Act 2025 (tax year 2026, July 2025 to June 2026), the latest salaried slabs the program was given, " +
        "with the 2025-26 minimum wage of Punjab, EOBI at 5% and 1% of the minimum wage, and PESSI (Punjab) at 6%. " +
        "Check every figure after each budget (June) against the Finance Act, the provincial minimum wage notification, EOBI and the " +
        "social security institution, and add the new tax year's rule set from its first day (1 July). Lateness and paternity leave " +
        "are company policy, not law.";

    /// <summary>Pakistan's payroll rules as seeded (see <see cref="PakistanNote"/>).</summary>
    public static PayrollRuleSet PakistanRuleSet() => new()
    {
        Country = "PK", Name = "Pakistan, tax year 2025-26 (Finance Act 2025)", EffectiveFrom = new DateOnly(2025, 7, 1), TaxYearStartMonth = 7,
        MinimumWage = 40000m, EobiEmployerRate = 5m, EobiEmployeeRate = 1m,
        SocialSecurityProvince = "Punjab (PESSI)", SocialSecurityRate = 6m, SocialSecurityWageCeiling = 40000m, SocialSecurityBasis = SocialSecurityBasis.Wage,
        WorkingDaysPerWeek = 6, HoursPerWeek = 48m, OvertimeMultiplier = 2m, OvertimeMinimumMinutes = 30, DailyRateBasis = DailyRateBasis.CalendarDays,
        LatenessRule = LatenessRule.DayPerLates, LatesPerDay = 3, DeductAbsences = true,
        SurchargeThreshold = 10_000_000m, SurchargeRate = 9m,
        GratuityEnabled = true, GratuityDaysPerYear = 30m, GratuityMinimumMonths = 12, GratuityPartYearMonths = 6, GratuityOnBasic = false,
        CnicPattern = @"^\d{5}-\d{7}-\d$", CnicExample = "35202-1234567-1",
        IbanPattern = "^PK[0-9]{2}[A-Z]{4}[0-9]{16}$", IbanExample = "PK36SCBL0000001123456702",
        Notes = PakistanNote,
        TaxSlabs =
        [
            new() { IncomeFrom = 0m, IncomeTo = 600_000m, FixedTax = 0m, Rate = 0m },
            new() { IncomeFrom = 600_000m, IncomeTo = 1_200_000m, FixedTax = 0m, Rate = 1m },
            new() { IncomeFrom = 1_200_000m, IncomeTo = 2_200_000m, FixedTax = 6_000m, Rate = 11m },
            new() { IncomeFrom = 2_200_000m, IncomeTo = 3_200_000m, FixedTax = 116_000m, Rate = 23m },
            new() { IncomeFrom = 3_200_000m, IncomeTo = 4_100_000m, FixedTax = 346_000m, Rate = 30m },
            new() { IncomeFrom = 4_100_000m, IncomeTo = null, FixedTax = 616_000m, Rate = 35m },
        ],
        Allowances =
        [
            new() { Code = "HRA", Name = "House rent", IsTaxable = true, SortOrder = 1 },
            new() { Code = "MEDICAL", Name = "Medical", IsTaxable = false, ExemptPercentOfBasic = 10m, SortOrder = 2 },
            new() { Code = "CONVEYANCE", Name = "Conveyance", IsTaxable = true, SortOrder = 3 },
            new() { Code = "UTILITIES", Name = "Utilities", IsTaxable = true, SortOrder = 4 },
            new() { Code = "OTHER", Name = "Other allowance", IsTaxable = true, SortOrder = 5 },
        ],
    };

    /// <summary>Pakistan's leave types as seeded (Shops and Establishments Ordinance and the Maternity Benefit Ordinance; paternity is company policy).</summary>
    public static List<LeaveType> PakistanLeaveTypes() =>
    [
        new() { Country = "PK", Code = "AL", Name = "Annual leave", Kind = LeaveKind.Annual, DaysPerYear = 14, IsPaid = true, AccruesMonthly = true, CarryOverMaxDays = 14, Encashable = true, MinimumServiceMonths = 12, SortOrder = 1 },
        new() { Country = "PK", Code = "CL", Name = "Casual leave", Kind = LeaveKind.Casual, DaysPerYear = 10, IsPaid = true, SortOrder = 2 },
        new() { Country = "PK", Code = "SL", Name = "Sick leave", Kind = LeaveKind.Sick, DaysPerYear = 8, IsPaid = true, SortOrder = 3 },
        new() { Country = "PK", Code = "ML", Name = "Maternity leave", Kind = LeaveKind.Maternity, DaysPerYear = 84, IsPaid = true, CountsCalendarDays = true, OnlyFor = Gender.Female, MinimumServiceMonths = 4, SortOrder = 4 },
        new() { Country = "PK", Code = "PL", Name = "Paternity leave", Kind = LeaveKind.Paternity, DaysPerYear = 7, IsPaid = true, OnlyFor = Gender.Male, SortOrder = 5 },
        new() { Country = "PK", Code = "UL", Name = "Unpaid leave", Kind = LeaveKind.Unpaid, DaysPerYear = 0, IsPaid = false, SortOrder = 6 },
    ];

    /// <summary>
    /// Pakistan's gazetted public holidays of 2026. The Islamic holidays follow the moon, so their dates are the expected
    /// ones and must be checked against the Cabinet Division's notification.
    /// </summary>
    public static IReadOnlyList<(DateOnly Date, string Name)> PakistanHolidays2026 { get; } =
    [
        (new(2026, 2, 5), "Kashmir Solidarity Day"),
        (new(2026, 3, 20), "Eid ul Fitr"),
        (new(2026, 3, 21), "Eid ul Fitr"),
        (new(2026, 3, 22), "Eid ul Fitr"),
        (new(2026, 3, 23), "Pakistan Day"),
        (new(2026, 5, 1), "Labour Day"),
        (new(2026, 5, 27), "Eid ul Azha"),
        (new(2026, 5, 28), "Eid ul Azha; Youm-e-Takbeer"),
        (new(2026, 5, 29), "Eid ul Azha"),
        (new(2026, 6, 25), "Ashura (9 Muharram)"),
        (new(2026, 6, 26), "Ashura (10 Muharram)"),
        (new(2026, 8, 14), "Independence Day"),
        (new(2026, 8, 26), "Eid Milad un Nabi"),
        (new(2026, 11, 9), "Iqbal Day"),
        (new(2026, 12, 25), "Quaid-e-Azam Day; Christmas"),
    ];

    /// <summary>Gives Pakistan its rule set, leave types and holidays when it has none. Running it again changes nothing.</summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = data.Create();
        db.SuppressAudit = true;
        if (!await db.PayrollRuleSets.AnyAsync(r => r.Country == "PK", cancellationToken)) db.PayrollRuleSets.Add(PakistanRuleSet());
        if (!await db.LeaveTypes.AnyAsync(t => t.Country == "PK", cancellationToken)) db.LeaveTypes.AddRange(PakistanLeaveTypes());
        if (!await db.Holidays.AnyAsync(h => h.Country == "PK", cancellationToken))
            db.Holidays.AddRange(PakistanHolidays2026.Select(h => new Holiday { Country = "PK", Date = h.Date, Name = h.Name }));
        await db.SaveChangesAsync(cancellationToken);
    }

    // Countries ----------------------------------------------------------------------------------------------------------

    /// <summary>The company's country (the ledger settings), PK by default.</summary>
    public async Task<string> CompanyCountryAsync()
    {
        await using var db = data.Create();
        return await CompanyCountryAsync(db);
    }

    internal static async Task<string> CompanyCountryAsync(RetailPlusDbContext db) =>
        await db.LedgerSettings.AsNoTracking().Select(s => s.Country).FirstOrDefaultAsync() is { Length: 2 } c ? c : DefaultCountry;

    /// <summary>Each store's country for the rule sets: its own setting, else the company's.</summary>
    public async Task<Dictionary<Guid, string>> StoreCountriesAsync()
    {
        await using var db = data.Create();
        return await StoreCountriesAsync(db);
    }

    internal static async Task<Dictionary<Guid, string>> StoreCountriesAsync(RetailPlusDbContext db)
    {
        var company = await CompanyCountryAsync(db);
        var own = await db.StoreSettingExtras.AsNoTracking().Where(x => x.Name == CountryExtra).ToDictionaryAsync(x => x.StoreId, x => x.Value);
        return (await db.Stores.AsNoTracking().Select(s => s.Id).ToListAsync())
            .ToDictionary(s => s, s => own.TryGetValue(s, out var c) && c is { Length: 2 } ? c.ToUpperInvariant() : company);
    }

    /// <summary>The country whose rule sets apply to a store (null: head office, the company's country).</summary>
    public async Task<string> CountryOfStoreAsync(Guid? storeId)
    {
        await using var db = data.Create();
        var countries = await StoreCountriesAsync(db);
        return storeId is Guid s && countries.TryGetValue(s, out var c) ? c : await CompanyCountryAsync(db);
    }

    /// <summary>Names the country of a store's rule sets; null or empty goes back to the company's country.</summary>
    public async Task SetStoreCountryAsync(Guid storeId, string? country)
    {
        var code = country?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(code) && (code.Length != 2 || !code.All(char.IsAsciiLetterUpper)))
            throw new BusinessRuleException("The country is a two-letter code, such as PK.");
        await using var db = data.Create();
        if (!await db.Stores.AnyAsync(s => s.Id == storeId)) throw new BusinessRuleException("That store no longer exists.");
        var row = await db.StoreSettingExtras.FirstOrDefaultAsync(x => x.StoreId == storeId && x.Name == CountryExtra);
        if (string.IsNullOrEmpty(code))
        {
            if (row is not null) db.StoreSettingExtras.Remove(row);
        }
        else if (row is null) db.StoreSettingExtras.Add(new StoreSettingExtra { StoreId = storeId, Name = CountryExtra, Value = code });
        else row.Value = code;
        await db.SaveChangesAsync();
    }

    // Rule sets ----------------------------------------------------------------------------------------------------------

    public async Task<List<PayrollRuleSet>> GetRuleSetsAsync(string? country = null)
    {
        await using var db = data.Create();
        var q = db.PayrollRuleSets.AsNoTracking().Include(r => r.TaxSlabs).Include(r => r.Allowances).AsQueryable();
        var code = country?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(code)) q = q.Where(r => r.Country == code);
        var sets = await q.OrderBy(r => r.Country).ThenByDescending(r => r.EffectiveFrom).ToListAsync();
        foreach (var s in sets) Order(s);
        return sets;
    }

    public async Task<PayrollRuleSet?> GetRuleSetAsync(Guid id)
    {
        await using var db = data.Create();
        var set = await db.PayrollRuleSets.AsNoTracking().Include(r => r.TaxSlabs).Include(r => r.Allowances).SingleOrDefaultAsync(r => r.Id == id);
        return set is null ? null : Order(set);
    }

    /// <summary>The rule set of a country in effect on a day: the latest that starts by then and has not ended.</summary>
    public async Task<PayrollRuleSet> RuleSetForAsync(string country, DateOnly day)
    {
        await using var db = data.Create();
        return await RuleSetForAsync(db, country, day) ?? throw new BusinessRuleException($"There are no payroll rules for {country} on {day:d}; add a rule set in HR settings.");
    }

    internal static async Task<PayrollRuleSet?> RuleSetForAsync(RetailPlusDbContext db, string country, DateOnly day)
    {
        var set = await db.PayrollRuleSets.AsNoTracking().Include(r => r.TaxSlabs).Include(r => r.Allowances)
            .Where(r => r.Country == country && r.EffectiveFrom <= day && (r.EffectiveTo == null || r.EffectiveTo >= day))
            .OrderByDescending(r => r.EffectiveFrom).FirstOrDefaultAsync();
        return set is null ? null : Order(set);
    }

    private static PayrollRuleSet Order(PayrollRuleSet set)
    {
        set.TaxSlabs = [.. set.TaxSlabs.OrderBy(s => s.IncomeFrom)];
        set.Allowances = [.. set.Allowances.OrderBy(a => a.SortOrder).ThenBy(a => a.Code, StringComparer.Ordinal)];
        return set;
    }

    /// <summary>Adds a rule set, or changes one with its slabs and allowance rules (which are replaced as given).</summary>
    public async Task<PayrollRuleSet> SaveRuleSetAsync(PayrollRuleSet set)
    {
        Check(set);
        var country = set.Country.Trim().ToUpperInvariant();
        await using var db = data.Create();
        if (await db.PayrollRuleSets.AnyAsync(r => r.Country == country && r.EffectiveFrom == set.EffectiveFrom && r.Id != set.Id))
            throw new BusinessRuleException($"{country} already has a rule set from {set.EffectiveFrom:d}.");
        var row = set.Id == Guid.Empty ? null : await db.PayrollRuleSets.Include(r => r.TaxSlabs).Include(r => r.Allowances).SingleOrDefaultAsync(r => r.Id == set.Id);
        if (row is null)
        {
            row = new PayrollRuleSet();
            row.Id = set.Id == Guid.Empty ? Guid.CreateVersion7() : set.Id;
            db.PayrollRuleSets.Add(row);
        }
        else
        {
            db.PayrollTaxSlabs.RemoveRange(row.TaxSlabs);
            db.PayrollAllowanceRules.RemoveRange(row.Allowances);
            row.TaxSlabs.Clear();
            row.Allowances.Clear();
        }
        row.Country = country;
        row.Name = set.Name.Trim();
        row.EffectiveFrom = set.EffectiveFrom;
        row.EffectiveTo = set.EffectiveTo;
        row.TaxYearStartMonth = set.TaxYearStartMonth;
        row.MinimumWage = Money.Round(set.MinimumWage);
        row.EobiEmployerRate = set.EobiEmployerRate;
        row.EobiEmployeeRate = set.EobiEmployeeRate;
        row.SocialSecurityProvince = Trimmed(set.SocialSecurityProvince);
        row.SocialSecurityRate = set.SocialSecurityRate;
        row.SocialSecurityWageCeiling = Money.Round(set.SocialSecurityWageCeiling);
        row.SocialSecurityBasis = set.SocialSecurityBasis;
        row.WorkingDaysPerWeek = set.WorkingDaysPerWeek;
        row.HoursPerWeek = set.HoursPerWeek;
        row.OvertimeMultiplier = set.OvertimeMultiplier;
        row.OvertimeMinimumMinutes = set.OvertimeMinimumMinutes;
        row.DailyRateBasis = set.DailyRateBasis;
        row.LatenessRule = set.LatenessRule;
        row.LatesPerDay = set.LatesPerDay;
        row.DeductAbsences = set.DeductAbsences;
        row.SurchargeThreshold = Money.Round(set.SurchargeThreshold);
        row.SurchargeRate = set.SurchargeRate;
        row.GratuityEnabled = set.GratuityEnabled;
        row.GratuityDaysPerYear = set.GratuityDaysPerYear;
        row.GratuityMinimumMonths = set.GratuityMinimumMonths;
        row.GratuityPartYearMonths = set.GratuityPartYearMonths;
        row.GratuityOnBasic = set.GratuityOnBasic;
        row.CnicPattern = Trimmed(set.CnicPattern);
        row.CnicExample = Trimmed(set.CnicExample);
        row.IbanPattern = Trimmed(set.IbanPattern);
        row.IbanExample = Trimmed(set.IbanExample);
        row.Notes = Trimmed(set.Notes);
        foreach (var s in set.TaxSlabs.OrderBy(s => s.IncomeFrom))
            row.TaxSlabs.Add(new PayrollTaxSlab { RuleSetId = row.Id, IncomeFrom = Money.Round(s.IncomeFrom), IncomeTo = s.IncomeTo is { } to ? Money.Round(to) : null, FixedTax = Money.Round(s.FixedTax), Rate = s.Rate });
        int order = 0;
        foreach (var a in set.Allowances)
            row.Allowances.Add(new PayrollAllowanceRule
            {
                RuleSetId = row.Id, Code = a.Code.Trim().ToUpperInvariant(), Name = a.Name.Trim(), IsTaxable = a.IsTaxable,
                ExemptPercentOfBasic = a.IsTaxable ? null : a.ExemptPercentOfBasic, SortOrder = a.SortOrder == 0 ? ++order : a.SortOrder,
            });
        await db.SaveChangesAsync();
        return Order(row);
    }

    private static void Check(PayrollRuleSet s)
    {
        if (s.Country.Trim().Length != 2) throw new BusinessRuleException("The country is a two-letter code, such as PK.");
        if (string.IsNullOrWhiteSpace(s.Name) || s.Name.Trim().Length > 80) throw new BusinessRuleException("Give the rule set a name of up to 80 characters.");
        if (s.EffectiveTo is { } to && to < s.EffectiveFrom) throw new BusinessRuleException("The rule set ends before it starts.");
        if (s.TaxYearStartMonth is < 1 or > 12) throw new BusinessRuleException("The tax year starts in a month from 1 to 12.");
        if (s.MinimumWage < 0 || s.SocialSecurityWageCeiling < 0 || s.SurchargeThreshold < 0) throw new BusinessRuleException("Wages and thresholds cannot be below zero.");
        foreach (var (rate, what) in new[] { (s.EobiEmployerRate, "The EOBI employer rate"), (s.EobiEmployeeRate, "The EOBI employee rate"), (s.SocialSecurityRate, "The social security rate"), (s.SurchargeRate, "The surcharge") })
            if (rate is < 0 or > 100) throw new BusinessRuleException($"{what} is a percentage from 0 to 100.");
        if (s.WorkingDaysPerWeek is < 1 or > 7) throw new BusinessRuleException("A week has 1 to 7 working days.");
        if (s.HoursPerWeek is <= 0 or > 168) throw new BusinessRuleException("Hours a week are more than 0 and at most 168.");
        if (s.OvertimeMultiplier < 1) throw new BusinessRuleException("Overtime is paid at least at the hourly rate (multiplier 1 or more).");
        if (s.OvertimeMinimumMinutes is < 0 or > 600) throw new BusinessRuleException("The least overtime counted is 0 to 600 minutes.");
        if (s.LatenessRule == LatenessRule.DayPerLates && s.LatesPerDay < 1) throw new BusinessRuleException("Say how many late arrivals cost a day's pay.");
        if (s.GratuityDaysPerYear < 0 || s.GratuityMinimumMonths < 0 || s.GratuityPartYearMonths is < 0 or > 11) throw new BusinessRuleException("The gratuity rules are not valid.");
        if (!PayrollRules.IsValidPattern(s.CnicPattern)) throw new BusinessRuleException("The identity number format is not a valid pattern.");
        if (!PayrollRules.IsValidPattern(s.IbanPattern)) throw new BusinessRuleException("The IBAN format is not a valid pattern.");
        var slabs = s.TaxSlabs.OrderBy(x => x.IncomeFrom).ToList();
        if (slabs.Select(x => x.IncomeFrom).Distinct().Count() != slabs.Count) throw new BusinessRuleException("Two tax slabs start at the same income.");
        foreach (var slab in slabs)
        {
            if (slab.IncomeFrom < 0 || slab.FixedTax < 0) throw new BusinessRuleException("Tax slab amounts cannot be below zero.");
            if (slab.Rate is < 0 or > 100) throw new BusinessRuleException("A tax slab's rate is a percentage from 0 to 100.");
            if (slab.IncomeTo is { } top && top <= slab.IncomeFrom) throw new BusinessRuleException($"The slab from {slab.IncomeFrom:N0} ends before it starts.");
        }
        var codes = s.Allowances.Select(a => a.Code.Trim().ToUpperInvariant()).ToList();
        if (codes.Any(c => c.Length is 0 or > 20)) throw new BusinessRuleException("Every allowance has a code of up to 20 characters.");
        if (codes.Distinct(StringComparer.Ordinal).Count() != codes.Count) throw new BusinessRuleException("Two allowances have the same code.");
        if (s.Allowances.Any(a => string.IsNullOrWhiteSpace(a.Name))) throw new BusinessRuleException("Every allowance has a name.");
        if (s.Allowances.Any(a => a.ExemptPercentOfBasic is < 0 or > 1000)) throw new BusinessRuleException("An exempt share of basic pay is a percentage from 0.");
    }

    public async Task DeleteRuleSetAsync(Guid id)
    {
        await using var db = data.Create();
        var row = await db.PayrollRuleSets.Include(r => r.TaxSlabs).Include(r => r.Allowances).SingleOrDefaultAsync(r => r.Id == id)
            ?? throw new BusinessRuleException("That rule set no longer exists.");
        DateOnly from = row.EffectiveFrom, to = row.EffectiveTo ?? DateOnly.MaxValue;
        if (await db.PayrollRuns.AnyAsync(r => (r.Status == PayrollRunStatus.Approved || r.Status == PayrollRunStatus.Posted || r.Status == PayrollRunStatus.Paid)
                && r.PeriodEnd >= from && r.PeriodEnd <= to))
            throw new BusinessRuleException("Payroll runs were approved under this rule set; end it with a date instead.");
        db.PayrollRuleSets.Remove(row);
        await db.SaveChangesAsync();
    }

    // Departments and designations ------------------------------------------------------------------------------------

    public async Task<List<HrDepartment>> GetDepartmentsAsync()
    {
        await using var db = data.Create();
        return await db.HrDepartments.AsNoTracking().OrderBy(d => d.Name).ToListAsync();
    }

    public async Task<HrDepartment> SaveDepartmentAsync(HrDepartment department)
    {
        var name = ListName(department.Name, "department");
        await using var db = data.Create();
        if (await db.HrDepartments.AnyAsync(d => d.Name == name && d.Id != department.Id)) throw new BusinessRuleException($"There is already a department {name}.");
        var row = department.Id == Guid.Empty ? null : await db.HrDepartments.SingleOrDefaultAsync(d => d.Id == department.Id);
        if (row is null) db.HrDepartments.Add(row = new HrDepartment());
        row.Name = name;
        row.IsActive = department.IsActive;
        await db.SaveChangesAsync();
        return row;
    }

    public async Task DeleteDepartmentAsync(Guid id)
    {
        await using var db = data.Create();
        var row = await db.HrDepartments.SingleOrDefaultAsync(d => d.Id == id) ?? throw new BusinessRuleException("That department no longer exists.");
        if (await db.Employees.AnyAsync(e => e.HrDepartmentId == id)) throw new BusinessRuleException($"Employees are in {row.Name}; make it inactive instead.");
        db.HrDepartments.Remove(row);
        await db.SaveChangesAsync();
    }

    public async Task<List<Designation>> GetDesignationsAsync()
    {
        await using var db = data.Create();
        return await db.Designations.AsNoTracking().OrderBy(d => d.Name).ToListAsync();
    }

    public async Task<Designation> SaveDesignationAsync(Designation designation)
    {
        var name = ListName(designation.Name, "designation");
        await using var db = data.Create();
        if (await db.Designations.AnyAsync(d => d.Name == name && d.Id != designation.Id)) throw new BusinessRuleException($"There is already a designation {name}.");
        var row = designation.Id == Guid.Empty ? null : await db.Designations.SingleOrDefaultAsync(d => d.Id == designation.Id);
        if (row is null) db.Designations.Add(row = new Designation());
        row.Name = name;
        row.IsActive = designation.IsActive;
        await db.SaveChangesAsync();
        return row;
    }

    public async Task DeleteDesignationAsync(Guid id)
    {
        await using var db = data.Create();
        var row = await db.Designations.SingleOrDefaultAsync(d => d.Id == id) ?? throw new BusinessRuleException("That designation no longer exists.");
        if (await db.Employees.AnyAsync(e => e.DesignationId == id)) throw new BusinessRuleException($"Employees are {row.Name}s; make it inactive instead.");
        db.Designations.Remove(row);
        await db.SaveChangesAsync();
    }

    private static string ListName(string? name, string what)
    {
        var text = name?.Trim() ?? "";
        if (text.Length is 0 or > 60) throw new BusinessRuleException($"A {what} needs a name of up to 60 characters.");
        return text;
    }

    // Leave types and holidays ------------------------------------------------------------------------------------------

    public async Task<List<LeaveType>> GetLeaveTypesAsync(string? country = null)
    {
        await using var db = data.Create();
        var q = db.LeaveTypes.AsNoTracking();
        var code = country?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(code)) q = q.Where(t => t.Country == code);
        return await q.OrderBy(t => t.Country).ThenBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync();
    }

    public async Task<LeaveType> SaveLeaveTypeAsync(LeaveType type)
    {
        var country = type.Country.Trim().ToUpperInvariant();
        var code = type.Code.Trim().ToUpperInvariant();
        if (country.Length != 2) throw new BusinessRuleException("The country is a two-letter code, such as PK.");
        if (code.Length is 0 or > 10) throw new BusinessRuleException("A leave type needs a code of up to 10 characters.");
        if (string.IsNullOrWhiteSpace(type.Name) || type.Name.Trim().Length > 60) throw new BusinessRuleException("A leave type needs a name of up to 60 characters.");
        if (type.DaysPerYear is < 0 or > 366 || type.CarryOverMaxDays is < 0 or > 366) throw new BusinessRuleException("Days a year and days carried over are 0 to 366.");
        if (type.MinimumServiceMonths is < 0 or > 600) throw new BusinessRuleException("The service needed is 0 to 600 months.");
        await using var db = data.Create();
        if (await db.LeaveTypes.AnyAsync(t => t.Country == country && t.Code == code && t.Id != type.Id)) throw new BusinessRuleException($"{country} already has a leave type {code}.");
        var row = type.Id == Guid.Empty ? null : await db.LeaveTypes.SingleOrDefaultAsync(t => t.Id == type.Id);
        if (row is null) db.LeaveTypes.Add(row = new LeaveType());
        else if (row.Country != country && await db.LeaveRequests.AnyAsync(r => r.LeaveTypeId == row.Id))
            throw new BusinessRuleException("Leave was taken of this type, so its country cannot change.");
        row.Country = country;
        row.Code = code;
        row.Name = type.Name.Trim();
        row.Kind = type.Kind;
        row.DaysPerYear = type.DaysPerYear;
        row.IsPaid = type.IsPaid;
        row.AccruesMonthly = type.AccruesMonthly;
        row.CarryOverMaxDays = type.CarryOverMaxDays;
        row.Encashable = type.Encashable;
        row.CountsCalendarDays = type.CountsCalendarDays;
        row.OnlyFor = type.OnlyFor;
        row.MinimumServiceMonths = type.MinimumServiceMonths;
        row.IsActive = type.IsActive;
        row.SortOrder = type.SortOrder;
        await db.SaveChangesAsync();
        return row;
    }

    public async Task DeleteLeaveTypeAsync(Guid id)
    {
        await using var db = data.Create();
        var row = await db.LeaveTypes.SingleOrDefaultAsync(t => t.Id == id) ?? throw new BusinessRuleException("That leave type no longer exists.");
        if (await db.LeaveRequests.AnyAsync(r => r.LeaveTypeId == id) || await db.LeaveAdjustments.AnyAsync(a => a.LeaveTypeId == id))
            throw new BusinessRuleException($"{row.Name} has been used; make it inactive instead.");
        db.LeaveTypes.Remove(row);
        await db.SaveChangesAsync();
    }

    public async Task<List<Holiday>> GetHolidaysAsync(string? country = null, int? year = null)
    {
        await using var db = data.Create();
        var q = db.Holidays.AsNoTracking();
        var code = country?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(code)) q = q.Where(h => h.Country == code);
        if (year is int y) q = q.Where(h => h.Date >= new DateOnly(y, 1, 1) && h.Date <= new DateOnly(y, 12, 31));
        return await q.OrderBy(h => h.Date).ThenBy(h => h.Country).ToListAsync();
    }

    public async Task<Holiday> SaveHolidayAsync(Holiday holiday)
    {
        var country = holiday.Country.Trim().ToUpperInvariant();
        if (country.Length != 2) throw new BusinessRuleException("The country is a two-letter code, such as PK.");
        if (string.IsNullOrWhiteSpace(holiday.Name) || holiday.Name.Trim().Length > 80) throw new BusinessRuleException("A holiday needs a name of up to 80 characters.");
        if (holiday.Date == default) throw new BusinessRuleException("Give the holiday's date.");
        await using var db = data.Create();
        if (await db.Holidays.AnyAsync(h => h.Country == country && h.Date == holiday.Date && h.Id != holiday.Id))
            throw new BusinessRuleException($"{country} already has a holiday on {holiday.Date:d}; change that one instead.");
        var row = holiday.Id == Guid.Empty ? null : await db.Holidays.SingleOrDefaultAsync(h => h.Id == holiday.Id);
        if (row is null) db.Holidays.Add(row = new Holiday());
        row.Country = country;
        row.Date = holiday.Date;
        row.Name = holiday.Name.Trim();
        await db.SaveChangesAsync();
        return row;
    }

    public async Task DeleteHolidayAsync(Guid id)
    {
        await using var db = data.Create();
        var row = await db.Holidays.SingleOrDefaultAsync(h => h.Id == id) ?? throw new BusinessRuleException("That holiday no longer exists.");
        db.Holidays.Remove(row);
        await db.SaveChangesAsync();
    }

    // Employees ----------------------------------------------------------------------------------------------------------

    /// <summary>The employees of the store scope matching the query, by name.</summary>
    public async Task<List<Employee>> GetEmployeesAsync(EmployeeQuery? query = null)
    {
        await using var db = data.Create();
        return await Query(db, query ?? new EmployeeQuery(), data.StoreScope).OrderBy(e => e.FullName).ToListAsync();
    }

    /// <summary>The employee query the lists and the server's pages use.</summary>
    public static IQueryable<Employee> Query(RetailPlusDbContext db, EmployeeQuery query, Guid? store)
    {
        var q = db.Employees.AsNoTracking();
        if (store is Guid s) q = q.Where(e => e.StoreId == s);
        if (query.DepartmentId is Guid d) q = q.Where(e => e.HrDepartmentId == d);
        if (query.DesignationId is Guid g) q = q.Where(e => e.DesignationId == g);
        var day = query.AsOf ?? DateOnly.FromDateTime(DateTime.Today);
        if (query.Active == true) q = q.Where(e => e.LeavingDate == null || e.LeavingDate >= day);
        if (query.Active == false) q = q.Where(e => e.LeavingDate != null && e.LeavingDate < day);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var t = query.Search.Trim();
            q = q.Where(e => e.FullName.Contains(t) || e.Code.Contains(t) || (e.Cnic ?? "").Contains(t) || (e.Phone ?? "").Contains(t));
        }
        return q;
    }

    public async Task<Employee?> GetEmployeeAsync(Guid id)
    {
        await using var db = data.Create();
        return await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id);
    }

    /// <summary>The next free employee code (E0001, E0002…).</summary>
    public async Task<string> NextCodeAsync()
    {
        await using var db = data.Create();
        return await NextCodeAsync(db);
    }

    private static async Task<string> NextCodeAsync(RetailPlusDbContext db)
    {
        var codes = await db.Employees.AsNoTracking().Select(e => e.Code).ToListAsync();
        int top = codes.Where(c => c.StartsWith('E')).Select(c => int.TryParse(c.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return "E" + (top + 1).ToString("0000", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Adds or changes an employee. The CNIC and IBAN are checked against the formats of the rule set of the employee's
    /// country; a code is made when none is given; a user account is linked to one employee at most.
    /// </summary>
    public async Task<Employee> SaveEmployeeAsync(Employee employee)
    {
        var name = employee.FullName?.Trim() ?? "";
        if (name.Length is 0 or > 100) throw new BusinessRuleException("Give the employee's full name (up to 100 characters).");
        if (employee.JoiningDate == default) throw new BusinessRuleException("Give the date the employee joined.");
        if (employee.LeavingDate is { } left && left < employee.JoiningDate) throw new BusinessRuleException("The leaving date is before the joining date.");
        if (employee.ConfirmationDate is { } confirmed && confirmed < employee.JoiningDate) throw new BusinessRuleException("The confirmation date is before the joining date.");
        if (employee.DateOfBirth is { } born && born >= employee.JoiningDate) throw new BusinessRuleException("The date of birth must be before the joining date.");
        if (employee.Email is { Length: > 0 } email && !email.Contains('@', StringComparison.Ordinal)) throw new BusinessRuleException($"{email} is not an email address.");
        if (employee.PriorTaxableIncome < 0 || employee.PriorTaxDeducted < 0) throw new BusinessRuleException("Income and tax from a previous employer cannot be below zero.");

        await using var db = data.Create();
        var countries = await StoreCountriesAsync(db);
        var country = employee.StoreId is Guid sid && countries.TryGetValue(sid, out var c) ? c : await CompanyCountryAsync(db);
        var rules = await RuleSetForAsync(db, country, DateOnly.FromDateTime(DateTime.Today))
            ?? await db.PayrollRuleSets.AsNoTracking().Where(r => r.Country == country).OrderByDescending(r => r.EffectiveFrom).FirstOrDefaultAsync();

        var cnic = Trimmed(employee.Cnic);
        if (cnic is not null && !PayrollRules.Matches(cnic, rules?.CnicPattern))
            throw new BusinessRuleException($"{cnic} is not an identity number in the form {rules?.CnicExample ?? "of the country"}.");
        var iban = employee.Iban is { Length: > 0 } raw ? PayrollRules.NormalizeIban(raw) : null;
        if (iban is { Length: 0 }) iban = null;
        if (iban is not null && (!PayrollRules.Matches(iban, rules?.IbanPattern) || !PayrollRules.IbanChecksumValid(iban)))
            throw new BusinessRuleException($"{iban} is not a valid IBAN{(rules?.IbanExample is { } example ? $" (like {example})" : "")}.");
        if (employee.PaymentMethod == SalaryPaymentMethod.Bank && iban is null) throw new BusinessRuleException("A salary paid by bank transfer needs the IBAN.");

        if (employee.StoreId is Guid store && !await db.Stores.AnyAsync(s => s.Id == store)) throw new BusinessRuleException("That store no longer exists.");
        if (employee.HrDepartmentId is Guid dept && !await db.HrDepartments.AnyAsync(d => d.Id == dept)) throw new BusinessRuleException("That department no longer exists.");
        if (employee.DesignationId is Guid des && !await db.Designations.AnyAsync(d => d.Id == des)) throw new BusinessRuleException("That designation no longer exists.");
        if (employee.ShiftId is Guid shift && !await db.Shifts.AnyAsync(s => s.Id == shift)) throw new BusinessRuleException("That shift no longer exists.");
        if (employee.UserAccountId is Guid user)
        {
            if (!await db.UserAccounts.AnyAsync(u => u.Id == user)) throw new BusinessRuleException("That user no longer exists.");
            var other = await db.Employees.AsNoTracking().Where(e => e.UserAccountId == user && e.Id != employee.Id).Select(e => e.Code + " " + e.FullName).FirstOrDefaultAsync();
            if (other is not null) throw new BusinessRuleException($"That user is already linked to {other}.");
        }
        if (employee.ManagerId is Guid manager)
        {
            // No one reports to themselves, directly or through others.
            var seen = new HashSet<Guid> { employee.Id };
            for (Guid? m = manager; m is Guid next; m = await db.Employees.AsNoTracking().Where(e => e.Id == next).Select(e => e.ManagerId).SingleOrDefaultAsync())
            {
                if (!seen.Add(next)) throw new BusinessRuleException("An employee cannot report to themselves, directly or through others.");
                if (!await db.Employees.AnyAsync(e => e.Id == next)) throw new BusinessRuleException("That manager is not an employee.");
            }
        }

        var code = Trimmed(employee.Code)?.ToUpperInvariant() ?? await NextCodeAsync(db);
        if (code.Length > 20) throw new BusinessRuleException("An employee code has up to 20 characters.");
        if (await db.Employees.AnyAsync(e => e.Code == code && e.Id != employee.Id)) throw new BusinessRuleException($"Employee code {code} is taken.");

        var row = employee.Id == Guid.Empty ? null : await db.Employees.SingleOrDefaultAsync(e => e.Id == employee.Id);
        if (row is null)
        {
            row = new Employee();
            if (employee.Id != Guid.Empty) row.Id = employee.Id;
            db.Employees.Add(row);
        }
        row.Code = code;
        row.FullName = name;
        row.FatherOrHusbandName = Trimmed(employee.FatherOrHusbandName);
        row.Cnic = cnic;
        row.DateOfBirth = employee.DateOfBirth;
        row.Gender = employee.Gender;
        row.MaritalStatus = employee.MaritalStatus;
        row.Address = Trimmed(employee.Address);
        row.City = Trimmed(employee.City);
        row.Phone = Trimmed(employee.Phone);
        row.Email = Trimmed(employee.Email);
        row.EmergencyContactName = Trimmed(employee.EmergencyContactName);
        row.EmergencyContactPhone = Trimmed(employee.EmergencyContactPhone);
        row.EmergencyContactRelation = Trimmed(employee.EmergencyContactRelation);
        row.JoiningDate = employee.JoiningDate;
        row.ConfirmationDate = employee.ConfirmationDate;
        row.LeavingDate = employee.LeavingDate;
        row.LeavingReason = employee.LeavingDate is null ? null : Trimmed(employee.LeavingReason);
        row.EmploymentType = employee.EmploymentType;
        row.HrDepartmentId = employee.HrDepartmentId;
        row.DesignationId = employee.DesignationId;
        row.StoreId = employee.StoreId;
        row.ManagerId = employee.ManagerId;
        row.UserAccountId = employee.UserAccountId;
        row.ShiftId = employee.ShiftId;
        row.TracksAttendance = employee.TracksAttendance;
        row.OvertimeEligible = employee.OvertimeEligible;
        row.EobiApplies = employee.EobiApplies;
        row.SocialSecurityApplies = employee.SocialSecurityApplies;
        row.PaymentMethod = employee.PaymentMethod;
        row.BankName = Trimmed(employee.BankName);
        row.Iban = iban;
        row.Ntn = Trimmed(employee.Ntn);
        row.EobiNumber = Trimmed(employee.EobiNumber);
        row.SocialSecurityNumber = Trimmed(employee.SocialSecurityNumber);
        row.PriorTaxYear = employee.PriorTaxYear;
        row.PriorTaxableIncome = Money.Round(employee.PriorTaxableIncome);
        row.PriorTaxDeducted = Money.Round(employee.PriorTaxDeducted);
        row.Notes = Trimmed(employee.Notes);
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>Deletes an employee added by mistake; one who was ever paid stays, with a leaving date.</summary>
    public async Task DeleteEmployeeAsync(Guid id)
    {
        await using var db = data.Create();
        var row = await db.Employees.SingleOrDefaultAsync(e => e.Id == id) ?? throw new BusinessRuleException("That employee no longer exists.");
        if (await db.Payslips.AnyAsync(p => p.EmployeeId == id) || await db.EmployeeLoans.AnyAsync(l => l.EmployeeId == id))
            throw new BusinessRuleException($"{row.FullName} has payslips or loans; give a leaving date instead.");
        if (await db.Employees.AnyAsync(e => e.ManagerId == id)) throw new BusinessRuleException($"Others report to {row.FullName}; give them another manager first.");
        if (await db.PayrollRuns.AnyAsync(r => r.EmployeeId == id)) throw new BusinessRuleException($"{row.FullName} has a final settlement.");
        db.Employees.Remove(row);
        await db.SaveChangesAsync();
    }

    // Documents ----------------------------------------------------------------------------------------------------------

    public async Task<List<EmployeeDocumentInfo>> GetDocumentsAsync(Guid employeeId)
    {
        await using var db = data.Create();
        return await db.EmployeeDocuments.AsNoTracking().Where(d => d.EmployeeId == employeeId).OrderByDescending(d => d.CreatedAtUtc)
            .Select(d => new EmployeeDocumentInfo(d.Id, d.EmployeeId, d.Title, d.FileName, d.ContentType, d.Size, d.CreatedAtUtc, d.UploadedBy)).ToListAsync();
    }

    public async Task<EmployeeDocument?> GetDocumentAsync(Guid id)
    {
        await using var db = data.Create();
        return await db.EmployeeDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id);
    }

    /// <summary>Keeps a file with an employee's record (at most <see cref="MaxDocumentBytes"/>).</summary>
    public async Task<EmployeeDocumentInfo> AddDocumentAsync(Guid employeeId, string? title, string fileName, string? contentType, byte[] content)
    {
        if (content.Length == 0) throw new BusinessRuleException("The file is empty.");
        if (content.Length > MaxDocumentBytes) throw new BusinessRuleException($"A document is at most {MaxDocumentBytes / 1024 / 1024} MB.");
        var file = Path.GetFileName(fileName ?? "").Trim();
        if (file.Length == 0) file = "document";
        if (file.Length > 200) file = file[^200..];
        await using var db = data.Create();
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == employeeId) ?? throw new BusinessRuleException("That employee no longer exists.");
        var row = new EmployeeDocument
        {
            EmployeeId = employeeId, Title = Cut(Trimmed(title) ?? Path.GetFileNameWithoutExtension(file), 100), FileName = file,
            ContentType = Cut(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim(), 100), Size = content.Length, Data = content,
            UploadedBy = Cut(data.CurrentUser?.UserName ?? "", 50),
        };
        db.EmployeeDocuments.Add(row);
        db.Audit(AuditActions.HrDocument, $"{employee.Code} {employee.FullName}: {row.Title} added", nameof(EmployeeDocument), row.Id);
        await db.SaveChangesAsync();
        return new EmployeeDocumentInfo(row.Id, row.EmployeeId, row.Title, row.FileName, row.ContentType, row.Size, row.CreatedAtUtc, row.UploadedBy);
    }

    public async Task DeleteDocumentAsync(Guid id)
    {
        await using var db = data.Create();
        var row = await db.EmployeeDocuments.SingleOrDefaultAsync(d => d.Id == id) ?? throw new BusinessRuleException("That document no longer exists.");
        var employee = await db.Employees.AsNoTracking().Where(e => e.Id == row.EmployeeId).Select(e => e.Code + " " + e.FullName).SingleAsync();
        db.EmployeeDocuments.Remove(row);
        db.Audit(AuditActions.HrDocument, $"{employee}: {row.Title} deleted", nameof(EmployeeDocument), row.Id);
        await db.SaveChangesAsync();
    }

    internal static string? Trimmed(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    internal static string Cut(string text, int length) => text.Length <= length ? text : text[..length];
}
