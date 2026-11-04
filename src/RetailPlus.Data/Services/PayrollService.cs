using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>The codes of the payslip lines the calculation makes; allowances and fixed deductions keep their own codes.</summary>
public static class PayslipCodes
{
    public const string Basic = "BASIC";
    public const string Overtime = "OVERTIME";
    public const string Commission = "COMMISSION";
    public const string LeaveEncashment = "ENCASHMENT";
    public const string Gratuity = "GRATUITY";
    public const string UnpaidLeave = "UNPAID";
    public const string Absence = "ABSENT";
    public const string Lateness = "LATE";
    public const string Tax = "TAX";
    public const string Eobi = "EOBI";
    public const string Loan = "LOAN";
    public const string EobiEmployer = "EOBI-ER";
    public const string SocialSecurity = "SS-ER";

    /// <summary>Deductions for time not worked: they lower the salary expense and the taxable income rather than being owed to anyone.</summary>
    public static bool IsAttendance(string code) => code is UnpaidLeave or Absence or Lateness;
}

/// <summary>
/// Payroll: each employee's salary structure with its history, loans and advances with their instalments, and payroll
/// runs (a month of a store or of the company, or a final settlement) from Draft through Calculated, Approved and Posted
/// to Paid. The calculation pays the days employed (joiners and leavers pro-rated), overtime and lateness from attendance,
/// commission from the commission rules, takes off unpaid leave and absence, income tax by the annualised projection,
/// the employee's EOBI and loan instalments, and adds the employer's EOBI and social security. Posting and paying go to
/// the general ledger through <see cref="LedgerService"/>. Runs and loans are numbered from the server's range.
/// </summary>
public sealed class PayrollService(DataContext data)
{
    /// <summary>The station range payroll runs and loans are numbered from: the server's.</summary>
    public const int PayrollStation = 1000;

    // Salary structures ------------------------------------------------------------------------------------------------------

    /// <summary>An employee's salaries, the latest first.</summary>
    public async Task<List<SalaryStructure>> GetSalaryHistoryAsync(Guid employeeId)
    {
        await using var db = data.Create();
        var rows = await db.SalaryStructures.AsNoTracking().Include(s => s.Components).Where(s => s.EmployeeId == employeeId)
            .OrderByDescending(s => s.EffectiveFrom).ToListAsync();
        foreach (var s in rows) s.Components = [.. s.Components.OrderBy(c => c.LineNumber)];
        return rows;
    }

    /// <summary>The salary in effect on a day: the latest that starts on or before it.</summary>
    public async Task<SalaryStructure?> SalaryOnAsync(Guid employeeId, DateOnly day) =>
        (await GetSalaryHistoryAsync(employeeId)).FirstOrDefault(s => s.EffectiveFrom <= day);

    /// <summary>
    /// Records a salary from a date: a new row, so the history stays (the same date replaces that row). Needs
    /// <see cref="Permissions.PayrollRun"/>. The audit log says that it changed, not the amounts.
    /// </summary>
    public async Task<SalaryStructure> SaveSalaryAsync(SalaryStructure salary, SessionInfo user)
    {
        Require(user, Permissions.PayrollRun, "change salaries");
        if (salary.Basic <= 0) throw new BusinessRuleException("The basic pay must be more than zero.");
        if (salary.EffectiveFrom == default) throw new BusinessRuleException("Give the date the salary starts.");
        foreach (var c in salary.Components)
        {
            if (string.IsNullOrWhiteSpace(c.Code) || c.Code.Trim().Length > 20) throw new BusinessRuleException("Every allowance and deduction has a code of up to 20 characters.");
            if (string.IsNullOrWhiteSpace(c.Name) || c.Name.Trim().Length > 60) throw new BusinessRuleException("Every allowance and deduction has a name of up to 60 characters.");
            if (c.Amount <= 0) throw new BusinessRuleException($"{c.Name} must be more than zero.");
            if (PayslipCodes.IsAttendance(c.Code.Trim().ToUpperInvariant()) || c.Code.Trim().ToUpperInvariant() is PayslipCodes.Basic or PayslipCodes.Tax or PayslipCodes.Eobi or PayslipCodes.Loan)
                throw new BusinessRuleException($"{c.Code} is a code the payroll keeps for itself.");
        }
        await using var db = data.Create();
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == salary.EmployeeId) ?? throw new BusinessRuleException("That employee no longer exists.");
        if (await FrozenAfterAsync(db, employee.Id, salary.EffectiveFrom) is { } run)
            throw new BusinessRuleException($"Payroll run #{run} already paid {employee.FullName} from then; give a later date.");
        var codes = await db.LedgerAccounts.AsNoTracking().Where(a => a.IsPosting && a.IsActive).Select(a => a.Code).ToListAsync();
        foreach (var c in salary.Components.Where(c => c.Kind == SalaryComponentKind.Deduction && !string.IsNullOrWhiteSpace(c.AccountCode)))
            if (!codes.Contains(c.AccountCode!.Trim())) throw new BusinessRuleException($"There is no account {c.AccountCode} to post {c.Name} to.");
        var row = await db.SalaryStructures.Include(s => s.Components).SingleOrDefaultAsync(s => s.EmployeeId == employee.Id && s.EffectiveFrom == salary.EffectiveFrom);
        if (row is null) db.SalaryStructures.Add(row = new SalaryStructure { EmployeeId = employee.Id, EffectiveFrom = salary.EffectiveFrom });
        else
        {
            db.SalaryComponents.RemoveRange(row.Components);
            row.Components.Clear();
        }
        row.Basic = Money.Round(salary.Basic);
        row.Note = HrService.Trimmed(salary.Note) is { } note ? HrService.Cut(note, 200) : null;
        row.ChangedBy = HrService.Cut(user.UserName, 50);
        int n = 0;
        foreach (var c in salary.Components)
            row.Components.Add(new SalaryComponent
            {
                LineNumber = ++n, Kind = c.Kind, Code = c.Code.Trim().ToUpperInvariant(), Name = c.Name.Trim(), Amount = Money.Round(c.Amount),
                AccountCode = c.Kind == SalaryComponentKind.Deduction ? HrService.Trimmed(c.AccountCode) : null,
            });
        db.Audit(AuditActions.SalaryChange, $"{employee.Code} {employee.FullName}: salary from {salary.EffectiveFrom.ToString("d", CultureInfo.CurrentCulture)}",
            nameof(SalaryStructure), row.Id);
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>Deletes a salary row that no approved payroll used.</summary>
    public async Task DeleteSalaryAsync(Guid id, SessionInfo user)
    {
        Require(user, Permissions.PayrollRun, "change salaries");
        await using var db = data.Create();
        var row = await db.SalaryStructures.Include(s => s.Components).SingleOrDefaultAsync(s => s.Id == id) ?? throw new BusinessRuleException("That salary no longer exists.");
        if (await FrozenAfterAsync(db, row.EmployeeId, row.EffectiveFrom) is { } run) throw new BusinessRuleException($"Payroll run #{run} was paid with it.");
        var employee = await db.Employees.AsNoTracking().Where(e => e.Id == row.EmployeeId).Select(e => e.Code + " " + e.FullName).SingleAsync();
        db.SalaryStructures.Remove(row);
        db.Audit(AuditActions.SalaryChange, $"{employee}: salary from {row.EffectiveFrom.ToString("d", CultureInfo.CurrentCulture)} deleted", nameof(SalaryStructure), row.Id);
        await db.SaveChangesAsync();
    }

    /// <summary>The number of an approved (or later) run that paid the employee for a period ending on or after the day.</summary>
    private static async Task<long?> FrozenAfterAsync(RetailPlusDbContext db, Guid employeeId, DateOnly day) =>
        await (from p in db.Payslips.AsNoTracking()
               join r in db.PayrollRuns.AsNoTracking() on p.PayrollRunId equals r.Id
               where p.EmployeeId == employeeId && r.PeriodEnd >= day
                     && (r.Status == PayrollRunStatus.Approved || r.Status == PayrollRunStatus.Posted || r.Status == PayrollRunStatus.Paid)
               orderby r.Number
               select (long?)r.Number).FirstOrDefaultAsync();

    // Loans ----------------------------------------------------------------------------------------------------------------

    public async Task<List<EmployeeLoan>> GetLoansAsync(Guid? employeeId = null, LoanStatus? status = null)
    {
        await using var db = data.Create();
        var q = db.EmployeeLoans.AsNoTracking().Include(l => l.Instalments).AsQueryable();
        if (employeeId is Guid id) q = q.Where(l => l.EmployeeId == id);
        if (status is { } s) q = q.Where(l => l.Status == s);
        if (data.StoreScope is Guid store) q = q.Where(l => db.Employees.Any(e => e.Id == l.EmployeeId && e.StoreId == store));
        var loans = await q.OrderByDescending(l => l.IssuedOn).ThenByDescending(l => l.Number).ToListAsync();
        foreach (var l in loans) l.Instalments = [.. l.Instalments.OrderBy(i => i.Number)];
        return loans;
    }

    public async Task<EmployeeLoan?> GetLoanAsync(Guid id)
    {
        await using var db = data.Create();
        var loan = await db.EmployeeLoans.AsNoTracking().Include(l => l.Instalments).SingleOrDefaultAsync(l => l.Id == id);
        if (loan is not null) loan.Instalments = [.. loan.Instalments.OrderBy(i => i.Number)];
        return loan;
    }

    /// <summary>
    /// Pays a loan or advance to an employee: instalments are scheduled from its first month (the last one takes what is
    /// left), and the payment is posted to the ledger (Dr advances to employees, Cr the cash or bank account it was paid
    /// from: a role, Cash or Bank, or an account code). Needs <see cref="Permissions.PayrollRun"/>.
    /// </summary>
    public async Task<EmployeeLoan> IssueLoanAsync(EmployeeLoan loan, string? paidFrom, SessionInfo user)
    {
        Require(user, Permissions.PayrollRun, "pay loans and advances");
        decimal amount = Money.Round(loan.Amount), instalment = Money.Round(loan.Instalment);
        if (amount <= 0) throw new BusinessRuleException("The amount must be more than zero.");
        if (instalment <= 0 || instalment > amount) throw new BusinessRuleException("The monthly instalment is more than zero and at most the amount.");
        if (amount / instalment > 120) throw new BusinessRuleException("A loan is recovered in at most 120 instalments.");
        if (loan.IssuedOn == default) throw new BusinessRuleException("Give the day it is paid.");
        var first = new DateOnly(loan.FirstMonth == default ? loan.IssuedOn.AddMonths(1).Year : loan.FirstMonth.Year,
            loan.FirstMonth == default ? loan.IssuedOn.AddMonths(1).Month : loan.FirstMonth.Month, 1);
        if (first < new DateOnly(loan.IssuedOn.Year, loan.IssuedOn.Month, 1)) throw new BusinessRuleException("Instalments start in the month it is paid or later.");
        Employee employee;
        await using (var read = data.Create())
            employee = await read.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == loan.EmployeeId) ?? throw new BusinessRuleException("That employee no longer exists.");
        if (!employee.IsEmployedOn(loan.IssuedOn)) throw new BusinessRuleException($"{employee.FullName} does not work for the company on {loan.IssuedOn:d}.");

        var row = new EmployeeLoan
        {
            EmployeeId = employee.Id, Kind = loan.Kind, IssuedOn = loan.IssuedOn, Amount = amount, Instalment = instalment, FirstMonth = first,
            Status = LoanStatus.Active, Reason = HrService.Trimmed(loan.Reason) is { } reason ? HrService.Cut(reason, 200) : null,
            CreatedBy = HrService.Cut(user.UserName, 50),
        };
        row.Id = Guid.CreateVersion7();
        row.Number = await NextNumberAsync(RetailPlusDbContext.Sequences.LoanNumbers);
        decimal left = amount;
        for (int i = 1; left > 0; i++)
        {
            var due = Math.Min(instalment, left);
            row.Instalments.Add(new LoanInstalment { LoanId = row.Id, Number = i, Month = first.AddMonths(i - 1), Amount = due });
            left -= due;
        }
        var (code, role) = Account(paidFrom, LedgerRoles.Cash);
        row.PaidFromAccount = code ?? role;
        string what = loan.Kind == LoanKind.Advance ? "Salary advance" : "Loan";
        var entry = await new LedgerService(data).PostAsync(new JournalDraft(loan.IssuedOn, JournalKind.Payroll, $"{what} #{row.Number} to {employee.Code} {employee.FullName}",
        [
            JournalDraftLine.Role(LedgerRoles.EmployeeAdvances, amount, 0, employee.StoreId, null, LedgerPartyKind.Employee, employee.Id),
            new JournalDraftLine(code, role, 0, amount, employee.StoreId),
        ])
        { Source = $"loan:{row.Id}", StoreId = employee.StoreId, Reference = $"LN{row.Number}" });
        row.JournalEntryId = entry.Id;
        await using var db = data.Create();
        db.EmployeeLoans.Add(row);
        db.Audit(AuditActions.LoanIssued, $"{what} #{row.Number} to {employee.Code} {employee.FullName}, {row.Instalments.Count} instalments from {first:MMM yyyy}",
            nameof(EmployeeLoan), row.Id);
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>Cancels a loan nothing was recovered of, reversing its payment in the ledger.</summary>
    public async Task<EmployeeLoan> CancelLoanAsync(Guid id, string? reason, SessionInfo user)
    {
        Require(user, Permissions.PayrollRun, "cancel loans");
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Say why the loan is cancelled.");
        await using var db = data.Create();
        var loan = await db.EmployeeLoans.Include(l => l.Instalments).SingleOrDefaultAsync(l => l.Id == id) ?? throw new BusinessRuleException("That loan no longer exists.");
        if (loan.Status != LoanStatus.Active) throw new BusinessRuleException($"Loan #{loan.Number} is {loan.Status.ToString().ToLowerInvariant()}.");
        if (loan.Instalments.Any(i => i.Recovered > 0)) throw new BusinessRuleException($"Instalments of loan #{loan.Number} were recovered already.");
        if (await PlannedAsync(db, loan.Instalments.Select(i => i.Id).ToList()) is { } run)
            throw new BusinessRuleException($"Payroll run #{run} takes an instalment of it; recalculate or delete that run first.");
        if (loan.JournalEntryId is Guid entry)
            await new LedgerService(data).ReverseAsync(entry, DateOnly.FromDateTime(DateTime.Today), $"loan #{loan.Number} cancelled: {reason.Trim()}");
        loan.Status = LoanStatus.Cancelled;
        db.Audit(AuditActions.LoanIssued, $"Loan #{loan.Number} cancelled: {reason.Trim()}", nameof(EmployeeLoan), loan.Id);
        await db.SaveChangesAsync();
        return loan;
    }

    /// <summary>The number of a run (not posted yet) whose payslips take one of these instalments.</summary>
    private static async Task<long?> PlannedAsync(RetailPlusDbContext db, List<Guid> instalments) =>
        await (from l in db.PayslipLines.AsNoTracking()
               join p in db.Payslips.AsNoTracking() on l.PayslipId equals p.Id
               join r in db.PayrollRuns.AsNoTracking() on p.PayrollRunId equals r.Id
               where l.LoanInstalmentId != null && instalments.Contains(l.LoanInstalmentId.Value) && r.Status != PayrollRunStatus.Posted && r.Status != PayrollRunStatus.Paid
               select (long?)r.Number).FirstOrDefaultAsync();

    // Runs -------------------------------------------------------------------------------------------------------------------

    /// <summary>The runs of the store scope (company runs only without one), the latest month first.</summary>
    public async Task<List<PayrollRun>> GetRunsAsync(int? year = null)
    {
        await using var db = data.Create();
        var q = db.PayrollRuns.AsNoTracking();
        if (year is int y) q = q.Where(r => r.Year == y);
        if (data.StoreScope is Guid store) q = q.Where(r => r.StoreId == store);
        return await q.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ThenByDescending(r => r.Number).ToListAsync();
    }

    public async Task<PayrollRun?> GetRunAsync(Guid id)
    {
        await using var db = data.Create();
        return await db.PayrollRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id);
    }

    /// <summary>A run's payslips with their lines, by employee name.</summary>
    public async Task<List<Payslip>> GetPayslipsAsync(Guid runId)
    {
        await using var db = data.Create();
        var slips = await db.Payslips.AsNoTracking().Include(p => p.Lines).Where(p => p.PayrollRunId == runId).ToListAsync();
        foreach (var p in slips) p.Lines = [.. p.Lines.OrderBy(l => l.LineNumber)];
        return [.. slips.OrderBy(p => p.EmployeeName, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.EmployeeCode, StringComparer.Ordinal)];
    }

    public async Task<Payslip?> GetPayslipAsync(Guid id)
    {
        await using var db = data.Create();
        var slip = await db.Payslips.AsNoTracking().Include(p => p.Lines).SingleOrDefaultAsync(p => p.Id == id);
        if (slip is not null) slip.Lines = [.. slip.Lines.OrderBy(l => l.LineNumber)];
        return slip;
    }

    /// <summary>
    /// Starts a month's payroll for one store, or (no store) for the whole company. A month has one run per store, or one
    /// for the company instead. Needs <see cref="Permissions.PayrollRun"/>.
    /// </summary>
    public async Task<PayrollRun> CreateRunAsync(int year, int month, Guid? storeId, string? notes, SessionInfo user)
    {
        Require(user, Permissions.PayrollRun, "run payroll");
        if (month is < 1 or > 12 || year is < 2000 or > 2100) throw new BusinessRuleException("Choose the month of the payroll.");
        await using var db = data.Create();
        if (storeId is Guid store && !await db.Stores.AnyAsync(s => s.Id == store)) throw new BusinessRuleException("That store no longer exists.");
        var clash = await db.PayrollRuns.AsNoTracking()
            .Where(r => r.Kind == PayrollRunKind.Monthly && r.Year == year && r.Month == month && (storeId == null || r.StoreId == null || r.StoreId == storeId))
            .Select(r => (long?)r.Number).FirstOrDefaultAsync();
        if (clash is not null)
            throw new BusinessRuleException($"Payroll run #{clash} already covers {MonthName(year, month)}{(storeId is null ? " for some or all stores" : " for this store")}.");
        var start = new DateOnly(year, month, 1);
        var run = new PayrollRun
        {
            Kind = PayrollRunKind.Monthly, Year = year, Month = month, PeriodStart = start, PeriodEnd = start.AddMonths(1).AddDays(-1), StoreId = storeId,
            Status = PayrollRunStatus.Draft, Notes = HrService.Trimmed(notes) is { } n ? HrService.Cut(n, 400) : null, CreatedBy = HrService.Cut(user.UserName, 50),
        };
        run.Number = await NextNumberAsync(RetailPlusDbContext.Sequences.PayrollRunNumbers);
        db.PayrollRuns.Add(run);
        db.Audit(AuditActions.PayrollRun, $"#{run.Number} {MonthName(year, month)} started", nameof(PayrollRun), run.Id);
        await db.SaveChangesAsync();
        return run;
    }

    /// <summary>
    /// Starts the final settlement of an employee who leaves (their leaving date set): the days of their last month, leave
    /// paid out, gratuity and the loans still owed.
    /// </summary>
    public async Task<PayrollRun> CreateSettlementAsync(Guid employeeId, string? notes, SessionInfo user)
    {
        Require(user, Permissions.PayrollRun, "run payroll");
        await using var db = data.Create();
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == employeeId) ?? throw new BusinessRuleException("That employee no longer exists.");
        if (employee.LeavingDate is not { } left) throw new BusinessRuleException($"Give {employee.FullName}'s leaving date first.");
        if (await db.PayrollRuns.AsNoTracking().Where(r => r.EmployeeId == employeeId).Select(r => (long?)r.Number).FirstOrDefaultAsync() is { } existing)
            throw new BusinessRuleException($"{employee.FullName} has a final settlement already (#{existing}).");
        var paid = await (from p in db.Payslips.AsNoTracking()
                          join r in db.PayrollRuns.AsNoTracking() on p.PayrollRunId equals r.Id
                          where p.EmployeeId == employeeId && r.Kind == PayrollRunKind.Monthly && r.Year == left.Year && r.Month == left.Month
                                && (r.Status == PayrollRunStatus.Approved || r.Status == PayrollRunStatus.Posted || r.Status == PayrollRunStatus.Paid)
                          select (long?)r.Number).FirstOrDefaultAsync();
        if (paid is not null) throw new BusinessRuleException($"Payroll run #{paid} pays {employee.FullName} for {MonthName(left.Year, left.Month)}; reopen it, or settle from the next month.");
        var run = new PayrollRun
        {
            Kind = PayrollRunKind.FinalSettlement, Year = left.Year, Month = left.Month, PeriodStart = new DateOnly(left.Year, left.Month, 1), PeriodEnd = left,
            StoreId = employee.StoreId, EmployeeId = employee.Id, Status = PayrollRunStatus.Draft,
            Notes = HrService.Trimmed(notes) is { } n ? HrService.Cut(n, 400) : null, CreatedBy = HrService.Cut(user.UserName, 50),
        };
        run.Number = await NextNumberAsync(RetailPlusDbContext.Sequences.PayrollRunNumbers);
        db.PayrollRuns.Add(run);
        db.Audit(AuditActions.PayrollRun, $"#{run.Number} final settlement of {employee.Code} {employee.FullName} started", nameof(PayrollRun), run.Id);
        await db.SaveChangesAsync();
        return run;
    }

    /// <summary>Deletes a run that is not approved yet, with its payslips.</summary>
    public async Task DeleteRunAsync(Guid id, SessionInfo user)
    {
        Require(user, Permissions.PayrollRun, "run payroll");
        await using var db = data.Create();
        var run = await db.PayrollRuns.SingleOrDefaultAsync(r => r.Id == id) ?? throw new BusinessRuleException("That payroll run no longer exists.");
        if (run.Status is not (PayrollRunStatus.Draft or PayrollRunStatus.Calculated)) throw new BusinessRuleException($"Run #{run.Number} is {run.Status.ToString().ToLowerInvariant()}; only a draft or calculated run is deleted.");
        db.PayrollRuns.Remove(run);
        db.Audit(AuditActions.PayrollRun, $"#{run.Number} deleted", nameof(PayrollRun), run.Id);
        await db.SaveChangesAsync();
    }

    /// <summary>Approves a calculated run (<see cref="Permissions.PayrollApprove"/>): its payslips are then fixed.</summary>
    public async Task<PayrollRun> ApproveAsync(Guid id, SessionInfo user)
    {
        Require(user, Permissions.PayrollApprove, "approve payroll");
        await using var db = data.Create();
        var run = await db.PayrollRuns.SingleOrDefaultAsync(r => r.Id == id) ?? throw new BusinessRuleException("That payroll run no longer exists.");
        if (run.Status != PayrollRunStatus.Calculated) throw new BusinessRuleException($"Run #{run.Number} is {run.Status.ToString().ToLowerInvariant()}; calculate it first.");
        if (run.Employees == 0) throw new BusinessRuleException($"Run #{run.Number} pays nobody.");
        if (run.Kind == PayrollRunKind.Monthly)
        {
            var settled = await (from p in db.Payslips.AsNoTracking()
                                 join s in db.PayrollRuns.AsNoTracking() on p.EmployeeId equals s.EmployeeId
                                 where p.PayrollRunId == run.Id && s.Kind == PayrollRunKind.FinalSettlement && s.Year == run.Year && s.Month == run.Month
                                 select p.EmployeeName).FirstOrDefaultAsync();
            if (settled is not null) throw new BusinessRuleException($"{settled} has a final settlement this month; calculate the run again.");
        }
        run.Status = PayrollRunStatus.Approved;
        run.ApprovedAtUtc = SyncColumns.Now();
        run.ApprovedBy = HrService.Cut(user.UserName, 50);
        db.Audit(AuditActions.PayrollRun, $"#{run.Number} {Title(run)} approved ({run.Employees} payslips)", nameof(PayrollRun), run.Id);
        await db.SaveChangesAsync();
        return run;
    }

    /// <summary>Takes an approved run back to calculated, to change it (<see cref="Permissions.PayrollApprove"/>).</summary>
    public async Task<PayrollRun> ReopenAsync(Guid id, SessionInfo user)
    {
        Require(user, Permissions.PayrollApprove, "reopen payroll");
        await using var db = data.Create();
        var run = await db.PayrollRuns.SingleOrDefaultAsync(r => r.Id == id) ?? throw new BusinessRuleException("That payroll run no longer exists.");
        if (run.Status != PayrollRunStatus.Approved) throw new BusinessRuleException($"Run #{run.Number} is {run.Status.ToString().ToLowerInvariant()}; only an approved run is reopened.");
        run.Status = PayrollRunStatus.Calculated;
        run.ApprovedAtUtc = null;
        run.ApprovedBy = null;
        db.Audit(AuditActions.PayrollRun, $"#{run.Number} {Title(run)} reopened", nameof(PayrollRun), run.Id);
        await db.SaveChangesAsync();
        return run;
    }

    /// <summary>
    /// Posts an approved run to the ledger (source <c>payroll:{id}</c>, dated the period's last day): Dr salaries expense
    /// and employer contributions; Cr salaries payable (each employee's net pay), income tax withheld, social security and
    /// pension contributions payable, advances to employees (instalments) and the accounts fixed deductions are owed to.
    /// Loan instalments count as recovered from then on.
    /// </summary>
    public async Task<PayrollRun> PostAsync(Guid id, SessionInfo user)
    {
        Require(user, Permissions.PayrollRun, "post payroll");
        PayrollRun run;
        List<Payslip> slips;
        await using (var read = data.Create())
        {
            run = await read.PayrollRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id) ?? throw new BusinessRuleException("That payroll run no longer exists.");
            if (run.Status != PayrollRunStatus.Approved) throw new BusinessRuleException($"Run #{run.Number} is {run.Status.ToString().ToLowerInvariant()}; only an approved run is posted.");
            slips = await read.Payslips.AsNoTracking().Include(p => p.Lines).Where(p => p.PayrollRunId == id).ToListAsync();
        }
        var entry = await new LedgerService(data).PostAsync(PostingDraft(run, slips));

        await using var db = data.Create();
        var row = await db.PayrollRuns.SingleAsync(r => r.Id == id);
        var recovered = slips.SelectMany(s => s.Lines).Where(l => l.LoanInstalmentId is not null).GroupBy(l => l.LoanInstalmentId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));
        if (recovered.Count > 0)
        {
            var ids = recovered.Keys.ToList();
            var instalments = await db.LoanInstalments.Where(i => ids.Contains(i.Id)).ToListAsync();
            foreach (var i in instalments)
            {
                i.Recovered = Money.Round(Math.Min(i.Amount, i.Recovered + recovered[i.Id]));
                i.PayrollRunId = run.Id;
            }
            var loanIds = instalments.Select(i => i.LoanId).Distinct().ToList();
            foreach (var loan in await db.EmployeeLoans.Include(l => l.Instalments).Where(l => loanIds.Contains(l.Id)).ToListAsync())
                if (loan.Instalments.All(i => i.Recovered >= i.Amount)) loan.Status = LoanStatus.Settled;
        }
        row.Status = PayrollRunStatus.Posted;
        row.PostedAtUtc = SyncColumns.Now();
        row.PostedBy = HrService.Cut(user.UserName, 50);
        row.JournalEntryId = entry.Id;
        db.Audit(AuditActions.PayrollRun, $"#{run.Number} {Title(run)} posted to the ledger as entry #{entry.Number}", nameof(PayrollRun), run.Id);
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>The entry a run posts: aggregated per store for the expense and the authorities, per employee for what they are owed and owe.</summary>
    internal static JournalDraft PostingDraft(PayrollRun run, List<Payslip> slips)
    {
        if (slips.Count == 0) throw new BusinessRuleException($"Run #{run.Number} has no payslips.");
        var expense = new Dictionary<Guid, decimal>();
        var employer = new Dictionary<Guid, decimal>();
        var tax = new Dictionary<Guid, decimal>();
        var statutory = new Dictionary<Guid, decimal>();
        var owed = new Dictionary<(string Code, Guid Store), decimal>();
        static void Add<TKey>(Dictionary<TKey, decimal> d, TKey key, decimal amount) where TKey : notnull => d[key] = d.GetValueOrDefault(key) + amount;
        var perEmployee = new List<JournalDraftLine>();
        // Stores are keyed by their id, head office (no store) by the empty id.
        static Guid? Store(Guid key) => key == Guid.Empty ? null : key;
        foreach (var slip in slips.OrderBy(s => s.EmployeeCode, StringComparer.Ordinal))
        {
            decimal payable = slip.Net, advances = 0;
            var at = slip.StoreId ?? Guid.Empty;
            foreach (var line in slip.Lines)
            {
                switch (line.Kind)
                {
                    case PayslipLineKind.Earning:
                        Add(expense, at, line.Amount);
                        break;
                    case PayslipLineKind.EmployerContribution:
                        Add(employer, at, line.Amount);
                        Add(statutory, at, line.Amount);
                        break;
                    default:
                        if (PayslipCodes.IsAttendance(line.Code)) Add(expense, at, -line.Amount);
                        else if (line.Code == PayslipCodes.Tax) Add(tax, at, line.Amount);
                        else if (line.Code == PayslipCodes.Eobi) Add(statutory, at, line.Amount);
                        else if (line.Code == PayslipCodes.Loan) advances += line.Amount;
                        else if (line.AccountCode is { } code) Add(owed, (code, at), line.Amount);
                        else payable += line.Amount;   // a deduction owed to someone the chart has no account for stays in salaries payable
                        break;
                }
            }
            var memo = $"{slip.EmployeeCode} {slip.EmployeeName}";
            Credit(perEmployee, null, LedgerRoles.SalariesPayable, payable, slip.StoreId, memo, slip.EmployeeId);
            Credit(perEmployee, null, LedgerRoles.EmployeeAdvances, advances, slip.StoreId, memo, slip.EmployeeId);
        }
        var lines = new List<JournalDraftLine>();
        foreach (var (store, amount) in expense) Credit(lines, null, LedgerRoles.SalariesExpense, -amount, Store(store), "Salaries and wages", null);
        foreach (var (store, amount) in employer) Credit(lines, null, LedgerRoles.EmployerContributionsExpense, -amount, Store(store), "Employer contributions", null);
        foreach (var (store, amount) in tax) Credit(lines, null, LedgerRoles.PayrollTaxPayable, amount, Store(store), "Income tax withheld", null);
        foreach (var (store, amount) in statutory) Credit(lines, null, LedgerRoles.StatutoryContributionsPayable, amount, Store(store), "EOBI and social security", null);
        foreach (var ((code, store), amount) in owed) Credit(lines, code, null, amount, Store(store), "Deductions from pay", null);
        lines.AddRange(perEmployee);
        return new JournalDraft(run.PeriodEnd, JournalKind.Payroll, $"Payroll #{run.Number}: {Title(run)}", lines)
        {
            Source = $"payroll:{run.Id}", StoreId = run.StoreId, Reference = $"PR{run.Number}",
        };
    }

    /// <summary>A credit of the amount (a debit when it is negative; nothing when it is zero).</summary>
    private static void Credit(List<JournalDraftLine> lines, string? code, string? role, decimal amount, Guid? store, string? memo, Guid? employee)
    {
        amount = Money.Round(amount);
        if (amount == 0) return;
        lines.Add(new JournalDraftLine(code, role, amount < 0 ? -amount : 0, amount > 0 ? amount : 0, store, memo,
            employee is null ? null : LedgerPartyKind.Employee, employee));
    }

    /// <summary>
    /// Records that a posted run was paid on a day: Dr salaries payable (each employee's net pay), Cr the bank account for
    /// those paid by transfer and the cash account for those paid in cash (the accounts with the roles Bank and Cash
    /// unless others are given). Source <c>payroll-payment:{id}</c>.
    /// </summary>
    public async Task<PayrollRun> PayAsync(Guid id, DateOnly paidOn, string? bankAccount, string? cashAccount, SessionInfo user)
    {
        Require(user, Permissions.PayrollRun, "pay payroll");
        PayrollRun run;
        List<Payslip> slips;
        await using (var read = data.Create())
        {
            run = await read.PayrollRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id) ?? throw new BusinessRuleException("That payroll run no longer exists.");
            if (run.Status != PayrollRunStatus.Posted) throw new BusinessRuleException($"Run #{run.Number} is {run.Status.ToString().ToLowerInvariant()}; post it before it is paid.");
            slips = await read.Payslips.AsNoTracking().Where(p => p.PayrollRunId == id).ToListAsync();
        }
        if (paidOn < run.PeriodStart) throw new BusinessRuleException("Pay is paid on or after the start of its month.");
        var (bankCode, bankRole) = Account(bankAccount, LedgerRoles.Bank);
        var (cashCode, cashRole) = Account(cashAccount, LedgerRoles.Cash);
        var lines = new List<JournalDraftLine>();
        var bank = new Dictionary<Guid, decimal>();
        var cash = new Dictionary<Guid, decimal>();
        foreach (var slip in slips.Where(s => s.Net > 0).OrderBy(s => s.EmployeeCode, StringComparer.Ordinal))
        {
            lines.Add(JournalDraftLine.Role(LedgerRoles.SalariesPayable, slip.Net, 0, slip.StoreId, $"{slip.EmployeeCode} {slip.EmployeeName}", LedgerPartyKind.Employee, slip.EmployeeId));
            var paidFrom = slip.PaymentMethod == SalaryPaymentMethod.Bank ? bank : cash;
            var at = slip.StoreId ?? Guid.Empty;
            paidFrom[at] = paidFrom.GetValueOrDefault(at) + slip.Net;
        }
        foreach (var (store, amount) in bank) lines.Add(new JournalDraftLine(bankCode, bankRole, 0, amount, store == Guid.Empty ? null : store, "Bank transfers"));
        foreach (var (store, amount) in cash) lines.Add(new JournalDraftLine(cashCode, cashRole, 0, amount, store == Guid.Empty ? null : store, "Paid in cash"));
        JournalEntry? entry = null;
        if (lines.Count > 0)
            entry = await new LedgerService(data).PostAsync(new JournalDraft(paidOn, JournalKind.Payroll, $"Salaries paid, payroll #{run.Number}: {Title(run)}", lines)
            {
                Source = $"payroll-payment:{run.Id}", StoreId = run.StoreId, Reference = $"PR{run.Number}",
            });
        await using var db = data.Create();
        var row = await db.PayrollRuns.SingleAsync(r => r.Id == id);
        row.Status = PayrollRunStatus.Paid;
        row.PaidOn = paidOn;
        row.PaidAtUtc = SyncColumns.Now();
        row.PaidBy = HrService.Cut(user.UserName, 50);
        row.PaymentEntryId = entry?.Id;
        db.Audit(AuditActions.PayrollRun, $"#{run.Number} {Title(run)} paid on {paidOn.ToString("d", CultureInfo.CurrentCulture)}", nameof(PayrollRun), run.Id);
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>An account given as a role name (Cash, Bank) or as a code; nothing given is the default role.</summary>
    private static (string? Code, string? Role) Account(string? given, string defaultRole)
    {
        var text = given?.Trim();
        if (string.IsNullOrEmpty(text)) return (null, defaultRole);
        return LedgerRoles.All.Contains(text, StringComparer.OrdinalIgnoreCase) ? (null, LedgerRoles.All.First(r => string.Equals(r, text, StringComparison.OrdinalIgnoreCase))) : (text, null);
    }

    /// <summary>The bank transfer list of a run: one line per employee paid by bank, as CSV (UTF-8).</summary>
    public async Task<string> BankTransfersCsvAsync(Guid runId)
    {
        var run = await GetRunAsync(runId) ?? throw new BusinessRuleException("That payroll run no longer exists.");
        var slips = await GetPayslipsAsync(runId);
        var csv = new StringBuilder();
        csv.AppendLine("Employee code,Employee,CNIC,Bank,IBAN,Amount,Reference");
        foreach (var s in slips.Where(s => s.PaymentMethod == SalaryPaymentMethod.Bank && s.Net > 0))
            csv.AppendLine(string.Join(',', Csv(s.EmployeeCode), Csv(s.EmployeeName), Csv(s.Cnic), Csv(s.BankName), Csv(s.Iban),
                s.Net.ToString("0.00", CultureInfo.InvariantCulture), Csv($"Salary {MonthName(run.Year, run.Month)} PR{run.Number}")));
        return csv.ToString();
    }

    private static string Csv(string? value)
    {
        var text = value ?? "";
        // A cell starting like a formula is kept as text in spreadsheets.
        if (text.Length > 0 && "=+-@".Contains(text[0], StringComparison.Ordinal)) text = "'" + text;
        return text.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;
    }

    // Calculation ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Calculates (again) every payslip of a draft or calculated run. Employees without a salary or without payroll rules
    /// for their country are left out, with a warning on the run. <paramref name="today"/> decides which days of the month
    /// are still to come (they are paid as worked).
    /// </summary>
    public async Task<PayrollRun> CalculateAsync(Guid id, SessionInfo user, DateOnly? today = null)
    {
        Require(user, Permissions.PayrollRun, "calculate payroll");
        await using var db = data.Create();
        var run = await db.PayrollRuns.SingleOrDefaultAsync(r => r.Id == id) ?? throw new BusinessRuleException("That payroll run no longer exists.");
        if (run.Status is not (PayrollRunStatus.Draft or PayrollRunStatus.Calculated))
            throw new BusinessRuleException($"Run #{run.Number} is {run.Status.ToString().ToLowerInvariant()}; {(run.Status == PayrollRunStatus.Approved ? "reopen it to calculate it again" : "it cannot change")}.");
        var old = await db.Payslips.Where(p => p.PayrollRunId == id).ToListAsync();
        db.Payslips.RemoveRange(old);

        var monthStart = new DateOnly(run.Year, run.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var employees = await EmployeesOfAsync(db, run);
        var warnings = new List<string>();
        var calendar = await HrCalendar.LoadAsync(db, employees, monthStart, monthEnd);
        var days = (await AttendanceService.DaysAsync(db, employees, run.PeriodStart, run.PeriodEnd, today ?? DateOnly.FromDateTime(DateTime.Now))).ToLookup(d => d.EmployeeId);
        var ids = employees.Select(e => e.Id).ToList();
        var salaries = new List<SalaryStructure>();
        var priors = new List<(Guid EmployeeId, DateOnly PeriodEnd, decimal Taxable, decimal Tax)>();
        var instalments = new List<(LoanInstalment Instalment, long LoanNumber, LoanKind Kind, Guid EmployeeId)>();
        var earliest = monthStart.AddYears(-1);
        foreach (var chunk in ids.Chunk(500))
        {
            var list = chunk.ToList();
            salaries.AddRange(await db.SalaryStructures.AsNoTracking().Include(s => s.Components).Where(s => list.Contains(s.EmployeeId) && s.EffectiveFrom <= run.PeriodEnd).ToListAsync());
            priors.AddRange((await (from p in db.Payslips.AsNoTracking()
                                    join r in db.PayrollRuns.AsNoTracking() on p.PayrollRunId equals r.Id
                                    where list.Contains(p.EmployeeId) && r.Id != run.Id && r.Status != PayrollRunStatus.Draft && r.PeriodEnd < run.PeriodStart && r.PeriodEnd >= earliest
                                    select new { p.EmployeeId, r.PeriodEnd, p.Taxable, p.Tax }).ToListAsync())
                .Select(p => (p.EmployeeId, p.PeriodEnd, p.Taxable, p.Tax)));
            var monthKey = monthStart;
            bool settlement = run.Kind == PayrollRunKind.FinalSettlement;
            instalments.AddRange((await (from i in db.LoanInstalments.AsNoTracking()
                                         join l in db.EmployeeLoans.AsNoTracking() on i.LoanId equals l.Id
                                         where list.Contains(l.EmployeeId) && l.Status == LoanStatus.Active && (settlement || i.Month <= monthKey)
                                         select new { Instalment = i, l.Number, l.Kind, l.EmployeeId }).ToListAsync())
                .Where(x => x.Instalment.Recovered < x.Instalment.Amount)
                .Select(x => (x.Instalment, x.Number, x.Kind, x.EmployeeId)));
        }
        var salaryOf = salaries.ToLookup(s => s.EmployeeId);
        var priorOf = priors.ToLookup(p => p.EmployeeId);
        var instalmentsOf = instalments.ToLookup(i => i.EmployeeId);
        var designations = await db.Designations.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name);
        var departments = await db.HrDepartments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name);
        var leaveTypes = run.Kind == PayrollRunKind.FinalSettlement ? await db.LeaveTypes.AsNoTracking().Where(t => t.Encashable && t.IsActive).ToListAsync() : [];
        // Commission of the period's sales, at every store, by salesperson.
        var commission = (await new StaffService(data.For(null, data.CurrentUser)).CommissionByStaffAsync(run.PeriodStart, run.PeriodEnd))
            .Where(c => c.StaffId is not null).GroupBy(c => c.StaffId!.Value).ToDictionary(g => g.Key, g => g.Sum(c => c.Commission));

        var made = new List<Payslip>();
        foreach (var employee in employees)
        {
            var rules = calendar.RulesOf(employee);
            if (rules is null)
            {
                warnings.Add($"{employee.Code} {employee.FullName}: there are no payroll rules for {calendar.CountryOf(employee)} on " +
                    $"{run.PeriodEnd.ToString("d", CultureInfo.CurrentCulture)}; add that tax year's rule set in HR settings.");
                continue;
            }
            var firstDay = employee.JoiningDate > run.PeriodStart ? employee.JoiningDate : run.PeriodStart;
            var lastDay = employee.LeavingDate is { } left && left < run.PeriodEnd ? left : run.PeriodEnd;
            if (lastDay < firstDay) continue;
            var salary = salaryOf[employee.Id].Where(s => s.EffectiveFrom <= lastDay).OrderByDescending(s => s.EffectiveFrom).FirstOrDefault();
            if (salary is null)
            {
                warnings.Add($"{employee.Code} {employee.FullName}: no salary on {lastDay.ToString("d", CultureInfo.CurrentCulture)}.");
                continue;
            }
            var input = new SlipInput(run, employee, rules, salary, calendar, days[employee.Id].ToList(), firstDay, lastDay, monthStart, monthEnd,
                employee.UserAccountId is Guid u ? commission.GetValueOrDefault(u) : 0, priorOf[employee.Id].ToList(), instalmentsOf[employee.Id].ToList(),
                employee.DesignationId is Guid des ? designations.GetValueOrDefault(des) : null, employee.HrDepartmentId is Guid dep ? departments.GetValueOrDefault(dep) : null);
            var encashment = new List<(LeaveType Type, decimal Days)>();
            foreach (var type in leaveTypes.Where(t => t.Country == calendar.CountryOf(employee)))
                if (await LeaveService.AvailableAsync(db, employee, type, lastDay) is { } available && available > 0) encashment.Add((type, available));
            var slip = Calculate(input, encashment, warnings);
            slip.PayrollRunId = run.Id;
            made.Add(slip);
        }
        db.Payslips.AddRange(made);
        run.Employees = made.Count;
        run.Gross = made.Sum(s => s.Gross);
        run.Deductions = made.Sum(s => s.Deductions);
        run.Net = made.Sum(s => s.Net);
        run.Tax = made.Sum(s => s.Tax);
        run.EmployerContributions = made.Sum(s => s.EmployerContributions);
        run.Warnings = warnings.Count == 0 ? null : HrService.Cut(string.Join("\n", warnings), 4000);
        run.Status = PayrollRunStatus.Calculated;
        run.CalculatedAtUtc = SyncColumns.Now();
        run.CalculatedBy = HrService.Cut(user.UserName, 50);
        db.Audit(AuditActions.PayrollRun, $"#{run.Number} {Title(run)} calculated: {made.Count} payslips{(warnings.Count > 0 ? $", {warnings.Count} warnings" : "")}",
            nameof(PayrollRun), run.Id);
        await db.SaveChangesAsync();
        return run;
    }

    /// <summary>Who a run pays: one employee for a settlement; else everyone of its store (or the company) employed in the month, less those settled in it.</summary>
    private static async Task<List<Employee>> EmployeesOfAsync(RetailPlusDbContext db, PayrollRun run)
    {
        if (run.Kind == PayrollRunKind.FinalSettlement)
            return await db.Employees.AsNoTracking().Where(e => e.Id == run.EmployeeId).ToListAsync();
        var q = db.Employees.AsNoTracking().Where(e => e.JoiningDate <= run.PeriodEnd && (e.LeavingDate == null || e.LeavingDate >= run.PeriodStart));
        if (run.StoreId is Guid store) q = q.Where(e => e.StoreId == store);
        q = q.Where(e => !db.PayrollRuns.Any(s => s.Kind == PayrollRunKind.FinalSettlement && s.EmployeeId == e.Id && s.Year == run.Year && s.Month == run.Month));
        return await q.OrderBy(e => e.FullName).ToListAsync();
    }

    private sealed record SlipInput(PayrollRun Run, Employee Employee, PayrollRuleSet Rules, SalaryStructure Salary, HrCalendar Calendar, List<AttendanceDay> Days,
        DateOnly FirstDay, DateOnly LastDay, DateOnly MonthStart, DateOnly MonthEnd, decimal Commission, List<(Guid EmployeeId, DateOnly PeriodEnd, decimal Taxable, decimal Tax)> Prior,
        List<(LoanInstalment Instalment, long LoanNumber, LoanKind Kind, Guid EmployeeId)> Instalments, string? Designation, string? Department);

    /// <summary>One employee's payslip from what was read for the run.</summary>
    private static Payslip Calculate(SlipInput x, List<(LeaveType Type, decimal Days)> encashment, List<string> warnings)
    {
        var (run, e, rules, salary) = (x.Run, x.Employee, x.Rules, x.Salary);
        bool settlement = run.Kind == PayrollRunKind.FinalSettlement;
        bool daily = e.EmploymentType == EmploymentType.DailyWage;
        int daysInMonth = x.MonthEnd.Day;
        int employedDays = x.LastDay.DayNumber - x.FirstDay.DayNumber + 1;
        bool fullMonth = x.FirstDay == x.MonthStart && x.LastDay == x.MonthEnd;
        int workingInMonth = 0, workingEmployed = 0;
        for (var d = x.MonthStart; d <= x.MonthEnd; d = d.AddDays(1))
        {
            if (!x.Calendar.PlanOn(e, d).Expected) continue;
            workingInMonth++;
            if (d >= x.FirstDay && d <= x.LastDay) workingEmployed++;
        }
        decimal calendarShare = fullMonth ? 1m : (decimal)employedDays / daysInMonth;
        decimal share = fullMonth ? 1m : rules.DailyRateBasis switch
        {
            DailyRateBasis.ThirtyDays => Math.Min(30m, employedDays) / 30m,
            DailyRateBasis.WorkingDays when workingInMonth > 0 => (decimal)workingEmployed / workingInMonth,
            _ => calendarShare,
        };
        decimal divisor = rules.DailyRateBasis switch
        {
            DailyRateBasis.ThirtyDays => 30m,
            DailyRateBasis.WorkingDays when workingInMonth > 0 => workingInMonth,
            _ => daysInMonth,
        };
        decimal hoursPerDay = rules.HoursPerWeek / Math.Max(1, rules.WorkingDaysPerWeek);
        var allowances = salary.Components.Where(c => c.Kind == SalaryComponentKind.Allowance).OrderBy(c => c.LineNumber).ToList();
        var fixedDeductions = salary.Components.Where(c => c.Kind == SalaryComponentKind.Deduction).OrderBy(c => c.LineNumber).ToList();
        decimal grossMonthly = salary.Basic + allowances.Sum(a => a.Amount);

        var days = x.Days.Where(d => d.Date >= x.FirstDay && d.Date <= x.LastDay).ToList();
        decimal present = days.Count(d => d.Status is AttendanceStatus.Present or AttendanceStatus.Scheduled);
        decimal paidLeave = days.Where(d => d.LeaveTypeId is not null && d.LeavePaid).Sum(d => d.LeaveDays);
        decimal unpaidLeave = days.Where(d => d.LeaveTypeId is not null && !d.LeavePaid).Sum(d => d.LeaveDays);
        int absent = rules.DeductAbsences ? days.Count(d => d.Status == AttendanceStatus.Absent) : 0;
        var lates = days.Where(d => d.LateMinutes > 0).ToList();
        int overtimeMinutes = e.OvertimeEligible ? days.Sum(d => d.OvertimeMinutes) : 0;

        var lines = new List<PayslipLine>();
        void Line(PayslipLineKind kind, string code, string name, decimal amount, decimal taxable = 0, decimal? quantity = null, decimal? rate = null,
            Guid? instalment = null, string? account = null)
        {
            amount = Money.Round(amount);
            if (amount <= 0) return;
            lines.Add(new PayslipLine
            {
                LineNumber = lines.Count + 1, Kind = kind, Code = code, Name = HrService.Cut(name, 80), Amount = amount, TaxableAmount = Money.Round(taxable),
                Quantity = quantity is { } q ? Math.Round(q, 2) : null, Rate = rate is { } r ? Math.Round(r, 4) : null, LoanInstalmentId = instalment, AccountCode = account,
            });
        }

        // Earnings: basic and allowances for the days employed (a daily wage for the days worked and paid leave).
        decimal basicDays = present + paidLeave;
        decimal basic = daily ? Money.Round(salary.Basic * basicDays) : Money.Round(salary.Basic * share);
        Line(PayslipLineKind.Earning, PayslipCodes.Basic, daily ? "Daily wages" : "Basic pay", basic, basic, daily ? basicDays : employedDays, daily ? salary.Basic : null);
        decimal regularTaxable = daily ? 0 : salary.Basic;
        foreach (var a in allowances)
        {
            var rule = rules.Allowances.FirstOrDefault(r => r.Code == a.Code);
            decimal amount = Money.Round(a.Amount * share);
            Line(PayslipLineKind.Earning, a.Code, a.Name, amount, TaxablePart(rule, amount, basic));
            regularTaxable += TaxablePart(rule, a.Amount, salary.Basic);
        }
        decimal hourly = daily ? salary.Basic / hoursPerDay : grossMonthly / divisor / hoursPerDay;
        if (overtimeMinutes > 0)
        {
            decimal hours = Math.Round(overtimeMinutes / 60m, 2);
            decimal amount = Money.Round(hours * hourly * rules.OvertimeMultiplier);
            Line(PayslipLineKind.Earning, PayslipCodes.Overtime, "Overtime", amount, amount, hours, hourly * rules.OvertimeMultiplier);
        }
        if (x.Commission > 0) Line(PayslipLineKind.Earning, PayslipCodes.Commission, "Commission", x.Commission, x.Commission);
        if (settlement)
        {
            decimal dayWage = (daily ? salary.Basic * 30m : grossMonthly) / 30m;
            foreach (var (type, available) in encashment)
            {
                decimal amount = Money.Round(available * dayWage);
                Line(PayslipLineKind.Earning, PayslipCodes.LeaveEncashment, $"{type.Name} paid out", amount, amount, available, dayWage);
            }
            int years = rules.GratuityEnabled ? PayrollRules.GratuityYears(e.JoiningDate, x.LastDay, rules.GratuityMinimumMonths, rules.GratuityPartYearMonths) : 0;
            if (years > 0)
            {
                decimal wage = daily ? salary.Basic * 30m : rules.GratuityOnBasic ? salary.Basic : grossMonthly;
                decimal amount = Money.Round(years * rules.GratuityDaysPerYear * wage / 30m);
                Line(PayslipLineKind.Earning, PayslipCodes.Gratuity, "Gratuity", amount, amount, years, rules.GratuityDaysPerYear * wage / 30m);
            }
        }
        decimal earnings = lines.Sum(l => l.Amount);

        // Time not worked (not for a daily wage, which pays only the days worked), never more than the pay itself.
        decimal dailyRate = grossMonthly / divisor;
        decimal room = lines.Where(l => l.Code == PayslipCodes.Basic || allowances.Any(a => a.Code == l.Code)).Sum(l => l.Amount);
        void Attendance(string code, string name, decimal amount, decimal quantity, decimal rate)
        {
            amount = Math.Min(Money.Round(amount), room);
            room -= amount;
            Line(PayslipLineKind.Deduction, code, name, amount, amount, quantity, rate);
        }
        if (!daily)
        {
            if (unpaidLeave > 0) Attendance(PayslipCodes.UnpaidLeave, "Unpaid leave", unpaidLeave * dailyRate, unpaidLeave, dailyRate);
            if (absent > 0) Attendance(PayslipCodes.Absence, "Absent without leave", absent * dailyRate, absent, dailyRate);
            int lateDays = rules.LatesPerDay > 0 ? lates.Count / rules.LatesPerDay : 0;
            int lateMinutes = lates.Sum(d => d.LateMinutes);
            if (rules.LatenessRule == LatenessRule.DayPerLates && lateDays > 0)
                Attendance(PayslipCodes.Lateness, $"Late {lates.Count} times", lateDays * dailyRate, lateDays, dailyRate);
            else if (rules.LatenessRule == LatenessRule.PerMinute && lateMinutes > 0)
                Attendance(PayslipCodes.Lateness, $"Late {lateMinutes} minutes", lateMinutes * hourly / 60m, lateMinutes, hourly / 60m);
        }
        decimal attendance = lines.Where(l => PayslipCodes.IsAttendance(l.Code)).Sum(l => l.Amount);

        // Income tax by the annualised projection.
        decimal taxableNow = Math.Max(0, lines.Where(l => l.Kind == PayslipLineKind.Earning).Sum(l => l.TaxableAmount) - attendance);
        var taxYear = PayrollRules.TaxYearStart(run.PeriodEnd, rules.TaxYearStartMonth);
        var prior = x.Prior.Where(p => p.PeriodEnd >= taxYear).ToList();
        decimal taxableBefore = prior.Sum(p => p.Taxable), taxBefore = prior.Sum(p => p.Tax);
        if (e.PriorTaxYear == taxYear.Year)
        {
            taxableBefore += e.PriorTaxableIncome;
            taxBefore += e.PriorTaxDeducted;
        }
        int monthsAfter = settlement ? 0 : PayrollRules.MonthsAfter(run.PeriodEnd, rules.TaxYearStartMonth, e.LeavingDate);
        var slabs = rules.TaxSlabs.Select(s => new TaxSlab(s.IncomeFrom, s.IncomeTo, s.FixedTax, s.Rate)).ToList();
        var tax = PayrollRules.ProjectedMonthlyTax(taxableBefore, taxBefore, taxableNow, daily ? taxableNow : regularTaxable, monthsAfter, slabs,
            rules.SurchargeThreshold, rules.SurchargeRate);
        Line(PayslipLineKind.Deduction, PayslipCodes.Tax, "Income tax", tax.Tax);
        if (e.EobiApplies && rules.EobiEmployeeRate > 0)
            Line(PayslipLineKind.Deduction, PayslipCodes.Eobi, "EOBI (employee)", rules.MinimumWage * rules.EobiEmployeeRate / 100m * calendarShare, 0, null, rules.EobiEmployeeRate);
        foreach (var d in fixedDeductions) Line(PayslipLineKind.Deduction, d.Code, d.Name, d.Amount, account: d.AccountCode);
        foreach (var (instalment, number, kind, _) in x.Instalments.OrderBy(i => i.Instalment.Month).ThenBy(i => i.LoanNumber))
            Line(PayslipLineKind.Deduction, PayslipCodes.Loan, $"{(kind == LoanKind.Advance ? "Advance" : "Loan")} #{number}, instalment {instalment.Number}",
                instalment.Amount - instalment.Recovered, instalment: instalment.Id);

        // Net pay never below zero: loan instalments wait first, then fixed deductions.
        decimal net = earnings - lines.Where(l => l.Kind == PayslipLineKind.Deduction).Sum(l => l.Amount);
        foreach (var line in lines.Where(l => l.Code == PayslipCodes.Loan).Reverse().Concat(lines.Where(l => fixedDeductions.Any(d => d.Code == l.Code) && l.Kind == PayslipLineKind.Deduction).Reverse()).ToList())
        {
            if (net >= 0) break;
            decimal cut = Math.Min(line.Amount, -net);
            line.Amount -= cut;
            net += cut;
            if (line.Amount == 0) lines.Remove(line);
        }
        if (net < 0) warnings.Add($"{e.Code} {e.FullName}: the deductions are more than the pay.");
        for (int i = 0; i < lines.Count; i++) lines[i].LineNumber = i + 1;

        // Employer contributions.
        if (e.EobiApplies && rules.EobiEmployerRate > 0)
            Line(PayslipLineKind.EmployerContribution, PayslipCodes.EobiEmployer, "EOBI (employer)", rules.MinimumWage * rules.EobiEmployerRate / 100m * calendarShare, 0, null, rules.EobiEmployerRate);
        decimal wageEarned = earnings - attendance;
        decimal monthlyWage = daily ? wageEarned : grossMonthly;
        if (e.SocialSecurityApplies && rules.SocialSecurityRate > 0 && (rules.SocialSecurityWageCeiling <= 0 || monthlyWage <= rules.SocialSecurityWageCeiling))
        {
            decimal on = rules.SocialSecurityBasis == SocialSecurityBasis.MinimumWage ? rules.MinimumWage * calendarShare : wageEarned;
            Line(PayslipLineKind.EmployerContribution, PayslipCodes.SocialSecurity, $"Social security{(rules.SocialSecurityProvince is { } p ? $" ({p})" : "")}",
                on * rules.SocialSecurityRate / 100m, 0, Money.Round(on), rules.SocialSecurityRate);
        }

        decimal deductions = lines.Where(l => l.Kind == PayslipLineKind.Deduction).Sum(l => l.Amount);
        return new Payslip
        {
            EmployeeId = e.Id, EmployeeCode = e.Code, EmployeeName = e.FullName, Designation = x.Designation, Department = x.Department, StoreId = e.StoreId, Cnic = e.Cnic,
            PaymentMethod = e.PaymentMethod, BankName = e.BankName, Iban = e.Iban, DaysInPeriod = daysInMonth, DaysEmployed = employedDays, DaysPresent = present,
            PaidLeaveDays = paidLeave, UnpaidLeaveDays = unpaidLeave, AbsentDays = absent, LateDays = lates.Count, LateMinutes = lates.Sum(d => d.LateMinutes),
            OvertimeHours = Math.Round(overtimeMinutes / 60m, 2), Basic = salary.Basic, Gross = earnings, Deductions = deductions, Net = earnings - deductions,
            Taxable = taxableNow, Tax = tax.Tax, EmployerContributions = lines.Where(l => l.Kind == PayslipLineKind.EmployerContribution).Sum(l => l.Amount),
            TaxableBefore = taxableBefore, TaxBefore = taxBefore, ProjectedAnnualTaxable = tax.ProjectedAnnualTaxable, ProjectedAnnualTax = tax.AnnualTax,
            Lines = lines,
        };
    }

    /// <summary>The taxable part of an allowance: all of it unless the rule set exempts it (wholly, or up to a share of the basic pay).</summary>
    private static decimal TaxablePart(PayrollAllowanceRule? rule, decimal amount, decimal basic)
    {
        if (rule is null || rule.IsTaxable) return amount;
        if (rule.ExemptPercentOfBasic is not { } percent) return 0;
        return Math.Max(0, amount - Money.Round(basic * percent / 100m));
    }

    // Helpers -----------------------------------------------------------------------------------------------------------------

    private static void Require(SessionInfo user, string permission, string what)
    {
        if (!user.Can(permission)) throw new BusinessRuleException($"{user.UserName} may not {what}.");
    }

    public static string MonthName(int year, int month) => new DateOnly(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>What a run is, for people: its month, or the settlement of its employee.</summary>
    public static string Title(PayrollRun run) => run.Kind == PayrollRunKind.FinalSettlement ? $"final settlement, {MonthName(run.Year, run.Month)}" : MonthName(run.Year, run.Month);

    private async Task<long> NextNumberAsync(string sequence)
    {
        var store = data.Station?.StoreId ?? Guid.Empty;
        await using var db = data.For(new StationIdentity(store, PayrollStation), data.CurrentUser).Create();
        return await db.NextNumberAsync(sequence);
    }
}
