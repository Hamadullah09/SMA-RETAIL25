using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.Data.Tests;

/// <summary>
/// Payroll on both engines, with Pakistan's seeded rules of tax year 2026: payslips for salaries in every tax slab (worked
/// out by hand below), allowances exempt up to a share of basic pay, joiners, unpaid leave, absence and overtime, the tax
/// projection across months and a previous employer, loans, the final settlement with gratuity and leave paid out, the
/// steps from draft to paid with who may take them, and the ledger entries, which must balance. Each test pays a store of
/// its own, because the tests share one database.
/// </summary>
public abstract class PayrollTests(DatabaseFixture db)
{
    /// <summary>Someone whose role has every permission (a database without users signs everyone in like this).</summary>
    private static readonly SessionInfo Admin = new();

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..6];

    private static SessionInfo Session(string name, params string[] keys) => new() { UserName = name, Permissions = keys.ToHashSet(StringComparer.Ordinal) };

    /// <summary>The services with the chart and Pakistan's rules seeded, and a new store to pay.</summary>
    private async Task<(DataContext Data, Guid Store)> StoreAsync()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        var data = db.Data;
        await new LedgerService(data).SeedAsync();
        await new HrService(data).SeedAsync();
        await using var ctx = db.CreateContext();
        var store = new Store { Code = Unique("P"), Name = "Payroll test store" };
        ctx.Stores.Add(store);
        await ctx.SaveChangesAsync();
        return (data, store.Id);
    }

    /// <summary>An employee of the store with a monthly salary from the day they joined; attendance is presumed unless tracked.</summary>
    private static async Task<Employee> PaidAsync(DataContext data, Guid store, string name, decimal basic, DateOnly joined, Employee? details = null,
        params (string Code, decimal Amount)[] allowances)
    {
        var employee = details ?? new Employee { TracksAttendance = false };
        employee.FullName = name;
        employee.JoiningDate = joined;
        employee.StoreId = store;
        employee = await new HrService(data).SaveEmployeeAsync(employee);
        await new PayrollService(data).SaveSalaryAsync(new SalaryStructure
        {
            EmployeeId = employee.Id, EffectiveFrom = joined, Basic = basic,
            Components = [.. allowances.Select(a => new SalaryComponent { Kind = SalaryComponentKind.Allowance, Code = a.Code, Name = a.Code, Amount = a.Amount })],
        }, Admin);
        return employee;
    }

    /// <summary>A month's run of the store, calculated as on a day after the month (so no day is still to come).</summary>
    private static async Task<(PayrollRun Run, Dictionary<Guid, Payslip> Slips)> CalculatedAsync(DataContext data, Guid store, int year, int month)
    {
        var payroll = new PayrollService(data);
        var run = await payroll.CreateRunAsync(year, month, store, null, Admin);
        run = await payroll.CalculateAsync(run.Id, Admin, new DateOnly(year, month, 1).AddMonths(2));
        return (run, (await payroll.GetPayslipsAsync(run.Id)).ToDictionary(p => p.EmployeeId));
    }

    private static decimal Line(Payslip slip, string code) => slip.Lines.Where(l => l.Code == code).Sum(l => l.Amount);

    /// <summary>What an entry puts on an account: debit positive, credit negative.</summary>
    private static decimal On(JournalEntry entry, string code) => entry.Lines.Where(l => l.Account!.Code == code).Sum(l => l.Debit - l.Credit);

    private static List<TaxSlab> Slabs(PayrollRuleSet set) => [.. set.TaxSlabs.Select(s => new TaxSlab(s.IncomeFrom, s.IncomeTo, s.FixedTax, s.Rate))];

    // The calculation ----------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Each_salary_pays_the_tax_of_its_slab_spread_over_the_tax_year()
    {
        var (data, store) = await StoreAsync();
        var joined = new DateOnly(2024, 1, 1);
        // July 2025, the first month of tax year 2026: the year's taxable income is twelve such months, and a month's tax a twelfth of the year's.
        //   480,000: nothing.                         960,000: 1% of 360,000 = 3,600.
        //   1,800,000: 6,000 + 11% of 600,000.        3,000,000: 116,000 + 23% of 800,000.
        //   3,600,000: 346,000 + 30% of 400,000.      6,000,000: 616,000 + 35% of 1,900,000.
        //   12,000,000: 616,000 + 35% of 7,900,000 = 3,381,000, and 9% surcharge above 10 million = 3,685,290.
        (decimal Basic, decimal Tax)[] cases =
            [(40_000m, 0m), (80_000m, 300m), (150_000m, 6_000m), (250_000m, 25_000m), (300_000m, 38_833.33m), (500_000m, 106_750m), (1_000_000m, 307_107.50m)];
        var employees = new List<(Employee Employee, decimal Basic, decimal Tax)>();
        foreach (var (basic, tax) in cases) employees.Add((await PaidAsync(data, store, $"Test Slab {basic:0}", basic, joined), basic, tax));
        // Medical allowance is free of tax up to 10% of basic pay; house rent is taxed.
        var allowances = await PaidAsync(data, store, "Test Allowances", 100_000m, joined, null, ("HRA", 45_000m), ("MEDICAL", 10_000m));
        var overMedical = await PaidAsync(data, store, "Test Over Medical", 100_000m, joined, null, ("MEDICAL", 15_000m));

        var (run, slips) = await CalculatedAsync(data, store, 2025, 7);
        Assert.Null(run.Warnings);
        Assert.Equal((PayrollRunStatus.Calculated, 9), (run.Status, run.Employees));
        var rules = await new HrService(data).RuleSetForAsync("PK", run.PeriodEnd);
        foreach (var (employee, basic, tax) in employees)
        {
            var slip = slips[employee.Id];
            Assert.Equal((basic, basic, tax, 12 * basic), (slip.Gross, slip.Taxable, slip.Tax, slip.ProjectedAnnualTaxable));
            Assert.Equal(PayrollRules.AnnualTax(12 * basic, Slabs(rules), rules.SurchargeThreshold, rules.SurchargeRate), slip.ProjectedAnnualTax);
            // EOBI: 1% of the minimum wage from the employee, 5% from the employer; social security 6% of it for those earning up to Rs 40,000.
            Assert.Equal((400m, 2_000m, basic <= 40_000m ? 2_400m : 0m), (Line(slip, PayslipCodes.Eobi), Line(slip, PayslipCodes.EobiEmployer), Line(slip, PayslipCodes.SocialSecurity)));
            Assert.Equal(basic - tax - 400m, slip.Net);
            Assert.Equal(slip.Gross - slip.Deductions, slip.Net);
        }
        var both = slips[allowances.Id];
        Assert.Equal((155_000m, 145_000m, 5_450m, 149_150m), (both.Gross, both.Taxable, both.Tax, both.Net));   // 1,740,000: 6,000 + 11% of 540,000 = 65,400
        Assert.Equal(0m, both.Lines.Single(l => l.Code == "MEDICAL").TaxableAmount);
        var over = slips[overMedical.Id];
        Assert.Equal((105_000m, 1_050m), (over.Taxable, over.Tax));   // 5,000 of the medical allowance is above 10% of basic: 1,260,000 a year
        Assert.Equal(5_000m, over.Lines.Single(l => l.Code == "MEDICAL").TaxableAmount);
        Assert.Equal((slips.Values.Sum(s => s.Gross), slips.Values.Sum(s => s.Tax), slips.Values.Sum(s => s.Net)), (run.Gross, run.Tax, run.Net));
    }

    [SkippableFact]
    public async Task Joiners_unpaid_leave_absence_and_overtime_change_the_month_pay()
    {
        var (data, store) = await StoreAsync();
        // October 2025 has 31 days, so a day of Rs 62,000 a month is Rs 2,000 and an hour (8 a day) Rs 250.
        var joiner = await PaidAsync(data, store, "Test Joiner", 62_000m, new DateOnly(2025, 10, 16));
        var onLeave = await PaidAsync(data, store, "Test Unpaid Leave", 62_000m, new DateOnly(2025, 1, 1));
        var unpaid = (await new HrService(data).GetLeaveTypesAsync("PK")).Single(t => t.Code == "UL");
        var leave = new LeaveService(data);
        var request = await leave.RequestAsync(new LeaveRequest { EmployeeId = onLeave.Id, LeaveTypeId = unpaid.Id, FromDate = new DateOnly(2025, 10, 7), ToDate = new DateOnly(2025, 10, 8) }, Admin);
        await leave.DecideAsync(request.Id, true, null, Admin);

        // Joins on Monday 27 October with the time clock: on time twice, late once, two hours over once, and absent on Friday 31.
        var shift = await new AttendanceService(data).SaveShiftAsync(new Shift
            { Name = Unique("Payroll day "), StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0), BreakMinutes = 60, GraceMinutes = 10, Days = WorkDays.MondayToSaturday, IsActive = true });
        var cashier = (await new SetupService(data).GetRolesAsync()).Single(r => r.Name == Permissions.CashierRole);
        var account = await new SetupService(data).SaveUserAsync(new UserAccount { UserName = Unique("pay"), RoleId = cashier.Id, IsActive = true }, "pass");
        var tracked = await PaidAsync(data, store, "Test Time Clock", 62_000m, new DateOnly(2025, 10, 27), new Employee { UserAccountId = account.Id, ShiftId = shift.Id, TracksAttendance = true });
        var clock = new StaffService(data);
        foreach (var (day, inAt, outAt) in new[] { (27, new TimeOnly(9, 0), new TimeOnly(17, 0)), (28, new TimeOnly(9, 0), new TimeOnly(17, 0)),
                     (29, new TimeOnly(9, 30), new TimeOnly(17, 0)), (30, new TimeOnly(9, 0), new TimeOnly(19, 0)) })
        {
            await clock.PunchInAsync(account.Id, new DateOnly(2025, 10, day).ToDateTime(inAt));
            await clock.PunchOutAsync(account.Id, new DateOnly(2025, 10, day).ToDateTime(outAt));
        }

        var (run, slips) = await CalculatedAsync(data, store, 2025, 10);
        Assert.Null(run.Warnings);
        var j = slips[joiner.Id];
        Assert.Equal((16, 32_000m, 206.45m, 1_032.26m, 0m), (j.DaysEmployed, Line(j, PayslipCodes.Basic), Line(j, PayslipCodes.Eobi), Line(j, PayslipCodes.EobiEmployer), j.Tax));
        Assert.Equal(32_000m - 206.45m, j.Net);

        var u = slips[onLeave.Id];
        Assert.Equal((2m, 4_000m, 58_000m, 57_600m), (u.UnpaidLeaveDays, Line(u, PayslipCodes.UnpaidLeave), u.Taxable, u.Net));

        var t = slips[tracked.Id];
        Assert.Equal((5, 4m, 1, 1, 2m), (t.DaysEmployed, t.DaysPresent, t.AbsentDays, t.LateDays, t.OvertimeHours));
        Assert.Equal((10_000m, 2_000m, 1_000m, 0m), (Line(t, PayslipCodes.Basic), Line(t, PayslipCodes.Absence), Line(t, PayslipCodes.Overtime), Line(t, PayslipCodes.Lateness)));
        Assert.Equal((9_000m, 64.52m, 8_935.48m), (t.Taxable, Line(t, PayslipCodes.Eobi), t.Net));
    }

    [SkippableFact]
    public async Task The_tax_projection_counts_earlier_months_a_raise_and_a_previous_employer()
    {
        var (data, store) = await StoreAsync();
        var payroll = new PayrollService(data);
        var raised = await PaidAsync(data, store, "Test Raise", 150_000m, new DateOnly(2024, 1, 1));
        var (july, slips) = await CalculatedAsync(data, store, 2025, 7);
        Assert.Equal(6_000m, slips[raised.Id].Tax);
        await payroll.SaveSalaryAsync(new SalaryStructure { EmployeeId = raised.Id, EffectiveFrom = new DateOnly(2025, 8, 1), Basic = 200_000m }, Admin);

        // August: 150,000 paid, this month's 200,000 and ten more like it make 2,350,000, taxed 116,000 + 23% of 150,000 = 150,500;
        // less the 6,000 deducted in July, spread over August and the ten months after it.
        var (_, august) = await CalculatedAsync(data, store, 2025, 8);
        var slip = august[raised.Id];
        Assert.Equal((150_000m, 6_000m, 2_350_000m, 150_500m, 13_136.36m), (slip.TaxableBefore, slip.TaxBefore, slip.ProjectedAnnualTaxable, slip.ProjectedAnnualTax, slip.Tax));

        // Joined in September with 400,000 earned and 2,000 tax deducted by a previous employer in the same tax year:
        // 400,000 + 100,000 + 9 × 100,000 = 1,400,000, taxed 6,000 + 11% of 200,000 = 28,000; less 2,000, over ten months.
        var newcomer = await PaidAsync(data, store, "Test Previous Employer", 100_000m, new DateOnly(2025, 9, 1),
            new Employee { TracksAttendance = false, PriorTaxYear = 2025, PriorTaxableIncome = 400_000m, PriorTaxDeducted = 2_000m });
        var (_, september) = await CalculatedAsync(data, store, 2025, 9);
        var joined = september[newcomer.Id];
        Assert.Equal((400_000m, 2_000m, 1_400_000m, 28_000m, 2_600m), (joined.TaxableBefore, joined.TaxBefore, joined.ProjectedAnnualTaxable, joined.ProjectedAnnualTax, joined.Tax));
        Assert.Equal(PayrollRunStatus.Calculated, (await payroll.GetRunAsync(july.Id))!.Status);
    }

    // Posting, paying and loans ------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Posting_and_paying_a_run_balance_in_the_ledger_and_recover_loan_instalments()
    {
        var (data, store) = await StoreAsync();
        var payroll = new PayrollService(data);
        var ledger = new LedgerService(data);
        var joined = new DateOnly(2024, 1, 1);
        var banked = await PaidAsync(data, store, "Test Banked", 150_000m, joined,
            new Employee { TracksAttendance = false, PaymentMethod = SalaryPaymentMethod.Bank, BankName = "Test Bank", Iban = "PK36SCBL0000001123456702" });
        var canteen = await PaidAsync(data, store, "Test Canteen", 40_000m, joined);
        var fund = await PaidAsync(data, store, "Test Fund", 80_000m, joined);
        var borrower = await PaidAsync(data, store, "Test Borrower", 60_000m, joined);
        await payroll.SaveSalaryAsync(new SalaryStructure
        {
            EmployeeId = canteen.Id, EffectiveFrom = joined, Basic = 40_000m,
            Components = [new SalaryComponent { Kind = SalaryComponentKind.Deduction, Code = "CANTEEN", Name = "Canteen", Amount = 1_000m, AccountCode = "4900" }],
        }, Admin);
        await payroll.SaveSalaryAsync(new SalaryStructure
        {
            EmployeeId = fund.Id, EffectiveFrom = joined, Basic = 80_000m,
            Components = [new SalaryComponent { Kind = SalaryComponentKind.Deduction, Code = "FUND", Name = "Staff fund", Amount = 2_000m }],
        }, Admin);
        await Assert.ThrowsAsync<BusinessRuleException>(() => payroll.SaveSalaryAsync(new SalaryStructure
            { EmployeeId = fund.Id, EffectiveFrom = joined, Basic = 80_000m, Components = [new SalaryComponent { Code = "TAX", Name = "Tax", Amount = 1m }] }, Admin));

        // A loan of 30,000 paid from the cash, taken back 10,000 a month from July.
        var loan = await payroll.IssueLoanAsync(new EmployeeLoan
            { EmployeeId = borrower.Id, Kind = LoanKind.Loan, IssuedOn = new DateOnly(2025, 6, 20), Amount = 30_000m, Instalment = 10_000m, FirstMonth = new DateOnly(2025, 7, 1), Reason = "Test" }, null, Admin);
        Assert.Equal([new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 1), new DateOnly(2025, 9, 1)], loan.Instalments.Select(i => i.Month));
        var lent = (await ledger.GetEntryAsync(loan.JournalEntryId!.Value))!;
        Assert.Equal((30_000m, -30_000m), (On(lent, "1150"), On(lent, "1010")));

        var (run, slips) = await CalculatedAsync(data, store, 2025, 7);
        Assert.Equal((143_600m, 38_600m, 77_300m, 49_500m), (slips[banked.Id].Net, slips[canteen.Id].Net, slips[fund.Id].Net, slips[borrower.Id].Net));
        Assert.Equal((1_000m, 2_000m, 10_000m, 100m), (Line(slips[canteen.Id], "CANTEEN"), Line(slips[fund.Id], "FUND"), Line(slips[borrower.Id], PayslipCodes.Loan), slips[borrower.Id].Tax));

        var approver = Session("approver", Permissions.PayrollApprove);
        run = await payroll.ApproveAsync(run.Id, approver);
        run = await payroll.PostAsync(run.Id, Admin);
        Assert.Equal(PayrollRunStatus.Posted, run.Status);
        var posted = (await ledger.GetEntryAsync(run.JournalEntryId!.Value))!;
        Assert.Equal((JournalKind.Payroll, $"payroll:{run.Id}", run.PeriodEnd), (posted.Kind, posted.Source, posted.Date));
        Assert.Equal(posted.Lines.Sum(l => l.Debit), posted.Lines.Sum(l => l.Credit));
        Assert.Equal(340_400m, posted.Lines.Sum(l => l.Debit));
        Assert.Equal(330_000m, On(posted, "6010"));                       // the gross pay
        Assert.Equal(10_400m, On(posted, "6020"));                        // EOBI 4 × 2,000 and social security 2,400
        Assert.Equal(-6_400m, On(posted, "2310"));                        // income tax withheld
        Assert.Equal(-12_000m, On(posted, "2320"));                       // EOBI from employees 1,600 and the employer's 10,400
        Assert.Equal(-1_000m, On(posted, "4900"));                        // the canteen deduction, to its own account
        Assert.Equal(-10_000m, On(posted, "1150"));                       // the instalment
        Assert.Equal(-311_000m, On(posted, "2300"));                      // net pay 309,000 and the staff fund kept in salaries payable
        Assert.All(posted.Lines.Where(l => l.Account!.Code is "2300" or "1150"), l => Assert.Equal(LedgerPartyKind.Employee, l.PartyKind));
        Assert.All(posted.Lines, l => Assert.Equal(store, l.StoreId));

        var repaid = (await payroll.GetLoanAsync(loan.Id))!;
        Assert.Equal((LoanStatus.Active, 10_000m, run.Id), (repaid.Status, repaid.Instalments[0].Recovered, repaid.Instalments[0].PayrollRunId));
        await Assert.ThrowsAsync<BusinessRuleException>(() => payroll.CancelLoanAsync(loan.Id, "Changed mind", Admin));
        await Assert.ThrowsAsync<BusinessRuleException>(() => new HrService(data).DeleteEmployeeAsync(borrower.Id));

        await Assert.ThrowsAsync<BusinessRuleException>(() => payroll.PayAsync(run.Id, new DateOnly(2025, 6, 30), null, null, Admin));
        run = await payroll.PayAsync(run.Id, new DateOnly(2025, 8, 1), null, null, Admin);
        var paid = (await ledger.GetEntryAsync(run.PaymentEntryId!.Value))!;
        Assert.Equal(paid.Lines.Sum(l => l.Debit), paid.Lines.Sum(l => l.Credit));
        Assert.Equal((309_000m, -143_600m, -165_400m), (On(paid, "2300"), On(paid, "1050"), On(paid, "1010")));
        Assert.Equal((PayrollRunStatus.Paid, new DateOnly(2025, 8, 1)), (run.Status, run.PaidOn));

        // What is left in salaries payable for the store is the staff fund, owed to whoever keeps it.
        var balances = await ledger.BalancesAsync(new DateOnly(2025, 7, 1), new DateOnly(2025, 8, 31), store);
        Assert.Equal(-2_000m, balances.Where(b => b.Code == "2300").Sum(b => b.Debit - b.Credit));
        Assert.Equal(-10_000m, balances.Where(b => b.Code == "1150").Sum(b => b.Debit - b.Credit));
    }

    // Steps and who may take them ----------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task A_run_goes_from_draft_to_paid_one_step_at_a_time_with_a_second_person_approving()
    {
        var (data, store) = await StoreAsync();
        var payroll = new PayrollService(data);
        var hr = new HrService(data);
        var runner = Session("runner", Permissions.PayrollRun);
        var approver = Session("approver", Permissions.PayrollApprove);
        var viewer = Session("viewer", Permissions.HrView);
        var employee = await PaidAsync(data, store, "=Test Formula", 90_000m, new DateOnly(2024, 1, 1),
            new Employee { TracksAttendance = false, PaymentMethod = SalaryPaymentMethod.Bank, BankName = "Test Bank", Iban = "PK36SCBL0000001123456702", Cnic = "11111-2222222-3" });

        await Assert.ThrowsAsync<BusinessRuleException>(() => payroll.CreateRunAsync(2025, 12, store, null, viewer));
        var run = await payroll.CreateRunAsync(2025, 12, store, "December", runner);
        Assert.Equal(PayrollRunStatus.Draft, run.Status);
        await Assert.ThrowsAsync<BusinessRuleException>(() => payroll.CreateRunAsync(2025, 12, store, null, runner));
        await Assert.ThrowsAsync<BusinessRuleException>(() => payroll.CreateRunAsync(2025, 12, null, null, runner));
        await Assert.ThrowsAsync<BusinessRuleException>(() => payroll.ApproveAsync(run.Id, approver));   // not calculated yet

        run = await payroll.CalculateAsync(run.Id, runner, new DateOnly(2026, 1, 15));
        await Assert.ThrowsAsync<BusinessRuleException>(() => payroll.ApproveAsync(run.Id, runner));     // payroll.run does not approve
        run = await payroll.ApproveAsync(run.Id, approver);
        Assert.Equal((PayrollRunStatus.Approved, "approver"), (run.Status, run.ApprovedBy));
        await Assert.ThrowsAsync<BusinessRuleException>(() => payroll.CalculateAsync(run.Id, runner));
        await Assert.ThrowsAsync<BusinessRuleException>(() => payroll.DeleteRunAsync(run.Id, runner));
        await Assert.ThrowsAsync<BusinessRuleException>(() => payroll.PayAsync(run.Id, new DateOnly(2026, 1, 1), null, null, runner));
        run = await payroll.ReopenAsync(run.Id, approver);
        Assert.Equal((PayrollRunStatus.Calculated, null), (run.Status, run.ApprovedBy));
        run = await payroll.ApproveAsync(run.Id, approver);

        // An approved month fixes the salary and the rules it was paid by.
        await Assert.ThrowsAsync<BusinessRuleException>(() => payroll.SaveSalaryAsync(new SalaryStructure
            { EmployeeId = employee.Id, EffectiveFrom = new DateOnly(2025, 12, 15), Basic = 95_000m }, runner));
        await payroll.SaveSalaryAsync(new SalaryStructure { EmployeeId = employee.Id, EffectiveFrom = new DateOnly(2026, 1, 1), Basic = 95_000m }, runner);
        var pakistan = await hr.RuleSetForAsync("PK", new DateOnly(2025, 12, 31));
        await Assert.ThrowsAsync<BusinessRuleException>(() => hr.DeleteRuleSetAsync(pakistan.Id));

        await Assert.ThrowsAsync<BusinessRuleException>(() => payroll.PostAsync(run.Id, approver));     // posting is payroll.run's
        run = await payroll.PostAsync(run.Id, runner);
        run = await payroll.PayAsync(run.Id, new DateOnly(2026, 1, 1), "Bank", null, runner);
        Assert.Equal(PayrollRunStatus.Paid, run.Status);

        // The bank list keeps a name that looks like a formula as text.
        var csv = await payroll.BankTransfersCsvAsync(run.Id);
        var line = Assert.Single(csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1));
        Assert.StartsWith($"{employee.Code},'=Test Formula,11111-2222222-3,Test Bank,PK36SCBL0000001123456702,", line, StringComparison.Ordinal);

        await using var ctx = db.CreateContext();
        var steps = await ctx.AuditEntries.AsNoTracking().Where(a => a.EntityId == run.Id && a.Action == AuditActions.PayrollRun).Select(a => a.Details).ToListAsync();
        Assert.Equal(7, steps.Count);   // started, calculated, approved, reopened, approved, posted, paid
    }

    [SkippableFact]
    public async Task A_month_without_rules_pays_nobody_and_says_which_rule_set_to_add()
    {
        var (data, store) = await StoreAsync();
        var payroll = new PayrollService(data);
        await PaidAsync(data, store, "Test After The Rules", 90_000m, new DateOnly(2025, 1, 1));
        var run = await payroll.CreateRunAsync(2026, 7, store, null, Admin);
        run = await payroll.CalculateAsync(run.Id, Admin, new DateOnly(2026, 8, 15));
        Assert.Equal(0, run.Employees);
        Assert.Contains("no payroll rules for PK", run.Warnings, StringComparison.Ordinal);
        Assert.Contains("HR settings", run.Warnings, StringComparison.Ordinal);
        await Assert.ThrowsAsync<BusinessRuleException>(() => payroll.ApproveAsync(run.Id, Admin));
        await payroll.DeleteRunAsync(run.Id, Admin);
        Assert.Null(await payroll.GetRunAsync(run.Id));
    }

    // Final settlement ---------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task A_leaver_is_settled_with_the_days_worked_leave_paid_out_and_gratuity_and_left_out_of_the_month()
    {
        var (data, store) = await StoreAsync();
        var payroll = new PayrollService(data);
        var hr = new HrService(data);
        var leaver = await PaidAsync(data, store, "Test Leaver", 60_000m, new DateOnly(2019, 3, 1), null, ("HRA", 30_000m));
        var stays = await PaidAsync(data, store, "Test Stayer", 70_000m, new DateOnly(2019, 3, 1));
        await Assert.ThrowsAsync<BusinessRuleException>(() => payroll.CreateSettlementAsync(leaver.Id, null, Admin));   // no leaving date yet
        leaver.LeavingDate = new DateOnly(2025, 11, 20);
        leaver.LeavingReason = "Moved away";
        await hr.SaveEmployeeAsync(leaver);

        var settlement = await payroll.CreateSettlementAsync(leaver.Id, null, Admin);
        Assert.Equal((PayrollRunKind.FinalSettlement, new DateOnly(2025, 11, 20)), (settlement.Kind, settlement.PeriodEnd));
        await Assert.ThrowsAsync<BusinessRuleException>(() => payroll.CreateSettlementAsync(leaver.Id, null, Admin));
        settlement = await payroll.CalculateAsync(settlement.Id, Admin, new DateOnly(2025, 12, 15));
        var slip = Assert.Single(await payroll.GetPayslipsAsync(settlement.Id));

        // Twenty days of November's 30; 6 years and 8 months of service, the part year over six months counting as a year:
        // 7 × 30 days of the monthly pay (90,000) is 630,000. Annual leave left: 14 carried from 2024 and 10 months of 2025 earned.
        Assert.Equal((40_000m, 20_000m, 630_000m), (Line(slip, PayslipCodes.Basic), Line(slip, "HRA"), Line(slip, PayslipCodes.Gratuity)));
        var encashment = slip.Lines.Single(l => l.Code == PayslipCodes.LeaveEncashment);
        Assert.Equal((25.67m, 3_000m, 77_010m), (encashment.Quantity, encashment.Rate, encashment.Amount));
        // Nothing is projected after the last month: the tax is the year's tax on what is paid now (1% above 600,000).
        Assert.Equal((767_010m, 1_670.10m), (slip.Taxable, slip.Tax));
        Assert.Equal((266.67m, 1_333.33m), (Line(slip, PayslipCodes.Eobi), Line(slip, PayslipCodes.EobiEmployer)));

        // The month's run of the store pays the others only.
        var (month, slips) = await CalculatedAsync(data, store, 2025, 11);
        Assert.Equal([stays.Id], slips.Keys);
        Assert.Equal(1, month.Employees);
        await payroll.ApproveAsync(settlement.Id, Admin);
        var posted = await payroll.PostAsync(settlement.Id, Admin);
        var entry = (await new LedgerService(data).GetEntryAsync(posted.JournalEntryId!.Value))!;
        Assert.Equal(entry.Lines.Sum(l => l.Debit), entry.Lines.Sum(l => l.Credit));
        Assert.Equal(slip.Gross, On(entry, "6010"));
    }
}

public sealed class PayrollTestsOnSqlite(SqliteFixture db) : PayrollTests(db), IClassFixture<SqliteFixture>;

public sealed class PayrollTestsOnSqlServer(SqlServerFixture db) : PayrollTests(db), IClassFixture<SqlServerFixture>;
