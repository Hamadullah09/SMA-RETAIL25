using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Services;
using RetailPlus.Reports;

namespace RetailPlus.Server;

public sealed record SalaryComponentInfo(SalaryComponentKind Kind, string Code, string Name, decimal Amount, string? AccountCode);
public sealed record SalaryInfo(Guid? Id, DateOnly EffectiveFrom, decimal Basic, string? Note, string? ChangedBy, IReadOnlyList<SalaryComponentInfo> Components,
    decimal Gross = 0, decimal FixedDeductions = 0);
public sealed record LoanInstalmentInfo(int Number, DateOnly Month, decimal Amount, decimal Recovered);
public sealed record LoanRow(Guid Id, long Number, Guid EmployeeId, string Code, string Name, LoanKind Kind, DateOnly IssuedOn, decimal Amount, decimal Instalment,
    DateOnly FirstMonth, LoanStatus Status, decimal Recovered, decimal Outstanding, string? Reason, string? PaidFromAccount, IReadOnlyList<LoanInstalmentInfo> Instalments);
public sealed record LoanBody(Guid EmployeeId, LoanKind Kind, DateOnly IssuedOn, decimal Amount, decimal Instalment, DateOnly? FirstMonth, string? Reason, string? PaidFrom);
public sealed record ReasonBody(string? Reason);
public sealed record RunRow(Guid Id, string Version, long Number, PayrollRunKind Kind, int Year, int Month, string Title, DateOnly PeriodStart, DateOnly PeriodEnd,
    Guid? StoreId, string? StoreCode, Guid? EmployeeId, string? EmployeeName, PayrollRunStatus Status, int Employees, decimal Gross, decimal Deductions, decimal Net,
    decimal Tax, decimal EmployerContributions, string? Notes, IReadOnlyList<string> Warnings, string? CreatedBy, string? CalculatedBy, DateTime? CalculatedAtUtc,
    string? ApprovedBy, DateTime? ApprovedAtUtc, string? PostedBy, DateTime? PostedAtUtc, long? JournalNumber, DateOnly? PaidOn, string? PaidBy, long? PaymentJournalNumber);
public sealed record RunBody(int Year, int Month, Guid? StoreId, string? Notes);
public sealed record SettlementBody(Guid EmployeeId, string? Notes);
public sealed record RunActionBody(string? Version);
public sealed record PayBody(string? Version, DateOnly? PaidOn, string? BankAccount, string? CashAccount);
public sealed record PayslipRow(Guid Id, Guid EmployeeId, string Code, string Name, string? Designation, string? Department, string? StoreCode, SalaryPaymentMethod PaymentMethod,
    int DaysEmployed, decimal DaysPresent, decimal PaidLeaveDays, decimal UnpaidLeaveDays, int AbsentDays, int LateDays, decimal OvertimeHours, decimal Basic,
    decimal Allowances, decimal Overtime, decimal Commission, decimal OtherEarnings, decimal Gross, decimal Attendance, decimal Tax, decimal Eobi, decimal Loans,
    decimal OtherDeductions, decimal Deductions, decimal Net, decimal EmployerContributions);
public sealed record RunDetail(RunRow Run, IReadOnlyList<PayslipRow> Payslips);
public sealed record PayslipLineInfo(PayslipLineKind Kind, string Code, string Name, decimal Amount, decimal? Quantity, decimal? Rate);
public sealed record PayslipDetail(PayslipRow Row, Guid RunId, long RunNumber, string RunTitle, PayrollRunStatus RunStatus, DateOnly PeriodStart, DateOnly PeriodEnd,
    string? Cnic, string? BankName, string? Iban, decimal MonthlyBasic, decimal Taxable, decimal TaxableBefore, decimal TaxBefore, decimal ProjectedAnnualTaxable,
    decimal ProjectedAnnualTax, string? CompanyName, string? StoreName, IReadOnlyList<PayslipLineInfo> Lines);
public sealed record CashSheetRow(string Code, string Name, string? Designation, decimal Net);
public sealed record CashSheet(RunRow Run, IReadOnlyList<CashSheetRow> Rows, decimal Total);
public sealed record HrReportInfo(string Key, string Group, string Title, IReadOnlyList<string> Parameters);

/// <summary>Payroll: salaries, loans and advances, payroll runs and their payslips, the bank and cash lists, and the HR reports.</summary>
public static partial class Endpoints
{
    private static void MapPayroll(RouteGroupBuilder api)
    {
        // Salaries.
        api.MapGet("/hr/employees/{id:guid}/salary", async (Guid id, ApiRequest api) =>
        {
            RequirePayroll(api);
            await VisibleEmployeeAsync(api, id);
            return Results.Ok((await new PayrollService(api.Root).GetSalaryHistoryAsync(id)).Select(ToInfo));
        }).RequireAuthorization(Need(Permissions.WebSignIn));
        api.MapPost("/hr/employees/{id:guid}/salary", async (Guid id, SalaryInfo body, ApiRequest api) =>
        {
            var employee = await VisibleEmployeeAsync(api, id);
            var saved = await new PayrollService(await api.ForChangeAsync(employee.StoreId)).SaveSalaryAsync(new SalaryStructure
            {
                EmployeeId = id, EffectiveFrom = body.EffectiveFrom, Basic = body.Basic, Note = body.Note,
                Components = [.. (body.Components ?? []).Select(c => new SalaryComponent { Kind = c.Kind, Code = c.Code ?? "", Name = c.Name ?? "", Amount = c.Amount, AccountCode = c.AccountCode })],
            }, api.Session);
            var info = ToInfo((await new PayrollService(api.Root).GetSalaryHistoryAsync(id)).Single(s => s.Id == saved.Id));
            return Results.Created($"/api/hr/salary/{saved.Id}", info);
        }).RequireAuthorization(Need(Permissions.PayrollRun));
        api.MapDelete("/hr/salary/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            Guid employeeId;
            await using (var db = api.Root.Create())
                employeeId = await db.SalaryStructures.AsNoTracking().Where(s => s.Id == id).Select(s => (Guid?)s.EmployeeId).SingleOrDefaultAsync() ?? throw ApiException.NotFound("That salary");
            var employee = await VisibleEmployeeAsync(api, employeeId, "That salary");
            await new PayrollService(await api.ForChangeAsync(employee.StoreId)).DeleteSalaryAsync(id, api.Session);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.PayrollRun));

        // Loans and advances.
        api.MapGet("/payroll/loans", async (ApiRequest api) =>
        {
            RequirePayroll(api);
            var store = api.ListStore();
            if (api.Id("employeeId") is Guid one) await VisibleEmployeeAsync(api, one);
            LoanStatus? status = Enum.TryParse<LoanStatus>(api.Text("status"), true, out var s) ? s : null;
            var loans = await new PayrollService(api.For(store)).GetLoansAsync(api.Id("employeeId"), status);
            return Results.Ok(await LoanRowsAsync(api, loans));
        }).RequireAuthorization(Need(Permissions.WebSignIn));
        api.MapGet("/payroll/loans/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            RequirePayroll(api);
            var loan = await new PayrollService(api.Root).GetLoanAsync(id) ?? throw ApiException.NotFound("That loan");
            await VisibleEmployeeAsync(api, loan.EmployeeId, "That loan");
            return Results.Ok((await LoanRowsAsync(api, [loan])).Single());
        }).RequireAuthorization(Need(Permissions.WebSignIn));
        api.MapPost("/payroll/loans", async (LoanBody body, ApiRequest api) =>
        {
            var employee = await VisibleEmployeeAsync(api, body.EmployeeId);
            var saved = await new PayrollService(await api.ForChangeAsync(employee.StoreId)).IssueLoanAsync(new EmployeeLoan
            {
                EmployeeId = body.EmployeeId, Kind = body.Kind, IssuedOn = body.IssuedOn, Amount = body.Amount, Instalment = body.Instalment,
                FirstMonth = body.FirstMonth ?? default, Reason = body.Reason,
            }, body.PaidFrom, api.Session);
            var row = (await LoanRowsAsync(api, [(await new PayrollService(api.Root).GetLoanAsync(saved.Id))!])).Single();
            return Results.Created($"/api/payroll/loans/{saved.Id}", row);
        }).RequireAuthorization(Need(Permissions.PayrollRun));
        api.MapPost("/payroll/loans/{id:guid}/cancel", async (Guid id, ReasonBody body, ApiRequest api) =>
        {
            var loan = await new PayrollService(api.Root).GetLoanAsync(id) ?? throw ApiException.NotFound("That loan");
            var employee = await VisibleEmployeeAsync(api, loan.EmployeeId, "That loan");
            await new PayrollService(await api.ForChangeAsync(employee.StoreId)).CancelLoanAsync(id, body?.Reason, api.Session);
            return Results.Ok((await LoanRowsAsync(api, [(await new PayrollService(api.Root).GetLoanAsync(id))!])).Single());
        }).RequireAuthorization(Need(Permissions.PayrollRun));

        // Payroll runs.
        api.MapGet("/payroll/runs", async (ApiRequest api) =>
        {
            RequirePayroll(api);
            var store = api.ListStore();
            var runs = await new PayrollService(api.For(store)).GetRunsAsync(api.Number("year"));
            return Results.Ok(await RunRowsAsync(api, runs));
        }).RequireAuthorization(Need(Permissions.WebSignIn));
        api.MapPost("/payroll/runs", async (RunBody body, ApiRequest api) =>
        {
            if (body.StoreId is Guid store && !api.User.CanSee(store)) throw ApiException.Forbidden("You may not see that store.");
            if (body.StoreId is null && !api.User.AllStores) throw ApiException.Forbidden("A payroll of the whole company is run by a user who sees every store.");
            var run = await new PayrollService(await api.ForChangeAsync(body.StoreId)).CreateRunAsync(body.Year, body.Month, body.StoreId, body.Notes, api.Session);
            return Results.Created($"/api/payroll/runs/{run.Id}", (await RunRowsAsync(api, [run])).Single());
        }).RequireAuthorization(Need(Permissions.PayrollRun));
        api.MapPost("/payroll/settlements", async (SettlementBody body, ApiRequest api) =>
        {
            var employee = await VisibleEmployeeAsync(api, body.EmployeeId);
            var run = await new PayrollService(await api.ForChangeAsync(employee.StoreId)).CreateSettlementAsync(body.EmployeeId, body.Notes, api.Session);
            return Results.Created($"/api/payroll/runs/{run.Id}", (await RunRowsAsync(api, [run])).Single());
        }).RequireAuthorization(Need(Permissions.PayrollRun));
        api.MapGet("/payroll/runs/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            RequirePayroll(api);
            var run = await VisibleRunAsync(api, id);
            var slips = await new PayrollService(api.Root).GetPayslipsAsync(id);
            var codes = await StoreCodesAsync(api);
            return Results.Ok(new RunDetail((await RunRowsAsync(api, [run])).Single(), [.. slips.Select(s => ToRow(s, codes))]));
        }).RequireAuthorization(Need(Permissions.WebSignIn));
        api.MapDelete("/payroll/runs/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            var run = await VisibleRunAsync(api, id);
            await new PayrollService(await api.ForChangeAsync(run.StoreId)).DeleteRunAsync(id, api.Session);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.PayrollRun));
        api.MapPost("/payroll/runs/{id:guid}/calculate", (Guid id, RunActionBody body, ApiRequest api) =>
            RunActionAsync(id, body.Version, api, (payroll, session) => payroll.CalculateAsync(id, session))).RequireAuthorization(Need(Permissions.PayrollRun));
        api.MapPost("/payroll/runs/{id:guid}/approve", (Guid id, RunActionBody body, ApiRequest api) =>
            RunActionAsync(id, body.Version, api, (payroll, session) => payroll.ApproveAsync(id, session))).RequireAuthorization(Need(Permissions.PayrollApprove));
        api.MapPost("/payroll/runs/{id:guid}/reopen", (Guid id, RunActionBody body, ApiRequest api) =>
            RunActionAsync(id, body.Version, api, (payroll, session) => payroll.ReopenAsync(id, session))).RequireAuthorization(Need(Permissions.PayrollApprove));
        api.MapPost("/payroll/runs/{id:guid}/post", (Guid id, RunActionBody body, ApiRequest api) =>
            RunActionAsync(id, body.Version, api, (payroll, session) => payroll.PostAsync(id, session))).RequireAuthorization(Need(Permissions.PayrollRun));
        api.MapPost("/payroll/runs/{id:guid}/pay", async (Guid id, PayBody body, ApiRequest api) =>
        {
            var today = await api.TodayAsync(null);
            return await RunActionAsync(id, body.Version, api, (payroll, session) => payroll.PayAsync(id, body.PaidOn ?? today, body.BankAccount, body.CashAccount, session));
        }).RequireAuthorization(Need(Permissions.PayrollRun));
        api.MapGet("/payroll/runs/{id:guid}/bank-transfers", async (Guid id, ApiRequest api) =>
        {
            RequirePayroll(api);
            var run = await VisibleRunAsync(api, id);
            var csv = await new PayrollService(api.Root).BankTransfersCsvAsync(id);
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv; charset=utf-8", $"bank-transfers-PR{run.Number}.csv");
        }).RequireAuthorization(Need(Permissions.WebSignIn));
        api.MapGet("/payroll/runs/{id:guid}/cash-sheet", async (Guid id, ApiRequest api) =>
        {
            RequirePayroll(api);
            var run = await VisibleRunAsync(api, id);
            var rows = (await new PayrollService(api.Root).GetPayslipsAsync(id)).Where(s => s.PaymentMethod == SalaryPaymentMethod.Cash && s.Net > 0)
                .Select(s => new CashSheetRow(s.EmployeeCode, s.EmployeeName, s.Designation, s.Net)).ToList();
            return Results.Ok(new CashSheet((await RunRowsAsync(api, [run])).Single(), rows, rows.Sum(r => r.Net)));
        }).RequireAuthorization(Need(Permissions.WebSignIn));
        api.MapGet("/payroll/payslips/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            RequirePayroll(api);
            var slip = await new PayrollService(api.Root).GetPayslipAsync(id) ?? throw ApiException.NotFound("That payslip");
            var run = await VisibleRunAsync(api, slip.PayrollRunId, "That payslip");
            var codes = await StoreCodesAsync(api);
            string? company = null, storeName = null;
            await using (var db = api.Root.Create())
            {
                if (slip.StoreId is Guid s)
                {
                    company = await db.StoreSettings.AsNoTracking().Where(x => x.StoreId == s).Select(x => x.CompanyName).FirstOrDefaultAsync();
                    storeName = await db.Stores.AsNoTracking().Where(x => x.Id == s).Select(x => x.Name).FirstOrDefaultAsync();
                }
                company ??= await db.StoreSettings.AsNoTracking().OrderBy(x => x.CreatedAtUtc).Select(x => x.CompanyName).FirstOrDefaultAsync();
            }
            return Results.Ok(new PayslipDetail(ToRow(slip, codes), run.Id, run.Number, PayrollService.Title(run), run.Status, run.PeriodStart, run.PeriodEnd, slip.Cnic,
                slip.BankName, slip.Iban, slip.Basic, slip.Taxable, slip.TaxableBefore, slip.TaxBefore, slip.ProjectedAnnualTaxable, slip.ProjectedAnnualTax, company, storeName,
                [.. slip.Lines.Select(l => new PayslipLineInfo(l.Kind, l.Code, l.Name, l.Amount, l.Quantity, l.Rate))]));
        }).RequireAuthorization(Need(Permissions.WebSignIn));

        // Reports.
        api.MapGet("/hr/reports", (ApiRequest api) => Results.Ok(HrReports.Where(r => !r.Payroll || MayReadPayroll(api))
            .Select(r => new HrReportInfo(r.Key, r.Group, r.Title, r.Parameters)))).RequireAuthorization(Need(Permissions.HrView));
        api.MapGet("/hr/reports/{key}", async (string key, ApiRequest api) =>
        {
            var (table, from, to) = await RunHrReportAsync(key, api);
            return Results.Ok(new ReportResult(table.Title, table.Subtitle, [.. table.Columns.Select(c => new ReportColumnInfo(c.Header, c.Kind, c.Total))], table.Rows,
                table.HasTotals ? table.TotalsRow() : new object?[table.Columns.Count], from, to));
        }).RequireAuthorization(Need(Permissions.HrView));
        api.MapGet("/hr/reports/{key}/csv", async (string key, ApiRequest api) =>
        {
            var (table, from, to) = await RunHrReportAsync(key, api);
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(table.ToCsv())).ToArray(), "text/csv; charset=utf-8",
                $"{key}-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
        }).RequireAuthorization(Need(Permissions.HrView));
    }

    // Who may read payroll ----------------------------------------------------------------------------------------------------

    private static bool MayReadPayroll(ApiRequest api) => api.User.Can(Permissions.PayrollRun) || api.User.Can(Permissions.PayrollApprove);

    /// <summary>Salaries, loans, runs and payslips are read by those who run or approve payroll.</summary>
    private static void RequirePayroll(ApiRequest api)
    {
        if (!MayReadPayroll(api)) throw ApiException.Forbidden("Your role cannot see payroll.");
    }

    private static async Task<PayrollRun> VisibleRunAsync(ApiRequest api, Guid id, string what = "That payroll run")
    {
        var run = await new PayrollService(api.Root).GetRunAsync(id);
        if (run is null || !Sees(api, run.StoreId)) throw ApiException.NotFound(what);
        return run;
    }

    /// <summary>Runs an action on a run after checking it is visible and that the version sent back is its current one (409 otherwise).</summary>
    private static async Task<IResult> RunActionAsync(Guid id, string? version, ApiRequest api, Func<PayrollService, SessionInfo, Task<PayrollRun>> action)
    {
        var run = await VisibleRunAsync(api, id);
        ApiRequest.CheckVersion(version, ApiRequest.Version(run.Version));
        var saved = await action(new PayrollService(await api.ForChangeAsync(run.StoreId)), api.Session);
        return Results.Ok((await RunRowsAsync(api, [saved])).Single());
    }

    private static async Task<Dictionary<Guid, string>> StoreCodesAsync(ApiRequest api)
    {
        await using var db = api.Root.Create();
        return await db.Stores.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Code);
    }

    // Shapes ---------------------------------------------------------------------------------------------------------------------

    private static SalaryInfo ToInfo(SalaryStructure s) => new(s.Id, s.EffectiveFrom, s.Basic, s.Note, s.ChangedBy,
        [.. s.Components.OrderBy(c => c.LineNumber).Select(c => new SalaryComponentInfo(c.Kind, c.Code, c.Name, c.Amount, c.AccountCode))],
        s.Basic + s.Components.Where(c => c.Kind == SalaryComponentKind.Allowance).Sum(c => c.Amount),
        s.Components.Where(c => c.Kind == SalaryComponentKind.Deduction).Sum(c => c.Amount));

    private static async Task<List<LoanRow>> LoanRowsAsync(ApiRequest api, List<EmployeeLoan> loans)
    {
        await using var db = api.Root.Create();
        var ids = loans.Select(l => l.EmployeeId).Distinct().ToList();
        var employees = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id);
        return [.. loans.Where(l => employees.TryGetValue(l.EmployeeId, out var e) && Sees(api, e.StoreId)).Select(l =>
        {
            var e = employees[l.EmployeeId];
            decimal recovered = l.Instalments.Sum(i => i.Recovered);
            return new LoanRow(l.Id, l.Number, l.EmployeeId, e.Code, e.FullName, l.Kind, l.IssuedOn, l.Amount, l.Instalment, l.FirstMonth, l.Status, recovered,
                l.Status == LoanStatus.Cancelled ? 0 : l.Amount - recovered, l.Reason, l.PaidFromAccount,
                [.. l.Instalments.OrderBy(i => i.Number).Select(i => new LoanInstalmentInfo(i.Number, i.Month, i.Amount, i.Recovered))]);
        })];
    }

    private static async Task<List<RunRow>> RunRowsAsync(ApiRequest api, List<PayrollRun> runs)
    {
        await using var db = api.Root.Create();
        var codes = await db.Stores.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Code);
        var employeeIds = runs.Where(r => r.EmployeeId is not null).Select(r => r.EmployeeId!.Value).ToList();
        var names = await db.Employees.AsNoTracking().Where(e => employeeIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName);
        var entryIds = runs.SelectMany(r => new[] { r.JournalEntryId, r.PaymentEntryId }).Where(i => i is not null).Select(i => i!.Value).ToList();
        var numbers = await db.JournalEntries.AsNoTracking().Where(j => entryIds.Contains(j.Id)).ToDictionaryAsync(j => j.Id, j => j.Number);
        return [.. runs.Where(r => Sees(api, r.StoreId)).Select(r => new RunRow(r.Id, ApiRequest.Version(r.Version), r.Number, r.Kind, r.Year, r.Month,
            r.Kind == PayrollRunKind.FinalSettlement && r.EmployeeId is Guid who ? $"Final settlement of {names.GetValueOrDefault(who)}" : PayrollService.MonthName(r.Year, r.Month),
            r.PeriodStart, r.PeriodEnd, r.StoreId, r.StoreId is Guid s ? codes.GetValueOrDefault(s) : null, r.EmployeeId,
            r.EmployeeId is Guid e ? names.GetValueOrDefault(e) : null, r.Status, r.Employees, r.Gross, r.Deductions, r.Net, r.Tax, r.EmployerContributions, r.Notes,
            r.Warnings is { } w ? w.Split('\n', StringSplitOptions.RemoveEmptyEntries) : [], r.CreatedBy, r.CalculatedBy, ApiRequest.Utc(r.CalculatedAtUtc),
            r.ApprovedBy, ApiRequest.Utc(r.ApprovedAtUtc), r.PostedBy, ApiRequest.Utc(r.PostedAtUtc),
            r.JournalEntryId is Guid j && numbers.TryGetValue(j, out var jn) ? jn : null, r.PaidOn, r.PaidBy,
            r.PaymentEntryId is Guid p && numbers.TryGetValue(p, out var pn) ? pn : null))];
    }

    private static readonly HashSet<string> OwnEarnings = [PayslipCodes.Basic, PayslipCodes.Overtime, PayslipCodes.Commission, PayslipCodes.LeaveEncashment, PayslipCodes.Gratuity];

    private static PayslipRow ToRow(Payslip s, Dictionary<Guid, string> codes)
    {
        decimal Sum(Func<PayslipLine, bool> which) => s.Lines.Where(which).Sum(l => l.Amount);
        var earnings = s.Lines.Where(l => l.Kind == PayslipLineKind.Earning).ToList();
        var deductions = s.Lines.Where(l => l.Kind == PayslipLineKind.Deduction).ToList();
        return new PayslipRow(s.Id, s.EmployeeId, s.EmployeeCode, s.EmployeeName, s.Designation, s.Department, s.StoreId is Guid st ? codes.GetValueOrDefault(st) : null,
            s.PaymentMethod, s.DaysEmployed, s.DaysPresent, s.PaidLeaveDays, s.UnpaidLeaveDays, s.AbsentDays, s.LateDays, s.OvertimeHours,
            earnings.Where(l => l.Code == PayslipCodes.Basic).Sum(l => l.Amount), earnings.Where(l => !OwnEarnings.Contains(l.Code)).Sum(l => l.Amount),
            Sum(l => l.Kind == PayslipLineKind.Earning && l.Code == PayslipCodes.Overtime), Sum(l => l.Kind == PayslipLineKind.Earning && l.Code == PayslipCodes.Commission),
            earnings.Where(l => l.Code is PayslipCodes.LeaveEncashment or PayslipCodes.Gratuity).Sum(l => l.Amount), s.Gross,
            deductions.Where(l => PayslipCodes.IsAttendance(l.Code)).Sum(l => l.Amount), s.Tax, deductions.Where(l => l.Code == PayslipCodes.Eobi).Sum(l => l.Amount),
            deductions.Where(l => l.Code == PayslipCodes.Loan).Sum(l => l.Amount),
            deductions.Where(l => !PayslipCodes.IsAttendance(l.Code) && l.Code is not (PayslipCodes.Tax or PayslipCodes.Eobi or PayslipCodes.Loan)).Sum(l => l.Amount),
            s.Deductions, s.Net, s.EmployerContributions);
    }

    // HR reports -------------------------------------------------------------------------------------------------------------------

    private sealed record HrReport(string Key, string Group, string Title, string[] Parameters, bool Payroll);

    /// <summary>The HR reports; parameters are <c>runId</c>, <c>employeeId</c> with <c>taxYear</c>, <c>month</c> (yyyy-MM) or <c>from</c>/<c>to</c>.</summary>
    private static readonly HrReport[] HrReports =
    [
        new("salary-sheet", "Payroll", "Salary sheet", ["run"], true),
        new("tax-certificate", "Payroll", "Tax deducted (certificate)", ["employee", "taxYear"], true),
        new("eobi", "Payroll", "EOBI return", ["month"], true),
        new("social-security", "Payroll", "Social security return", ["month"], true),
        new("attendance", "Attendance and leave", "Attendance", ["month"], false),
        new("leave", "Attendance and leave", "Leave taken", ["dates"], false),
        new("overtime", "Attendance and leave", "Overtime", ["dates"], false),
        new("headcount", "People", "Headcount and turnover", ["dates"], false),
    ];

    private static async Task<(ReportTable Table, DateOnly From, DateOnly To)> RunHrReportAsync(string key, ApiRequest api)
    {
        var report = HrReports.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase)) ?? throw ApiException.NotFound("That report");
        if (report.Payroll) RequirePayroll(api);
        var store = api.ListStore();
        switch (report.Key)
        {
            case "salary-sheet":
            {
                var run = await VisibleRunAsync(api, api.Id("runId") ?? throw ApiException.Bad("Choose the payroll run."));
                var slips = await new PayrollService(api.Root).GetPayslipsAsync(run.Id);
                var codes = await StoreCodesAsync(api);
                var table = new ReportTable
                {
                    Title = $"Salary sheet, payroll #{run.Number}", Subtitle = $"{PayrollService.Title(run)}{(run.StoreId is Guid s ? $", store {codes.GetValueOrDefault(s)}" : "")}, {run.Status}",
                    Columns =
                    [
                        new("Code"), new("Employee"), new("Designation"), new("Days paid", ColumnKind.Quantity), new("Basic", ColumnKind.Money, true),
                        new("Allowances", ColumnKind.Money, true), new("Overtime", ColumnKind.Money, true), new("Commission", ColumnKind.Money, true),
                        new("Other earnings", ColumnKind.Money, true), new("Gross", ColumnKind.Money, true), new("Leave and lateness", ColumnKind.Money, true),
                        new("Income tax", ColumnKind.Money, true), new("EOBI", ColumnKind.Money, true), new("Loans", ColumnKind.Money, true),
                        new("Other deductions", ColumnKind.Money, true), new("Net pay", ColumnKind.Money, true), new("Employer", ColumnKind.Money, true),
                    ],
                };
                foreach (var r in slips.Select(s => ToRow(s, codes)))
                    table.Rows.Add([r.Code, r.Name, r.Designation, r.DaysPresent + r.PaidLeaveDays, r.Basic, r.Allowances, r.Overtime, r.Commission, r.OtherEarnings,
                        r.Gross, r.Attendance, r.Tax, r.Eobi, r.Loans, r.OtherDeductions, r.Net, r.EmployerContributions]);
                return (table, run.PeriodStart, run.PeriodEnd);
            }
            case "tax-certificate":
            {
                var employee = await VisibleEmployeeAsync(api, api.Id("employeeId") ?? throw ApiException.Bad("Choose the employee."));
                var country = await new HrService(api.Root).CountryOfStoreAsync(employee.StoreId);
                int startYear = api.Number("taxYear") ?? (await api.TodayAsync(store)).Year;
                await using var db = api.Root.Create();
                var rules = await db.PayrollRuleSets.AsNoTracking().Where(r => r.Country == country).OrderByDescending(r => r.EffectiveFrom).FirstOrDefaultAsync();
                var start = new DateOnly(startYear, rules?.TaxYearStartMonth ?? 7, 1);
                var end = start.AddYears(1).AddDays(-1);
                var rows = await (from p in db.Payslips.AsNoTracking()
                                  join r in db.PayrollRuns.AsNoTracking() on p.PayrollRunId equals r.Id
                                  where p.EmployeeId == employee.Id && r.Status != PayrollRunStatus.Draft && r.PeriodEnd >= start && r.PeriodEnd <= end
                                  orderby r.PeriodEnd
                                  select new { r.Number, r.Year, r.Month, r.Kind, r.Status, p.Taxable, p.Tax }).ToListAsync();
                var table = new ReportTable
                {
                    Title = $"Tax deducted, tax year {PayrollRules.TaxYearName(start)}",
                    Subtitle = $"{employee.Code} {employee.FullName}{(employee.Cnic is { } cnic ? $", CNIC {cnic}" : "")}{(employee.Ntn is { } ntn ? $", NTN {ntn}" : "")}",
                    Columns = [new("Month"), new("Payroll run"), new("Status"), new("Taxable income", ColumnKind.Money, true), new("Tax deducted", ColumnKind.Money, true)],
                };
                if (employee.PriorTaxYear == startYear && (employee.PriorTaxableIncome > 0 || employee.PriorTaxDeducted > 0))
                    table.Rows.Add(["Previous employer", "", "", employee.PriorTaxableIncome, employee.PriorTaxDeducted]);
                foreach (var r in rows)
                    table.Rows.Add([PayrollService.MonthName(r.Year, r.Month) + (r.Kind == PayrollRunKind.FinalSettlement ? " (final settlement)" : ""), $"#{r.Number}", r.Status.ToString(), r.Taxable, r.Tax]);
                return (table, start, end);
            }
            case "eobi":
            case "social-security":
            {
                var (from, to) = MonthOf(api, await api.TodayAsync(store));
                bool eobi = report.Key == "eobi";
                int year = from.Year, month = from.Month;
                await using var db = api.Root.Create();
                var runs = db.PayrollRuns.AsNoTracking().Where(r => r.Year == year && r.Month == month && r.Status != PayrollRunStatus.Draft).Select(r => r.Id);
                var q = db.Payslips.AsNoTracking().Include(p => p.Lines).Where(p => runs.Contains(p.PayrollRunId));
                if (store is Guid only) q = q.Where(p => p.StoreId == only);
                var slips = (await q.ToListAsync()).Where(p => Sees(api, p.StoreId)).OrderBy(p => p.EmployeeName).ToList();
                var ids = slips.Select(s => s.EmployeeId).Distinct().ToList();
                var numbers = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id))
                    .ToDictionaryAsync(e => e.Id, e => eobi ? e.EobiNumber : e.SocialSecurityNumber);
                var table = new ReportTable
                {
                    Title = eobi ? "EOBI return" : "Social security return", Subtitle = PayrollService.MonthName(from.Year, from.Month),
                    Columns = eobi
                        ? [new("Code"), new("Employee"), new("CNIC"), new("EOBI number"), new("Wage", ColumnKind.Money, true), new("Employee share", ColumnKind.Money, true),
                           new("Employer share", ColumnKind.Money, true), new("Total", ColumnKind.Money, true)]
                        : [new("Code"), new("Employee"), new("CNIC"), new("Social security number"), new("Wage", ColumnKind.Money, true), new("Contribution", ColumnKind.Money, true)],
                };
                foreach (var s in slips)
                {
                    decimal wage = s.Gross - s.Lines.Where(l => PayslipCodes.IsAttendance(l.Code)).Sum(l => l.Amount);
                    if (eobi)
                    {
                        decimal mine = s.Lines.Where(l => l.Code == PayslipCodes.Eobi).Sum(l => l.Amount), theirs = s.Lines.Where(l => l.Code == PayslipCodes.EobiEmployer).Sum(l => l.Amount);
                        if (mine + theirs == 0) continue;
                        table.Rows.Add([s.EmployeeCode, s.EmployeeName, s.Cnic, numbers.GetValueOrDefault(s.EmployeeId), wage, mine, theirs, mine + theirs]);
                    }
                    else
                    {
                        decimal paid = s.Lines.Where(l => l.Code == PayslipCodes.SocialSecurity).Sum(l => l.Amount);
                        if (paid == 0) continue;
                        table.Rows.Add([s.EmployeeCode, s.EmployeeName, s.Cnic, numbers.GetValueOrDefault(s.EmployeeId), wage, paid]);
                    }
                }
                return (table, from, to);
            }
            case "attendance":
            {
                var (from, to) = MonthOf(api, await api.TodayAsync(store));
                var employees = await ListEmployeesAsync(api, store, from, to);
                var days = (await new AttendanceService(api.For(store)).DaysAsync(from, to, [.. employees.Select(e => e.Id)], await api.TodayAsync(store))).ToLookup(d => d.EmployeeId);
                var table = new ReportTable
                {
                    Title = "Attendance", Subtitle = PayrollService.MonthName(from.Year, from.Month),
                    Columns =
                    [
                        new("Code"), new("Employee"), new("Present", ColumnKind.Count, true), new("Late", ColumnKind.Count, true), new("Left early", ColumnKind.Count, true),
                        new("Absent", ColumnKind.Count, true), new("Leave days", ColumnKind.Quantity, true), new("Holidays", ColumnKind.Count, true),
                        new("Days off", ColumnKind.Count, true), new("Overtime hours", ColumnKind.Quantity, true),
                    ],
                };
                foreach (var e in employees)
                {
                    var own = days[e.Id].ToList();
                    table.Rows.Add([e.Code, e.FullName, own.Count(d => d.Status == AttendanceStatus.Present), own.Count(d => d.LateMinutes > 0), own.Count(d => d.EarlyMinutes > 0),
                        own.Count(d => d.Status == AttendanceStatus.Absent), own.Sum(d => d.LeaveDays), own.Count(d => d.Status == AttendanceStatus.Holiday),
                        own.Count(d => d.Status == AttendanceStatus.Off), Math.Round(own.Sum(d => d.OvertimeMinutes) / 60m, 2)]);
                }
                return (table, from, to);
            }
            case "overtime":
            {
                var (from, to) = await api.PeriodAsync(store);
                if (to.DayNumber - from.DayNumber > 92) throw ApiException.Bad("Overtime is reported for at most 93 days at a time.");
                var employees = await ListEmployeesAsync(api, store, from, to);
                var days = (await new AttendanceService(api.For(store)).DaysAsync(from, to, [.. employees.Select(e => e.Id)], await api.TodayAsync(store))).ToLookup(d => d.EmployeeId);
                var table = new ReportTable
                {
                    Title = "Overtime", Subtitle = $"{from.ToString("d", CultureInfo.CurrentCulture)} to {to.ToString("d", CultureInfo.CurrentCulture)}",
                    Columns = [new("Code"), new("Employee"), new("Overtime allowed"), new("Days", ColumnKind.Count, true), new("Hours", ColumnKind.Quantity, true)],
                };
                foreach (var e in employees)
                {
                    var over = days[e.Id].Where(d => d.OvertimeMinutes > 0).ToList();
                    if (over.Count == 0) continue;
                    table.Rows.Add([e.Code, e.FullName, e.OvertimeEligible ? "Yes" : "No", over.Count, Math.Round(over.Sum(d => d.OvertimeMinutes) / 60m, 2)]);
                }
                return (table, from, to);
            }
            case "leave":
            {
                var (from, to) = await api.PeriodAsync(store);
                await using var db = api.Root.Create();
                var scoped = ScopedEmployees(db, api, store);
                var requests = await db.LeaveRequests.AsNoTracking()
                    .Where(r => r.FromDate <= to && r.ToDate >= from && (r.Status == LeaveRequestStatus.Approved || r.Status == LeaveRequestStatus.Pending)
                        && scoped.Any(e => e.Id == r.EmployeeId))
                    .OrderBy(r => r.FromDate).ToListAsync();
                var ids = requests.Select(r => r.EmployeeId).Distinct().ToList();
                var employees = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id);
                var types = await db.LeaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id);
                var table = new ReportTable
                {
                    Title = "Leave taken", Subtitle = $"{from.ToString("d", CultureInfo.CurrentCulture)} to {to.ToString("d", CultureInfo.CurrentCulture)}",
                    Columns = [new("Code"), new("Employee"), new("Leave"), new("Paid"), new("From", ColumnKind.Date), new("To", ColumnKind.Date), new("Days", ColumnKind.Quantity, true), new("Status")],
                };
                foreach (var r in requests.OrderBy(r => employees[r.EmployeeId].FullName).ThenBy(r => r.FromDate))
                {
                    var type = types.GetValueOrDefault(r.LeaveTypeId);
                    table.Rows.Add([employees[r.EmployeeId].Code, employees[r.EmployeeId].FullName, type?.Name, type?.IsPaid == true ? "Yes" : "No", r.FromDate, r.ToDate, r.Days, r.Status.ToString()]);
                }
                return (table, from, to);
            }
            default:
            {
                var (from, to) = await api.PeriodAsync(store);
                await using var db = api.Root.Create();
                var employees = await ScopedEmployees(db, api, store).Where(e => e.JoiningDate <= to).ToListAsync();
                var departments = await db.HrDepartments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name);
                var table = new ReportTable
                {
                    Title = "Headcount and turnover", Subtitle = $"{from.ToString("d", CultureInfo.CurrentCulture)} to {to.ToString("d", CultureInfo.CurrentCulture)}",
                    Columns =
                    [
                        new("Department"), new("At the start", ColumnKind.Count, true), new("Joined", ColumnKind.Count, true), new("Left", ColumnKind.Count, true),
                        new("At the end", ColumnKind.Count, true), new("Turnover", ColumnKind.Percent),
                    ],
                };
                foreach (var group in employees.GroupBy(e => e.HrDepartmentId is Guid d ? departments.GetValueOrDefault(d) ?? "" : "(no department)").OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
                {
                    int atStart = group.Count(e => e.IsEmployedOn(from)), atEnd = group.Count(e => e.IsEmployedOn(to));
                    int joined = group.Count(e => e.JoiningDate >= from && e.JoiningDate <= to), left = group.Count(e => e.LeavingDate is { } l && l >= from && l <= to);
                    decimal average = (atStart + atEnd) / 2m;
                    table.Rows.Add([group.Key, atStart, joined, left, atEnd, average == 0 ? 0m : Math.Round(left / average * 100m, 1)]);
                }
                return (table, from, to);
            }
        }
    }

    /// <summary>The month asked for (<c>month</c>, yyyy-MM), else this month.</summary>
    private static (DateOnly From, DateOnly To) MonthOf(ApiRequest api, DateOnly today)
    {
        var month = api.Text("month") is { } text
            ? DateOnly.TryParseExact(text + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m) ? m : throw ApiException.Bad($"month must be like 2026-11, not {text}.")
            : new DateOnly(today.Year, today.Month, 1);
        return (month, month.AddMonths(1).AddDays(-1));
    }
}
