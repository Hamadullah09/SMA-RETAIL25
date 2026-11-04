using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;
using RetailPlus.Sync.Tests;

namespace RetailPlus.Server.Tests;

/// <summary>
/// The HR and payroll API (phase E3) through WebApplicationFactory: who may open what, store limits, versions, a payroll
/// run from draft to paid with its payslips, lists and reports, leave and corrections, and that nothing of it reaches the
/// tills but the users they sign in with.
/// </summary>
public sealed class HrApiTests(SyncWorld world) : IClassFixture<SyncWorld>
{
    private void Require() => Skip.IfNot(world.Available, world.UnavailableReason);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<T>(SyncWorld.Json))!;

    /// <summary>A user of the store (none: every store's) with a role of just these permissions, signed in, with the CSRF header.</summary>
    private async Task<HttpClient> ClientAsync(Guid? store, params string[] permissions)
    {
        var setup = new SetupService(world.ServerData);
        var role = await setup.SaveRoleAsync(new Role { Name = "HR API " + Guid.NewGuid().ToString("N")[..6], Level = 3 }, permissions);
        return await SignInAsync(role.Id, store);
    }

    /// <summary>A user with one of the standard roles.</summary>
    private async Task<HttpClient> StandardAsync(string role, Guid? store) =>
        await SignInAsync((await new SetupService(world.ServerData).GetRolesAsync()).Single(r => r.Name == role).Id, store);

    private async Task<HttpClient> SignInAsync(Guid role, Guid? store)
    {
        var name = "hr-" + Guid.NewGuid().ToString("N")[..8];
        await new SetupService(world.ServerData).SaveUserAsync(new UserAccount { UserName = name, RoleId = role, StoreId = store, IsActive = true }, WebClients.UserPassword);
        var client = await world.SignInAsync(name, WebClients.UserPassword);
        client.DefaultRequestHeaders.Add(CsrfGuard.Header, CsrfGuard.Value);
        return client;
    }

    private static object NewEmployee(string name, Guid? store, string? iban = null, int? priorTaxYear = null, decimal priorIncome = 0, decimal priorTax = 0, Guid? user = null) => new
    {
        fullName = name, joiningDate = "2024-01-01", storeId = store, userId = user, tracksAttendance = false, overtimeEligible = true, eobiApplies = true,
        socialSecurityApplies = true, paymentMethod = iban is null ? "Cash" : "Bank", bankName = iban is null ? null : "Test Bank", iban,
        priorTaxYear, priorTaxableIncome = priorIncome, priorTaxDeducted = priorTax, employmentType = "Permanent",
    };

    private static async Task<EmployeeDetail> AddEmployeeAsync(HttpClient client, object body)
    {
        using var response = await client.PostAsJsonAsync("/api/hr/employees", body, SyncWorld.Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<EmployeeDetail>(response);
    }

    private static async Task<RunRow> StepAsync(HttpClient client, RunRow run, string step, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await client.PostAsJsonAsync($"/api/payroll/runs/{run.Id}/{step}", new RunActionBody(run.Version), SyncWorld.Json);
        Assert.Equal(expected, response.StatusCode);
        return expected == HttpStatusCode.OK ? await ReadAsync<RunRow>(response) : run;
    }

    [SkippableFact]
    public async Task HR_pages_need_their_permissions_and_payroll_is_for_those_who_run_or_approve_it()
    {
        Require();
        var store = await world.AddStoreAsync();
        using var webOnly = await ClientAsync(store.Id, Permissions.WebSignIn);
        using var viewer = await ClientAsync(store.Id, Permissions.WebSignIn, Permissions.HrView);
        using var officer = await StandardAsync(Permissions.HrOfficerRole, store.Id);

        Assert.Equal(HttpStatusCode.Forbidden, await WebClients.StatusAsync(webOnly.GetAsync("/api/hr/employees")));
        Assert.Equal(HttpStatusCode.OK, await WebClients.StatusAsync(viewer.GetAsync("/api/hr/employees")));
        Assert.Equal(HttpStatusCode.Forbidden, await WebClients.StatusAsync(viewer.PostAsJsonAsync("/api/hr/employees", NewEmployee("Test Viewer Made", store.Id), SyncWorld.Json)));
        var employee = await AddEmployeeAsync(officer, NewEmployee("Test Officer Made", store.Id));

        // Salaries, runs and loans: the HR officer runs payroll; hr.view alone sees none of it.
        Assert.Equal(HttpStatusCode.Forbidden, await WebClients.StatusAsync(viewer.GetAsync($"/api/hr/employees/{employee.Id}/salary")));
        Assert.Equal(HttpStatusCode.Forbidden, await WebClients.StatusAsync(viewer.GetAsync("/api/payroll/runs")));
        Assert.Equal(HttpStatusCode.Forbidden, await WebClients.StatusAsync(viewer.GetAsync("/api/payroll/loans")));
        Assert.Equal(HttpStatusCode.OK, await WebClients.StatusAsync(officer.GetAsync($"/api/hr/employees/{employee.Id}/salary")));
        Assert.Equal(HttpStatusCode.OK, await WebClients.StatusAsync(officer.GetAsync("/api/payroll/runs")));

        // The payroll reports are listed only for payroll's users.
        var viewerReports = await viewer.GetFromJsonAsync<List<HrReportInfo>>("/api/hr/reports", SyncWorld.Json);
        var officerReports = await officer.GetFromJsonAsync<List<HrReportInfo>>("/api/hr/reports", SyncWorld.Json);
        Assert.DoesNotContain(viewerReports!, r => r.Key == "salary-sheet");
        Assert.Contains(officerReports!, r => r.Key == "salary-sheet");
        Assert.Equal(HttpStatusCode.Forbidden, await WebClients.StatusAsync(viewer.GetAsync("/api/hr/reports/eobi?month=2026-03")));
        Assert.Equal(HttpStatusCode.OK, await WebClients.StatusAsync(viewer.GetAsync("/api/hr/reports/headcount")));
    }

    [SkippableFact]
    public async Task A_store_administrator_sees_and_keeps_only_the_employees_of_their_store()
    {
        Require();
        var a = await world.AddStoreAsync();
        var b = await world.AddStoreAsync();
        using var super = await world.SuperAsync();
        var atB = await AddEmployeeAsync(super, NewEmployee("Test Store B Person", b.Id));
        var headOffice = await AddEmployeeAsync(super, NewEmployee("Test Head Office Person", null));
        using var adminA = await world.AdminOfAsync(a.Id);
        var own = await AddEmployeeAsync(adminA, NewEmployee("Test Store A Person", a.Id));

        Assert.Equal(HttpStatusCode.NotFound, await WebClients.StatusAsync(adminA.GetAsync($"/api/hr/employees/{atB.Id}")));
        Assert.Equal(HttpStatusCode.NotFound, await WebClients.StatusAsync(adminA.GetAsync($"/api/hr/employees/{headOffice.Id}")));
        var list = (await adminA.GetFromJsonAsync<PageOf<EmployeeRow>>("/api/hr/employees?pageSize=500&status=all", SyncWorld.Json))!;
        Assert.Contains(list.Items, e => e.Id == own.Id);
        Assert.All(list.Items, e => Assert.Equal(a.Id, e.StoreId));
        Assert.Equal(HttpStatusCode.Forbidden, await WebClients.StatusAsync(adminA.PostAsJsonAsync("/api/hr/employees", NewEmployee("Test Elsewhere", b.Id), SyncWorld.Json)));
        Assert.Equal(HttpStatusCode.Forbidden, await WebClients.StatusAsync(adminA.PostAsJsonAsync("/api/hr/employees", NewEmployee("Test Nowhere", null), SyncWorld.Json)));
        Assert.Equal(HttpStatusCode.Forbidden, await WebClients.StatusAsync(adminA.PostAsJsonAsync("/api/payroll/runs", new RunBody(2026, 1, null, null), SyncWorld.Json)));
        Assert.Equal(HttpStatusCode.Forbidden, await WebClients.StatusAsync(adminA.PostAsJsonAsync("/api/payroll/runs", new RunBody(2026, 1, b.Id, null), SyncWorld.Json)));
        // With more than one store, the shared HR lists are changed by those who see every store.
        Assert.Equal(HttpStatusCode.Forbidden, await WebClients.StatusAsync(adminA.PostAsJsonAsync("/api/hr/departments", new HrListItem(null, null, "Test Shared", true), SyncWorld.Json)));
        Assert.Equal(HttpStatusCode.Created, await WebClients.StatusAsync(super.PostAsJsonAsync("/api/hr/departments",
            new HrListItem(null, null, "Test Shared " + Guid.NewGuid().ToString("N")[..4], true), SyncWorld.Json)));
    }

    [SkippableFact]
    public async Task An_employee_is_changed_with_its_version_and_checked_against_the_countrys_formats()
    {
        Require();
        var store = await world.AddStoreAsync();
        using var officer = await StandardAsync(Permissions.HrOfficerRole, store.Id);
        var saved = await AddEmployeeAsync(officer, NewEmployee("Test Versioned", store.Id));
        Assert.Equal("PK", saved.Country);
        using (var put = await officer.PutAsJsonAsync($"/api/hr/employees/{saved.Id}", saved with { Phone = "0300 0000000" }, SyncWorld.Json))
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        using (var stale = await officer.PutAsJsonAsync($"/api/hr/employees/{saved.Id}", saved with { Phone = "0300 1111111" }, SyncWorld.Json))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var current = (await officer.GetFromJsonAsync<EmployeeDetail>($"/api/hr/employees/{saved.Id}", SyncWorld.Json))!;
        Assert.Equal("0300 0000000", current.Phone);

        using var wrong = await officer.PutAsJsonAsync($"/api/hr/employees/{saved.Id}", current with { Cnic = "3520212345671" }, SyncWorld.Json);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("35202-1234567-1", await wrong.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var sets = (await officer.GetFromJsonAsync<List<RuleSetInfo>>("/api/hr/rule-sets?country=PK", SyncWorld.Json))!;
        var seeded = Assert.Single(sets, r => r.EffectiveFrom == new DateOnly(2025, 7, 1));
        Assert.Equal((new DateOnly(2026, 6, 30), 6), (seeded.EffectiveTo, seeded.TaxSlabs.Count));
    }

    [SkippableFact]
    public async Task A_payroll_run_goes_from_draft_to_paid_with_a_second_person_approving()
    {
        Require();
        var store = await world.AddStoreAsync();
        using var officer = await StandardAsync(Permissions.HrOfficerRole, store.Id);
        using var approver = await ClientAsync(store.Id, Permissions.WebSignIn, Permissions.HrView, Permissions.PayrollApprove);
        // March 2026, the ninth month of tax year 2026: the banked employee was paid 150,000 a month from July with 6,000 tax
        // each month, so 1,200,000 and 48,000 so far; with March and three more months the year is 1,800,000, taxed 72,000.
        var banked = await AddEmployeeAsync(officer, NewEmployee("Test API Banked", store.Id, "PK36SCBL0000001123456702", 2025, 1_200_000m, 48_000m));
        var cash = await AddEmployeeAsync(officer, NewEmployee("Test API Cash", store.Id));
        foreach (var (id, basic) in new[] { (banked.Id!.Value, 150_000m), (cash.Id!.Value, 40_000m) })
            using (var salary = await officer.PostAsJsonAsync($"/api/hr/employees/{id}/salary", new SalaryInfo(null, new DateOnly(2025, 7, 1), basic, null, null, []), SyncWorld.Json))
                Assert.Equal(HttpStatusCode.Created, salary.StatusCode);

        using var created = await officer.PostAsJsonAsync("/api/payroll/runs", new RunBody(2026, 3, store.Id, "Test month"), SyncWorld.Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var run = await ReadAsync<RunRow>(created);
        Assert.Equal(PayrollRunStatus.Draft, run.Status);
        run = await StepAsync(officer, run, "calculate");
        Assert.Equal((PayrollRunStatus.Calculated, 2), (run.Status, run.Employees));
        await StepAsync(officer, run, "approve", HttpStatusCode.Forbidden);
        var stale = run;
        run = await StepAsync(approver, run, "approve");
        await StepAsync(approver, stale, "reopen", HttpStatusCode.Conflict);
        Assert.Equal((PayrollRunStatus.Approved, 2), (run.Status, run.Employees));
        run = await StepAsync(officer, run, "post");
        Assert.NotNull(run.JournalNumber);
        using (var pay = await officer.PostAsJsonAsync($"/api/payroll/runs/{run.Id}/pay", new PayBody(run.Version, new DateOnly(2026, 4, 1), null, null), SyncWorld.Json))
        {
            Assert.Equal(HttpStatusCode.OK, pay.StatusCode);
            run = await ReadAsync<RunRow>(pay);
        }
        Assert.Equal((PayrollRunStatus.Paid, new DateOnly(2026, 4, 1)), (run.Status, run.PaidOn));
        Assert.NotNull(run.PaymentJournalNumber);

        var detail = (await officer.GetFromJsonAsync<RunDetail>($"/api/payroll/runs/{run.Id}", SyncWorld.Json))!;
        var slip = detail.Payslips.Single(p => p.EmployeeId == banked.Id);
        Assert.Equal((150_000m, 6_000m, 400m, 143_600m), (slip.Gross, slip.Tax, slip.Eobi, slip.Net));
        var other = detail.Payslips.Single(p => p.EmployeeId == cash.Id);
        Assert.Equal((40_000m, 0m, 39_600m), (other.Gross, other.Tax, other.Net));
        var payslip = (await officer.GetFromJsonAsync<PayslipDetail>($"/api/payroll/payslips/{slip.Id}", SyncWorld.Json))!;
        Assert.Equal((1_200_000m, 48_000m, 1_800_000m, 72_000m), (payslip.TaxableBefore, payslip.TaxBefore, payslip.ProjectedAnnualTaxable, payslip.ProjectedAnnualTax));
        Assert.Contains(payslip.Lines, l => l.Code == PayslipCodes.EobiEmployer && l.Amount == 2_000m);

        var csv = await officer.GetStringAsync($"/api/payroll/runs/{run.Id}/bank-transfers");
        Assert.Contains("PK36SCBL0000001123456702,143600.00", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("Test API Cash", csv, StringComparison.Ordinal);
        var sheet = (await officer.GetFromJsonAsync<CashSheet>($"/api/payroll/runs/{run.Id}/cash-sheet", SyncWorld.Json))!;
        Assert.Equal(39_600m, sheet.Total);
        var salarySheet = (await officer.GetFromJsonAsync<ReportResult>($"/api/hr/reports/salary-sheet?runId={run.Id}", SyncWorld.Json))!;
        Assert.Equal(2, salarySheet.Rows.Count);
        var eobi = (await officer.GetFromJsonAsync<ReportResult>($"/api/hr/reports/eobi?month=2026-03&storeId={store.Id}", SyncWorld.Json))!;
        Assert.Equal(2, eobi.Rows.Count);

        // Paid is the last step: it cannot be deleted or calculated again.
        Assert.Equal(HttpStatusCode.BadRequest, await WebClients.StatusAsync(officer.DeleteAsync($"/api/payroll/runs/{run.Id}")));
        await StepAsync(officer, run, "calculate", HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task Leave_and_attendance_corrections_are_decided_by_someone_else()
    {
        Require();
        var store = await world.AddStoreAsync();
        using var officer = await StandardAsync(Permissions.HrOfficerRole, store.Id);
        using var second = await StandardAsync(Permissions.HrOfficerRole, store.Id);
        var employee = await AddEmployeeAsync(officer, NewEmployee("Test API Leave", store.Id));
        var annual = (await officer.GetFromJsonAsync<List<LeaveTypeInfo>>("/api/hr/leave-types?country=PK", SyncWorld.Json))!.Single(t => t.Code == "AL");

        using var asked = await officer.PostAsJsonAsync("/api/hr/leave-requests",
            new LeaveRequestBody(employee.Id!.Value, annual.Id!.Value, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 3), false, false, "Wedding"), SyncWorld.Json);
        Assert.Equal(HttpStatusCode.Created, asked.StatusCode);
        var request = await ReadAsync<LeaveRequestRow>(asked);
        Assert.Equal((2m, LeaveRequestStatus.Pending, true), (request.Days, request.Status, request.MayDecide));
        using (var approve = await officer.PostAsJsonAsync($"/api/hr/leave-requests/{request.Id}/approve", new DecisionRequest(null), SyncWorld.Json))
            Assert.Equal(LeaveRequestStatus.Approved, (await ReadAsync<LeaveRequestRow>(approve)).Status);
        var balances = (await officer.GetFromJsonAsync<List<LeaveBalanceRow>>($"/api/hr/leave-balances?year=2026&employeeId={employee.Id}", SyncWorld.Json))!;
        Assert.Equal(2m, balances.Single(b => b.LeaveCode == "AL").Taken);

        using var corrected = await officer.PostAsJsonAsync("/api/hr/attendance/corrections",
            new CorrectionRequest(employee.Id!.Value, new DateOnly(2026, 3, 4), AttendanceStatus.Absent, 0, 0, 0, 0, "Did not come"), SyncWorld.Json);
        Assert.Equal(HttpStatusCode.Created, corrected.StatusCode);
        var correction = await ReadAsync<CorrectionRow>(corrected);
        Assert.Equal(HttpStatusCode.BadRequest, await WebClients.StatusAsync(officer.PostAsJsonAsync($"/api/hr/attendance/corrections/{correction.Id}/approve", new DecisionRequest(null), SyncWorld.Json)));
        Assert.Equal(HttpStatusCode.OK, await WebClients.StatusAsync(second.PostAsJsonAsync($"/api/hr/attendance/corrections/{correction.Id}/approve", new DecisionRequest("Checked"), SyncWorld.Json)));
        var day = (await officer.GetFromJsonAsync<List<AttendanceRow>>($"/api/hr/attendance?from=2026-03-04&to=2026-03-04&storeId={store.Id}", SyncWorld.Json))!
            .Single(d => d.EmployeeId == employee.Id);
        Assert.Equal((AttendanceStatus.Absent, true), (day.Status, day.Corrected));
    }

    [SkippableFact]
    public async Task Employees_pay_and_loans_stay_on_the_server_and_only_their_users_go_down_to_the_tills()
    {
        Require();
        var store = await world.AddStoreAsync();
        var till = await world.NewJoinedTillAsync(store.Id);
        using var super = await world.SuperAsync();
        var cashier = (await new SetupService(world.ServerData).GetRolesAsync()).Single(r => r.Name == Permissions.CashierRole).Id;
        var userName = "punch-" + Guid.NewGuid().ToString("N")[..6];
        using (var user = await super.PostAsJsonAsync("/api/users", new UserBody(null, userName, "Test Puncher", cashier, store.Id, true, "Punch-pass-1"), SyncWorld.Json))
            Assert.Equal(HttpStatusCode.Created, user.StatusCode);
        var account = (await new SetupService(world.ServerData).GetUsersAsync()).Single(u => u.UserName == userName);
        var employee = await AddEmployeeAsync(super, NewEmployee("Test Private Person", store.Id, "PK36SCBL0000001123456702", user: account.Id));
        using (var salary = await super.PostAsJsonAsync($"/api/hr/employees/{employee.Id}/salary", new SalaryInfo(null, new DateOnly(2025, 7, 1), 77_777m, null, null, []), SyncWorld.Json))
            Assert.Equal(HttpStatusCode.Created, salary.StatusCode);
        using (var loan = await super.PostAsJsonAsync("/api/payroll/loans",
                   new LoanBody(employee.Id!.Value, LoanKind.Advance, new DateOnly(2026, 2, 10), 12_345m, 12_345m, new DateOnly(2026, 3, 1), "Test", null), SyncWorld.Json))
            Assert.Equal(HttpStatusCode.Created, loan.StatusCode);

        await till.SyncAsync();
        await using var db = till.Data.Create();
        Assert.True(await db.UserAccounts.AnyAsync(u => u.UserName == userName));
        Assert.False(await db.Employees.AnyAsync());
        Assert.False(await db.SalaryStructures.AnyAsync());
        Assert.False(await db.EmployeeLoans.AnyAsync());
        Assert.False(await db.JournalEntries.AnyAsync());
        var lines = await db.AuditEntries.AsNoTracking().ToListAsync();
        Assert.DoesNotContain(lines, a => a.EntityName != null && (HrTables.Names.Contains(a.EntityName) || a.EntityName == nameof(JournalEntry)));
        Assert.All(lines, a => Assert.DoesNotContain("Test Private Person", a.Details ?? "", StringComparison.Ordinal));
        await using var server = world.ServerData.Create();
        Assert.True(await server.AuditEntries.AnyAsync(a => a.StoreId == store.Id && a.EntityId == employee.Id));
    }
}
