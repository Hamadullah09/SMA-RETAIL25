using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.Data.Tests;

/// <summary>
/// Human resources on both engines: Pakistan's seeded rule set, leave types and holidays; rule sets as data; employees with
/// their identity and bank checks and the audit log that hides them; HR lists and documents; attendance from the time clock
/// with the roster, holidays and corrections; and leave with its approvals and balances.
/// </summary>
public abstract class HrTests(DatabaseFixture db)
{
    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..6];

    /// <summary>The services with the ledger's chart and Pakistan's HR rules seeded, as the server starts them.</summary>
    private async Task<DataContext> DataAsync()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        var data = db.Data;
        await new LedgerService(data).SeedAsync();
        await new HrService(data).SeedAsync();
        return data;
    }

    /// <summary>A user whose role has just these permissions, signed in.</summary>
    private async Task<(UserAccount User, SessionInfo Session)> UserAsync(params string[] permissions)
    {
        var setup = new SetupService(db.Data);
        var role = await setup.SaveRoleAsync(new Role { Name = Unique("HR test "), Level = 3 }, permissions);
        var saved = await setup.SaveUserAsync(new UserAccount { UserName = Unique("hr"), RoleId = role.Id, IsActive = true }, "pass");
        var user = (await setup.AuthenticateAsync(saved.UserName, "pass"))!;
        var session = new SessionInfo();
        session.SignIn(user);
        return (user, session);
    }

    private static Task<Employee> EmployeeAsync(DataContext data, string name, DateOnly joined, Guid? store = null, Guid? user = null, Gender? gender = null,
        Guid? manager = null, Guid? shift = null, bool tracks = true) =>
        new HrService(data).SaveEmployeeAsync(new Employee
        {
            FullName = name, JoiningDate = joined, StoreId = store, UserAccountId = user, Gender = gender, ManagerId = manager, ShiftId = shift, TracksAttendance = tracks,
        });

    // Rule sets ------------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Pakistan_is_seeded_once_with_the_rules_of_tax_year_2026_that_end_with_it()
    {
        var data = await DataAsync();
        var hr = new HrService(data);
        await hr.SeedAsync();
        var set = Assert.Single(await hr.GetRuleSetsAsync("PK"), r => r.EffectiveFrom == new DateOnly(2025, 7, 1));
        Assert.Contains("tax year 2026", set.Name, StringComparison.Ordinal);
        Assert.Equal((new DateOnly(2026, 6, 30), 7, "2025-26"), (set.EffectiveTo, set.TaxYearStartMonth, PayrollRules.TaxYearName(set.EffectiveFrom)));
        Assert.Equal(set.EffectiveFrom.AddYears(1).AddDays(-1), set.EffectiveTo);
        Assert.Contains("Finance Act 2025", set.Notes, StringComparison.Ordinal);
        Assert.Contains("Finance Act 2026", set.Notes, StringComparison.Ordinal);

        // The slabs run on from one to the next, and each slab's fixed tax is the tax at its start by the slab before.
        var slabs = set.TaxSlabs;
        Assert.Equal(6, slabs.Count);
        Assert.Equal(0m, slabs[0].IncomeFrom);
        Assert.Null(slabs[^1].IncomeTo);
        for (int i = 1; i < slabs.Count; i++)
        {
            Assert.Equal(slabs[i - 1].IncomeTo, slabs[i].IncomeFrom);
            Assert.True(slabs[i].Rate > slabs[i - 1].Rate);
            Assert.Equal(slabs[i - 1].FixedTax + (slabs[i].IncomeFrom - slabs[i - 1].IncomeFrom) * slabs[i - 1].Rate / 100m, slabs[i].FixedTax);
        }
        Assert.Equal((10_000_000m, 9m), (set.SurchargeThreshold, set.SurchargeRate));

        // EOBI and social security are worked out on the minimum wage: Rs 2,000 and Rs 400 a month, and Rs 2,400 from the employer.
        Assert.Equal((2_000m, 400m, 2_400m), (set.MinimumWage * set.EobiEmployerRate / 100m, set.MinimumWage * set.EobiEmployeeRate / 100m,
            set.MinimumWage * set.SocialSecurityRate / 100m));
        Assert.Equal(SocialSecurityBasis.MinimumWage, set.SocialSecurityBasis);
        var medical = Assert.Single(set.Allowances, a => a.Code == "MEDICAL");
        Assert.Equal((false, 10m), (medical.IsTaxable, medical.ExemptPercentOfBasic));

        Assert.Equal(set.Id, (await hr.RuleSetForAsync("PK", new DateOnly(2026, 6, 30))).Id);
        var next = await Assert.ThrowsAsync<BusinessRuleException>(() => hr.RuleSetForAsync("PK", new DateOnly(2026, 7, 1)));
        Assert.Contains("add a rule set", next.Message, StringComparison.Ordinal);

        Assert.Equal(6, (await hr.GetLeaveTypesAsync("PK")).Count);
        Assert.Equal(HrService.PakistanHolidays2026.Count, (await hr.GetHolidaysAsync("PK", 2026)).Count);
        Assert.Equal(HrService.PakistanHolidays2026.Count, HrService.PakistanHolidays2026.Select(h => h.Date).Distinct().Count());
    }

    [SkippableFact]
    public async Task Another_country_or_year_is_a_rule_set_of_its_own_and_wrong_figures_are_refused()
    {
        var data = await DataAsync();
        var hr = new HrService(data);
        const string country = "XA";
        PayrollRuleSet Set(DateOnly from, decimal topRate) => new()
        {
            Country = country, Name = $"Test land from {from:yyyy}", EffectiveFrom = from, MinimumWage = 30_000m, EobiEmployerRate = 5m, EobiEmployeeRate = 1m,
            TaxSlabs = [new() { IncomeFrom = 0, IncomeTo = 100_000m, Rate = 0 }, new() { IncomeFrom = 100_000m, FixedTax = 0, Rate = topRate }],
            Allowances = [new() { Code = "hra", Name = "House rent" }],
        };
        var first = await hr.SaveRuleSetAsync(Set(new DateOnly(2025, 7, 1), 10m));
        var second = await hr.SaveRuleSetAsync(Set(new DateOnly(2026, 7, 1), 5m));
        Assert.Equal("HRA", Assert.Single(first.Allowances).Code);
        Assert.Equal(first.Id, (await hr.RuleSetForAsync(country, new DateOnly(2026, 6, 30))).Id);
        Assert.Equal(second.Id, (await hr.RuleSetForAsync(country, new DateOnly(2026, 7, 1))).Id);

        // Changing a rule set replaces its slabs as given.
        first.TaxSlabs = [new() { IncomeFrom = 0, IncomeTo = 50_000m, Rate = 0 }, new() { IncomeFrom = 50_000m, IncomeTo = 200_000m, Rate = 10 },
            new() { IncomeFrom = 200_000m, FixedTax = 15_000m, Rate = 20 }];
        await hr.SaveRuleSetAsync(first);
        Assert.Equal([0m, 50_000m, 200_000m], (await hr.GetRuleSetAsync(first.Id))!.TaxSlabs.Select(s => s.IncomeFrom));

        await Assert.ThrowsAsync<BusinessRuleException>(() => hr.SaveRuleSetAsync(Set(new DateOnly(2025, 7, 1), 10m)));   // the same start twice
        var twin = Set(new DateOnly(2027, 7, 1), 10m);
        twin.TaxSlabs.Add(new PayrollTaxSlab { IncomeFrom = 100_000m, Rate = 20 });
        await Assert.ThrowsAsync<BusinessRuleException>(() => hr.SaveRuleSetAsync(twin));
        await Assert.ThrowsAsync<BusinessRuleException>(() => hr.SaveRuleSetAsync(Set(new DateOnly(2027, 7, 1), 101m)));
        var badPattern = Set(new DateOnly(2027, 7, 1), 10m);
        badPattern.CnicPattern = "[unclosed";
        await Assert.ThrowsAsync<BusinessRuleException>(() => hr.SaveRuleSetAsync(badPattern));
        var backwards = Set(new DateOnly(2027, 7, 1), 10m);
        backwards.EffectiveTo = new DateOnly(2027, 6, 30);
        await Assert.ThrowsAsync<BusinessRuleException>(() => hr.SaveRuleSetAsync(backwards));

        await hr.DeleteRuleSetAsync(second.Id);
        Assert.Single(await hr.GetRuleSetsAsync(country));
    }

    // Employees ------------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Employees_are_checked_against_their_countrys_formats_and_the_log_hides_their_numbers()
    {
        var data = await DataAsync();
        var hr = new HrService(data);
        var store = db.Station!.StoreId;
        var nextCode = await hr.NextCodeAsync();
        var employee = await hr.SaveEmployeeAsync(new Employee
        {
            FullName = "  Test Person One ", JoiningDate = new DateOnly(2025, 1, 6), StoreId = store, Cnic = "12345-1234567-1",
            PaymentMethod = SalaryPaymentMethod.Bank, BankName = "Test Bank", Iban = " pk36 scbl 0000 0011 2345 6702 ",
        });
        Assert.Equal((nextCode, "Test Person One", "PK36SCBL0000001123456702"), (employee.Code, employee.FullName, employee.Iban));

        var wrongCnic = await Assert.ThrowsAsync<BusinessRuleException>(() => hr.SaveEmployeeAsync(new Employee { FullName = "X", JoiningDate = new DateOnly(2025, 1, 6), Cnic = "1234512345671" }));
        Assert.Contains("35202-1234567-1", wrongCnic.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<BusinessRuleException>(() => hr.SaveEmployeeAsync(new Employee
            { FullName = "X", JoiningDate = new DateOnly(2025, 1, 6), PaymentMethod = SalaryPaymentMethod.Bank, Iban = "PK37SCBL0000001123456702" }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => hr.SaveEmployeeAsync(new Employee { FullName = "X", JoiningDate = new DateOnly(2025, 1, 6), PaymentMethod = SalaryPaymentMethod.Bank }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => hr.SaveEmployeeAsync(new Employee { FullName = "X", JoiningDate = new DateOnly(2025, 1, 6), LeavingDate = new DateOnly(2025, 1, 5) }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => hr.SaveEmployeeAsync(new Employee { FullName = "X", JoiningDate = new DateOnly(2025, 1, 6), Code = employee.Code }));

        // A user is linked to one employee, and nobody reports to themselves through others.
        var (user, _) = await UserAsync(Permissions.TimeClockPunch);
        var boss = await EmployeeAsync(data, "Test Boss", new DateOnly(2024, 1, 1), store, user.Id);
        await Assert.ThrowsAsync<BusinessRuleException>(() => EmployeeAsync(data, "Test Twin", new DateOnly(2024, 1, 1), store, user.Id));
        employee.ManagerId = boss.Id;
        employee = await hr.SaveEmployeeAsync(employee);
        boss.ManagerId = employee.Id;
        await Assert.ThrowsAsync<BusinessRuleException>(() => hr.SaveEmployeeAsync(boss));
        await Assert.ThrowsAsync<BusinessRuleException>(() => hr.DeleteEmployeeAsync(boss.Id));

        // The audit log says that the identity and bank numbers changed, never what they are.
        employee.Cnic = "54321-7654321-9";
        await hr.SaveEmployeeAsync(employee);
        await using var ctx = db.CreateContext();
        var lines = await ctx.AuditEntries.AsNoTracking().Where(a => a.EntityId == employee.Id && a.EntityName == nameof(Employee)).ToListAsync();
        Assert.Contains(lines, a => a.Action == "Added");
        var changed = lines.Last(a => a.Action == "Changed" && a.After != null && a.After.Contains(nameof(Employee.Cnic), StringComparison.Ordinal));
        Assert.Equal("(hidden)", JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(changed.After!)![nameof(Employee.Cnic)].GetString());
        Assert.All(lines, a => Assert.DoesNotContain("54321-7654321-9", (a.Before ?? "") + a.After + a.Details, StringComparison.Ordinal));
        Assert.All(lines, a => Assert.DoesNotContain("PK36SCBL", (a.Before ?? "") + a.After + a.Details, StringComparison.Ordinal));
    }

    [SkippableFact]
    public async Task Departments_designations_and_documents_are_kept_and_cannot_be_deleted_while_used()
    {
        var data = await DataAsync();
        var hr = new HrService(data);
        var department = await hr.SaveDepartmentAsync(new HrDepartment { Name = Unique("Floor "), IsActive = true });
        var designation = await hr.SaveDesignationAsync(new Designation { Name = Unique("Cashier "), IsActive = true });
        await Assert.ThrowsAsync<BusinessRuleException>(() => hr.SaveDepartmentAsync(new HrDepartment { Name = department.Name }));
        var employee = await hr.SaveEmployeeAsync(new Employee
            { FullName = "Test Person Two", JoiningDate = new DateOnly(2025, 2, 1), HrDepartmentId = department.Id, DesignationId = designation.Id });
        await Assert.ThrowsAsync<BusinessRuleException>(() => hr.DeleteDepartmentAsync(department.Id));
        await Assert.ThrowsAsync<BusinessRuleException>(() => hr.DeleteDesignationAsync(designation.Id));

        await Assert.ThrowsAsync<BusinessRuleException>(() => hr.AddDocumentAsync(employee.Id, "Empty", "empty.pdf", "application/pdf", []));
        await Assert.ThrowsAsync<BusinessRuleException>(() => hr.AddDocumentAsync(employee.Id, "Big", "big.pdf", "application/pdf", new byte[HrService.MaxDocumentBytes + 1]));
        var document = await hr.AddDocumentAsync(employee.Id, null, @"C:\scans\contract.pdf", "application/pdf", [1, 2, 3]);
        Assert.Equal(("contract", "contract.pdf", 3L), (document.Title, document.FileName, document.Size));
        Assert.Equal([1, 2, 3], (await hr.GetDocumentAsync(document.Id))!.Data);
        Assert.Single(await hr.GetDocumentsAsync(employee.Id));
        await hr.DeleteDocumentAsync(document.Id);
        Assert.Empty(await hr.GetDocumentsAsync(employee.Id));
        await using var ctx = db.CreateContext();
        Assert.Equal(2, await ctx.AuditEntries.CountAsync(a => a.EntityId == document.Id && a.Action == AuditActions.HrDocument));

        await hr.DeleteEmployeeAsync(employee.Id);
        await hr.DeleteDepartmentAsync(department.Id);
        await hr.DeleteDesignationAsync(designation.Id);
    }

    // Attendance -----------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Attendance_comes_from_the_time_clock_with_the_roster_holidays_and_approved_corrections()
    {
        var data = await DataAsync();
        var attendance = new AttendanceService(data);
        var shift = await attendance.SaveShiftAsync(new Shift
        {
            Name = Unique("Day "), StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0), BreakMinutes = 60, GraceMinutes = 10, Days = WorkDays.MondayToSaturday, IsActive = true,
        });
        var cashier = (await new SetupService(data).GetRolesAsync()).Single(r => r.Name == Permissions.CashierRole);
        var account = await new SetupService(data).SaveUserAsync(new UserAccount { UserName = Unique("clock"), RoleId = cashier.Id, IsActive = true }, "pass");
        var employee = await EmployeeAsync(data, "Test Clock Person", new DateOnly(2026, 1, 5), db.Station!.StoreId, account.Id, shift: shift.Id);

        // The week of Pakistan Day (Monday 23 March 2026).
        var monday = new DateOnly(2026, 3, 23);
        var clock = new StaffService(data);
        async Task WorkAsync(DateOnly day, int fromHour, int fromMinute, int toHour)
        {
            await clock.PunchInAsync(account.Id, day.ToDateTime(new TimeOnly(fromHour, fromMinute)));
            await clock.PunchOutAsync(account.Id, day.ToDateTime(new TimeOnly(toHour, 0)));
        }
        await WorkAsync(monday.AddDays(1), 9, 0, 17);    // Tuesday on time
        await WorkAsync(monday.AddDays(2), 9, 30, 17);   // Wednesday half an hour late
        await WorkAsync(monday.AddDays(5), 9, 0, 19);    // Saturday two hours over
        await attendance.SaveRosterDayAsync(new RosterDay { EmployeeId = employee.Id, Date = monday.AddDays(4), ShiftId = null, Note = "Swapped" });

        var today = new DateOnly(2026, 4, 1);
        var week = (await attendance.DaysAsync(monday, monday.AddDays(6), [employee.Id], today)).ToDictionary(d => d.Date);
        Assert.Equal(AttendanceStatus.Holiday, week[monday].Status);
        Assert.Equal("Pakistan Day", week[monday].Holiday);
        Assert.Equal((AttendanceStatus.Present, 420, 0), (week[monday.AddDays(1)].Status, week[monday.AddDays(1)].WorkedMinutes, week[monday.AddDays(1)].LateMinutes));
        Assert.Equal((AttendanceStatus.Present, 30), (week[monday.AddDays(2)].Status, week[monday.AddDays(2)].LateMinutes));
        Assert.Equal(AttendanceStatus.Absent, week[monday.AddDays(3)].Status);
        Assert.Equal(AttendanceStatus.Off, week[monday.AddDays(4)].Status);
        Assert.Equal((AttendanceStatus.Present, 120), (week[monday.AddDays(5)].Status, week[monday.AddDays(5)].OvertimeMinutes));
        Assert.Equal(AttendanceStatus.Off, week[monday.AddDays(6)].Status);
        // Days not yet come are still to come.
        Assert.Equal(AttendanceStatus.Scheduled, (await attendance.DaysAsync(monday.AddDays(3), monday.AddDays(3), [employee.Id], monday.AddDays(3))).Single().Status);

        // Thursday is corrected (a forgotten punch): it counts once someone else approves it.
        var (asker, askerSession) = await UserAsync(Permissions.WebSignIn, Permissions.HrAttendance);
        var (_, deciderSession) = await UserAsync(Permissions.WebSignIn, Permissions.HrAttendance);
        var (_, viewerSession) = await UserAsync(Permissions.WebSignIn, Permissions.HrView);
        var thursday = monday.AddDays(3);
        await Assert.ThrowsAsync<BusinessRuleException>(() => attendance.RequestCorrectionAsync(new AttendanceCorrection
            { EmployeeId = employee.Id, Date = thursday, Status = AttendanceStatus.Present, WorkedMinutes = 420, Reason = "Forgot to punch" }, viewerSession));
        await Assert.ThrowsAsync<BusinessRuleException>(() => attendance.RequestCorrectionAsync(new AttendanceCorrection
            { EmployeeId = employee.Id, Date = thursday, Status = AttendanceStatus.Present, WorkedMinutes = 420, Reason = " " }, askerSession));
        var correction = await attendance.RequestCorrectionAsync(new AttendanceCorrection
            { EmployeeId = employee.Id, Date = thursday, Status = AttendanceStatus.Present, WorkedMinutes = 420, LateMinutes = 5, Reason = "Forgot to punch" }, askerSession);
        Assert.Equal((HrApproval.Pending, asker.Id), (correction.Approval, correction.RequestedById));
        Assert.Equal(AttendanceStatus.Absent, (await attendance.DaysAsync(thursday, thursday, [employee.Id], today)).Single().Status);
        await Assert.ThrowsAsync<BusinessRuleException>(() => attendance.DecideCorrectionAsync(correction.Id, true, null, askerSession));
        await attendance.DecideCorrectionAsync(correction.Id, true, "Seen on camera", deciderSession);
        var corrected = (await attendance.DaysAsync(thursday, thursday, [employee.Id], today)).Single();
        Assert.Equal((AttendanceStatus.Present, 420, 5, true), (corrected.Status, corrected.WorkedMinutes, corrected.LateMinutes, corrected.Corrected));
        await Assert.ThrowsAsync<BusinessRuleException>(() => attendance.DecideCorrectionAsync(correction.Id, false, null, deciderSession));

        // The roster names another shift for a week; a shift on the roster cannot be deleted.
        var late = await attendance.SaveShiftAsync(new Shift
            { Name = Unique("Late "), StartTime = new TimeOnly(13, 0), EndTime = new TimeOnly(21, 0), BreakMinutes = 30, Days = WorkDays.Everyday, IsActive = true });
        await attendance.SetRosterAsync(monday.AddDays(7), [(employee.Id, late.Id)]);
        var row = Assert.Single(await attendance.GetRosterAsync(monday.AddDays(9)), r => r.EmployeeId == employee.Id);
        Assert.Equal((shift.Id, late.Id), (row.UsualShiftId, row.WeekShiftId));
        await Assert.ThrowsAsync<BusinessRuleException>(() => attendance.DeleteShiftAsync(late.Id));
        await Assert.ThrowsAsync<BusinessRuleException>(() => attendance.SaveShiftAsync(new Shift { Name = "Bad break", StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0), BreakMinutes = 60, Days = WorkDays.Monday }));
    }

    // Leave ----------------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Leave_is_counted_in_working_days_approved_by_the_manager_and_kept_within_the_balance()
    {
        var data = await DataAsync();
        var hr = new HrService(data);
        var leave = new LeaveService(data);
        var store = db.Station!.StoreId;
        var types = (await hr.GetLeaveTypesAsync("PK")).ToDictionary(t => t.Code);
        var (managerUser, managerSession) = await UserAsync(Permissions.WebSignIn, Permissions.HrView);
        var (ownUser, ownSession) = await UserAsync(Permissions.WebSignIn, Permissions.HrView, Permissions.HrLeave);
        var (_, otherSession) = await UserAsync(Permissions.WebSignIn, Permissions.HrView);
        var (_, officerSession) = await UserAsync(Permissions.WebSignIn, Permissions.HrView, Permissions.HrLeave);
        var manager = await EmployeeAsync(data, "Test Leave Manager", new DateOnly(2020, 1, 1), store, managerUser.Id);
        var employee = await EmployeeAsync(data, "Test Leave Person", new DateOnly(2024, 1, 1), store, ownUser.Id, Gender.Female, manager.Id, tracks: false);

        // Monday 2 to Saturday 7 March 2026 with the Saturday a half day: five and a half working days of the six-day week.
        var request = await leave.RequestAsync(new LeaveRequest
        {
            EmployeeId = employee.Id, LeaveTypeId = types["AL"].Id, FromDate = new DateOnly(2026, 3, 2), ToDate = new DateOnly(2026, 3, 7), LastDayHalf = true, Reason = "Family visit",
        }, ownSession);
        Assert.Equal((5.5m, LeaveRequestStatus.Pending), (request.Days, request.Status));
        await Assert.ThrowsAsync<BusinessRuleException>(() => leave.RequestAsync(new LeaveRequest
            { EmployeeId = employee.Id, LeaveTypeId = types["CL"].Id, FromDate = new DateOnly(2026, 3, 6), ToDate = new DateOnly(2026, 3, 6) }, ownSession));

        // Never one's own, nor someone who is neither the manager nor has hr.leave; the manager's user may.
        await Assert.ThrowsAsync<BusinessRuleException>(() => leave.DecideAsync(request.Id, true, null, ownSession));
        await Assert.ThrowsAsync<BusinessRuleException>(() => leave.DecideAsync(request.Id, true, null, otherSession));
        Assert.True(await leave.MayDecideAsync(employee.Id, managerSession));
        var approved = await leave.DecideAsync(request.Id, true, "Enjoy", managerSession);
        Assert.Equal(LeaveRequestStatus.Approved, approved.Status);

        var days = (await new AttendanceService(data).DaysAsync(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8), [employee.Id], new DateOnly(2026, 4, 1))).ToDictionary(d => d.Date);
        Assert.Equal((AttendanceStatus.Leave, 1m, "AL", true), (days[new DateOnly(2026, 3, 2)].Status, days[new DateOnly(2026, 3, 2)].LeaveDays, days[new DateOnly(2026, 3, 2)].LeaveCode,
            days[new DateOnly(2026, 3, 2)].LeavePaid));
        Assert.Equal(0.5m, days[new DateOnly(2026, 3, 7)].LeaveDays);
        Assert.Equal(AttendanceStatus.Off, days[new DateOnly(2026, 3, 8)].Status);

        // Annual leave is earned a twelfth a month, and up to 14 days are carried from each year into the next.
        var annual = (await leave.BalancesAsync(2026, [employee.Id], new DateOnly(2026, 3, 31))).Single(b => b.Code == "AL");
        Assert.Equal((14m, 3.5m, 14m, 5.5m, 0m, 12m), (annual.Entitled, annual.Earned, annual.CarriedIn, annual.Taken, annual.Pending, annual.Available!.Value));
        Assert.DoesNotContain(await leave.BalancesAsync(2026, [employee.Id]), b => b.Code == "PL");   // paternity is for men

        // Casual leave is ten days a year: eleven working days are refused, the balance is raised by hand with hr.leave only.
        var tooMuch = await Assert.ThrowsAsync<BusinessRuleException>(() => leave.RequestAsync(new LeaveRequest
            { EmployeeId = employee.Id, LeaveTypeId = types["CL"].Id, FromDate = new DateOnly(2026, 4, 1), ToDate = new DateOnly(2026, 4, 13) }, ownSession));
        Assert.Contains("10 days", tooMuch.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<BusinessRuleException>(() => leave.AdjustAsync(new LeaveAdjustment
            { EmployeeId = employee.Id, LeaveTypeId = types["CL"].Id, Year = 2026, Days = 1, Reason = "Long service" }, managerSession));
        await leave.AdjustAsync(new LeaveAdjustment { EmployeeId = employee.Id, LeaveTypeId = types["CL"].Id, Year = 2026, Days = 1, Reason = "Long service" }, officerSession);
        var casual = await leave.RequestAsync(new LeaveRequest
            { EmployeeId = employee.Id, LeaveTypeId = types["CL"].Id, FromDate = new DateOnly(2026, 4, 1), ToDate = new DateOnly(2026, 4, 13) }, ownSession);
        Assert.Equal(11m, casual.Days);

        // Maternity leave is for women with four months' service; the one who asked may cancel.
        var newcomer = await EmployeeAsync(data, "Test New Person", new DateOnly(2026, 3, 1), store, gender: Gender.Female);
        var man = await EmployeeAsync(data, "Test Male Person", new DateOnly(2020, 3, 1), store, gender: Gender.Male);
        await Assert.ThrowsAsync<BusinessRuleException>(() => leave.RequestAsync(new LeaveRequest
            { EmployeeId = newcomer.Id, LeaveTypeId = types["ML"].Id, FromDate = new DateOnly(2026, 4, 1), ToDate = new DateOnly(2026, 4, 30) }, officerSession));
        await Assert.ThrowsAsync<BusinessRuleException>(() => leave.RequestAsync(new LeaveRequest
            { EmployeeId = man.Id, LeaveTypeId = types["ML"].Id, FromDate = new DateOnly(2026, 4, 1), ToDate = new DateOnly(2026, 4, 30) }, officerSession));
        var cancelled = await leave.CancelAsync(casual.Id, ownSession);
        Assert.Equal(LeaveRequestStatus.Cancelled, cancelled.Status);
        await Assert.ThrowsAsync<BusinessRuleException>(() => leave.CancelAsync(casual.Id, ownSession));
    }
}

public sealed class HrTestsOnSqlite(SqliteFixture db) : HrTests(db), IClassFixture<SqliteFixture>;

public sealed class HrTestsOnSqlServer(SqlServerFixture db) : HrTests(db), IClassFixture<SqlServerFixture>;
