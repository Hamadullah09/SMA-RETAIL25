using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Services;

namespace RetailPlus.Server;

public sealed record HrListItem(Guid? Id, string? Version, string Name, bool IsActive, int Employees = 0);
public sealed record EmployeeRow(Guid Id, string Code, string FullName, string? Designation, string? Department, Guid? StoreId, string? StoreCode,
    EmploymentType EmploymentType, DateOnly JoiningDate, DateOnly? LeavingDate, bool Active, string? Phone, string? UserName);
public sealed record EmployeeDetail(Guid? Id, string? Version, string? Code, string FullName, string? FatherOrHusbandName, string? Cnic, DateOnly? DateOfBirth,
    Gender? Gender, MaritalStatus? MaritalStatus, string? Address, string? City, string? Phone, string? Email, string? EmergencyContactName,
    string? EmergencyContactPhone, string? EmergencyContactRelation, DateOnly JoiningDate, DateOnly? ConfirmationDate, DateOnly? LeavingDate, string? LeavingReason,
    EmploymentType EmploymentType, Guid? DepartmentId, Guid? DesignationId, Guid? StoreId, Guid? ManagerId, Guid? UserId, Guid? ShiftId,
    bool TracksAttendance, bool OvertimeEligible, bool EobiApplies, bool SocialSecurityApplies, SalaryPaymentMethod PaymentMethod, string? BankName,
    string? Iban, string? Ntn, string? EobiNumber, string? SocialSecurityNumber, int? PriorTaxYear, decimal PriorTaxableIncome, decimal PriorTaxDeducted,
    string? Notes, string? ManagerName = null, string? UserName = null, string? Country = null);
public sealed record EmployeeDocumentRow(Guid Id, Guid EmployeeId, string Title, string FileName, string ContentType, long Size, DateTime UploadedAtUtc, string? UploadedBy);
public sealed record HrStoreCountry(Guid StoreId, string Code, string Name, string Country, bool Own);
public sealed record HrSettingsInfo(string CompanyCountry, IReadOnlyList<string> Countries, IReadOnlyList<HrStoreCountry> Stores);
public sealed record CountryRequest(string? Country);
public sealed record TaxSlabInfo(decimal IncomeFrom, decimal? IncomeTo, decimal FixedTax, decimal Rate);
public sealed record AllowanceRuleInfo(string Code, string Name, bool IsTaxable, decimal? ExemptPercentOfBasic, int SortOrder);
public sealed record RuleSetInfo(Guid? Id, string? Version, string Country, string Name, DateOnly EffectiveFrom, DateOnly? EffectiveTo, int TaxYearStartMonth,
    decimal MinimumWage, decimal EobiEmployerRate, decimal EobiEmployeeRate, string? SocialSecurityProvince, decimal SocialSecurityRate, decimal SocialSecurityWageCeiling,
    SocialSecurityBasis SocialSecurityBasis, int WorkingDaysPerWeek, decimal HoursPerWeek, decimal OvertimeMultiplier, int OvertimeMinimumMinutes,
    DailyRateBasis DailyRateBasis, LatenessRule LatenessRule, int LatesPerDay, bool DeductAbsences, decimal SurchargeThreshold, decimal SurchargeRate,
    bool GratuityEnabled, decimal GratuityDaysPerYear, int GratuityMinimumMonths, int GratuityPartYearMonths, bool GratuityOnBasic,
    string? CnicPattern, string? CnicExample, string? IbanPattern, string? IbanExample, string? Notes,
    IReadOnlyList<TaxSlabInfo> TaxSlabs, IReadOnlyList<AllowanceRuleInfo> Allowances);
public sealed record LeaveTypeInfo(Guid? Id, string? Version, string Country, string Code, string Name, LeaveKind Kind, decimal DaysPerYear, bool IsPaid,
    bool AccruesMonthly, decimal CarryOverMaxDays, bool Encashable, bool CountsCalendarDays, Gender? OnlyFor, int MinimumServiceMonths, bool IsActive, int SortOrder);
public sealed record HolidayInfo(Guid? Id, string? Version, string Country, DateOnly Date, string Name);
public sealed record ShiftInfo(Guid? Id, string? Version, string Name, string StartTime, string EndTime, int BreakMinutes, int GraceMinutes,
    IReadOnlyList<string> Days, Guid? StoreId, bool IsActive);
public sealed record RosterChangeInfo(Guid? Id, Guid EmployeeId, DateOnly Date, Guid? ShiftId, string? Note);
public sealed record RosterRowInfo(Guid EmployeeId, string Code, string Name, Guid? StoreId, Guid? UsualShiftId, Guid? WeekShiftId, IReadOnlyList<RosterChangeInfo> Changes);
public sealed record RosterInfo(DateOnly WeekStart, IReadOnlyList<DateOnly> Days, IReadOnlyList<RosterRowInfo> Rows);
public sealed record RosterAssignment(Guid EmployeeId, Guid? ShiftId);
public sealed record RosterRequest(DateOnly WeekStart, IReadOnlyList<RosterAssignment> Assignments);
public sealed record AttendanceRow(Guid EmployeeId, string Code, string Name, DateOnly Date, AttendanceStatus Status, string? Shift, DateTime? ShiftStart, DateTime? ShiftEnd,
    DateTime? FirstIn, DateTime? LastOut, int WorkedMinutes, int LateMinutes, int EarlyMinutes, int OvertimeMinutes, string? Holiday, string? LeaveCode,
    decimal LeaveDays, bool Corrected);
public sealed record AttendanceSheetRow(Guid EmployeeId, string Code, string Name, IReadOnlyList<string> Marks, int Present, int Late, int LeftEarly, int Absent,
    decimal Leave, int Holidays, int Off, decimal OvertimeHours);
public sealed record AttendanceSheet(string Month, IReadOnlyList<DateOnly> Days, IReadOnlyList<AttendanceSheetRow> Rows);
public sealed record CorrectionRow(Guid Id, Guid EmployeeId, string Code, string Name, DateOnly Date, AttendanceStatus Status, int WorkedMinutes, int LateMinutes,
    int EarlyMinutes, int OvertimeMinutes, string Reason, HrApproval Approval, string? RequestedBy, DateTime RequestedAtUtc, string? DecidedBy, DateTime? DecidedAtUtc,
    string? DecisionNote);
public sealed record CorrectionRequest(Guid EmployeeId, DateOnly Date, AttendanceStatus Status, int WorkedMinutes, int LateMinutes, int EarlyMinutes, int OvertimeMinutes, string? Reason);
public sealed record DecisionRequest(string? Note);
public sealed record LeaveRequestRow(Guid Id, Guid EmployeeId, string Code, string Name, Guid LeaveTypeId, string LeaveType, DateOnly FromDate, DateOnly ToDate,
    bool FirstDayHalf, bool LastDayHalf, decimal Days, string? Reason, LeaveRequestStatus Status, string? RequestedBy, DateTime RequestedAtUtc, string? DecidedBy,
    DateTime? DecidedAtUtc, string? DecisionNote, bool MayDecide);
public sealed record LeaveRequestBody(Guid EmployeeId, Guid LeaveTypeId, DateOnly FromDate, DateOnly ToDate, bool FirstDayHalf, bool LastDayHalf, string? Reason);
public sealed record LeaveBalanceRow(Guid EmployeeId, string Code, string Name, Guid LeaveTypeId, string LeaveCode, string LeaveType, bool Paid, bool Limited,
    decimal Entitled, decimal Earned, decimal CarriedIn, decimal Adjusted, decimal Taken, decimal Pending, decimal? Available);
public sealed record LeaveAdjustmentBody(Guid EmployeeId, Guid LeaveTypeId, int Year, decimal Days, string? Reason);

/// <summary>Human resources: HR lists and settings (rule sets, leave types, holidays), employees and their documents, shifts and the roster, attendance and leave.</summary>
public static partial class Endpoints
{
    private static void MapHr(RouteGroupBuilder api)
    {
        // Lists and settings shared by every store.
        api.MapGet("/hr/departments", async (ApiRequest api) => Results.Ok(await DepartmentsAsync(api))).RequireAuthorization(Need(Permissions.HrView));
        api.MapPost("/hr/departments", (HrListItem body, ApiRequest api) => SaveDepartmentAsync(null, body, api)).RequireAuthorization(Need(Permissions.HrEdit));
        api.MapPut("/hr/departments/{id:guid}", (Guid id, HrListItem body, ApiRequest api) => SaveDepartmentAsync(id, body, api)).RequireAuthorization(Need(Permissions.HrEdit));
        api.MapDelete("/hr/departments/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            await api.RequireCompanyChangeAsync();
            await new HrService(await api.ForChangeAsync()).DeleteDepartmentAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.HrEdit));
        api.MapGet("/hr/designations", async (ApiRequest api) => Results.Ok(await DesignationsAsync(api))).RequireAuthorization(Need(Permissions.HrView));
        api.MapPost("/hr/designations", (HrListItem body, ApiRequest api) => SaveDesignationAsync(null, body, api)).RequireAuthorization(Need(Permissions.HrEdit));
        api.MapPut("/hr/designations/{id:guid}", (Guid id, HrListItem body, ApiRequest api) => SaveDesignationAsync(id, body, api)).RequireAuthorization(Need(Permissions.HrEdit));
        api.MapDelete("/hr/designations/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            await api.RequireCompanyChangeAsync();
            await new HrService(await api.ForChangeAsync()).DeleteDesignationAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.HrEdit));

        api.MapGet("/hr/settings", HrSettingsAsync).RequireAuthorization(Need(Permissions.HrView));
        api.MapPut("/hr/stores/{id:guid}/country", async (Guid id, CountryRequest body, ApiRequest api) =>
        {
            api.RequireStore(id, "That store");
            await new HrService(api.For(id)).SetStoreCountryAsync(id, body.Country);
            return Results.Ok(await HrSettingsAsync(api));
        }).RequireAuthorization(Need(Permissions.HrEdit));

        api.MapGet("/hr/rule-sets", async (ApiRequest api) => Results.Ok((await new HrService(api.Root).GetRuleSetsAsync(api.Text("country"))).Select(ToInfo)))
            .RequireAuthorization(Need(Permissions.HrView));
        api.MapGet("/hr/rule-sets/{id:guid}", async (Guid id, ApiRequest api) =>
            await new HrService(api.Root).GetRuleSetAsync(id) is { } set ? Results.Ok(ToInfo(set)) : throw ApiException.NotFound("That rule set"))
            .RequireAuthorization(Need(Permissions.HrView));
        api.MapPost("/hr/rule-sets", (RuleSetInfo body, ApiRequest api) => SaveRuleSetAsync(null, body, api)).RequireAuthorization(Need(Permissions.HrEdit));
        api.MapPut("/hr/rule-sets/{id:guid}", (Guid id, RuleSetInfo body, ApiRequest api) => SaveRuleSetAsync(id, body, api)).RequireAuthorization(Need(Permissions.HrEdit));
        api.MapDelete("/hr/rule-sets/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            await api.RequireCompanyChangeAsync();
            if (await new HrService(api.Root).GetRuleSetAsync(id) is null) throw ApiException.NotFound("That rule set");
            await new HrService(await api.ForChangeAsync()).DeleteRuleSetAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.HrEdit));

        api.MapGet("/hr/leave-types", async (ApiRequest api) => Results.Ok((await new HrService(api.Root).GetLeaveTypesAsync(api.Text("country"))).Select(ToInfo)))
            .RequireAuthorization(Need(Permissions.HrView));
        api.MapPost("/hr/leave-types", (LeaveTypeInfo body, ApiRequest api) => SaveLeaveTypeAsync(null, body, api)).RequireAuthorization(Need(Permissions.HrEdit));
        api.MapPut("/hr/leave-types/{id:guid}", (Guid id, LeaveTypeInfo body, ApiRequest api) => SaveLeaveTypeAsync(id, body, api)).RequireAuthorization(Need(Permissions.HrEdit));
        api.MapDelete("/hr/leave-types/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            await api.RequireCompanyChangeAsync();
            await new HrService(await api.ForChangeAsync()).DeleteLeaveTypeAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.HrEdit));

        api.MapGet("/hr/holidays", async (ApiRequest api) => Results.Ok((await new HrService(api.Root).GetHolidaysAsync(api.Text("country"), api.Number("year"))).Select(ToInfo)))
            .RequireAuthorization(Need(Permissions.HrView));
        api.MapPost("/hr/holidays", (HolidayInfo body, ApiRequest api) => SaveHolidayAsync(null, body, api)).RequireAuthorization(Need(Permissions.HrEdit));
        api.MapPut("/hr/holidays/{id:guid}", (Guid id, HolidayInfo body, ApiRequest api) => SaveHolidayAsync(id, body, api)).RequireAuthorization(Need(Permissions.HrEdit));
        api.MapDelete("/hr/holidays/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            await api.RequireCompanyChangeAsync();
            await new HrService(await api.ForChangeAsync()).DeleteHolidayAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.HrEdit));

        // Employees.
        api.MapGet("/hr/employees", EmployeesAsync).RequireAuthorization(Need(Permissions.HrView));
        api.MapGet("/hr/employees/next-code", async (ApiRequest api) => Results.Ok(new { code = await new HrService(api.Root).NextCodeAsync() }))
            .RequireAuthorization(Need(Permissions.HrEdit));
        api.MapGet("/hr/employees/{id:guid}", async (Guid id, ApiRequest api) => Results.Ok(await EmployeeDetailAsync(api, await VisibleEmployeeAsync(api, id))))
            .RequireAuthorization(Need(Permissions.HrView));
        api.MapPost("/hr/employees", (EmployeeDetail body, ApiRequest api) => SaveEmployeeAsync(null, body, api)).RequireAuthorization(Need(Permissions.HrEdit));
        api.MapPut("/hr/employees/{id:guid}", (Guid id, EmployeeDetail body, ApiRequest api) => SaveEmployeeAsync(id, body, api)).RequireAuthorization(Need(Permissions.HrEdit));
        api.MapDelete("/hr/employees/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            var employee = await VisibleEmployeeAsync(api, id);
            await new HrService(await api.ForChangeAsync(employee.StoreId)).DeleteEmployeeAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.HrEdit));
        api.MapGet("/hr/employees/{id:guid}/documents", async (Guid id, ApiRequest api) =>
        {
            await VisibleEmployeeAsync(api, id);
            return Results.Ok((await new HrService(api.Root).GetDocumentsAsync(id)).Select(ToRow));
        }).RequireAuthorization(Need(Permissions.HrView));
        api.MapPost("/hr/employees/{id:guid}/documents", async (Guid id, HttpRequest request, ApiRequest api) =>
        {
            var employee = await VisibleEmployeeAsync(api, id);
            if (!request.HasFormContentType) throw ApiException.Bad("Send the document as a form upload with the field file.");
            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file") ?? throw ApiException.Bad("Send the document in the field file.");
            if (file.Length > HrService.MaxDocumentBytes) throw ApiException.Bad($"A document is at most {HrService.MaxDocumentBytes / 1024 / 1024} MB.");
            byte[] content;
            await using (var stream = file.OpenReadStream())
            using (var memory = new MemoryStream())
            {
                await stream.CopyToAsync(memory);
                content = memory.ToArray();
            }
            var saved = await new HrService(await api.ForChangeAsync(employee.StoreId)).AddDocumentAsync(id, form["title"].FirstOrDefault(), file.FileName, file.ContentType, content);
            return Results.Created($"/api/hr/documents/{saved.Id}", ToRow(saved));
        }).RequireAuthorization(Need(Permissions.HrEdit)).DisableAntiforgery();
        api.MapGet("/hr/documents/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            var document = await new HrService(api.Root).GetDocumentAsync(id) ?? throw ApiException.NotFound("That document");
            await VisibleEmployeeAsync(api, document.EmployeeId, "That document");
            return Results.File(document.Data, document.ContentType, document.FileName);
        }).RequireAuthorization(Need(Permissions.HrView));
        api.MapDelete("/hr/documents/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            var document = await new HrService(api.Root).GetDocumentAsync(id) ?? throw ApiException.NotFound("That document");
            var employee = await VisibleEmployeeAsync(api, document.EmployeeId, "That document");
            await new HrService(await api.ForChangeAsync(employee.StoreId)).DeleteDocumentAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.HrEdit));

        // Shifts and the roster.
        api.MapGet("/hr/shifts", async (ApiRequest api) => Results.Ok((await new AttendanceService(api.For(api.ListStore())).GetShiftsAsync()).Select(ToInfo)))
            .RequireAuthorization(Need(Permissions.HrView));
        api.MapPost("/hr/shifts", (ShiftInfo body, ApiRequest api) => SaveShiftAsync(null, body, api)).RequireAuthorization(Need(Permissions.HrEdit));
        api.MapPut("/hr/shifts/{id:guid}", (Guid id, ShiftInfo body, ApiRequest api) => SaveShiftAsync(id, body, api)).RequireAuthorization(Need(Permissions.HrEdit));
        api.MapDelete("/hr/shifts/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            var shift = await StoredShiftAsync(api, id);
            await RequireShiftChangeAsync(api, shift.StoreId);
            await new AttendanceService(await api.ForChangeAsync(shift.StoreId)).DeleteShiftAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.HrEdit));
        api.MapGet("/hr/roster", RosterAsync).RequireAuthorization(Need(Permissions.HrView));
        api.MapPut("/hr/roster", async (RosterRequest body, ApiRequest api) =>
        {
            foreach (var a in body.Assignments) await VisibleEmployeeAsync(api, a.EmployeeId, "An employee on the roster");
            await new AttendanceService(await api.ForChangeAsync()).SetRosterAsync(body.WeekStart, [.. body.Assignments.Select(a => (a.EmployeeId, a.ShiftId))]);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.HrAttendance));
        api.MapPost("/hr/roster/changes", async (RosterChangeInfo body, ApiRequest api) =>
        {
            var employee = await VisibleEmployeeAsync(api, body.EmployeeId);
            var saved = await new AttendanceService(await api.ForChangeAsync(employee.StoreId))
                .SaveRosterDayAsync(new RosterDay { EmployeeId = body.EmployeeId, Date = body.Date, ShiftId = body.ShiftId, Note = body.Note });
            return Results.Ok(new RosterChangeInfo(saved.Id, saved.EmployeeId, saved.Date, saved.ShiftId, saved.Note));
        }).RequireAuthorization(Need(Permissions.HrAttendance));
        api.MapDelete("/hr/roster/changes/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            Guid employeeId;
            await using (var db = api.Root.Create())
                employeeId = await db.RosterDays.AsNoTracking().Where(d => d.Id == id).Select(d => (Guid?)d.EmployeeId).SingleOrDefaultAsync()
                    ?? throw ApiException.NotFound("That change of the roster");
            var employee = await VisibleEmployeeAsync(api, employeeId, "That change of the roster");
            await new AttendanceService(await api.ForChangeAsync(employee.StoreId)).DeleteRosterDayAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.HrAttendance));

        // Attendance.
        api.MapGet("/hr/attendance", AttendanceAsync).RequireAuthorization(Need(Permissions.HrView));
        api.MapGet("/hr/attendance/sheet", AttendanceSheetAsync).RequireAuthorization(Need(Permissions.HrView));
        api.MapGet("/hr/attendance/corrections", CorrectionsAsync).RequireAuthorization(Need(Permissions.HrView));
        api.MapPost("/hr/attendance/corrections", async (CorrectionRequest body, ApiRequest api) =>
        {
            var employee = await VisibleEmployeeAsync(api, body.EmployeeId);
            var saved = await new AttendanceService(await api.ForChangeAsync(employee.StoreId)).RequestCorrectionAsync(new AttendanceCorrection
            {
                EmployeeId = body.EmployeeId, Date = body.Date, Status = body.Status, WorkedMinutes = body.WorkedMinutes, LateMinutes = body.LateMinutes,
                EarlyMinutes = body.EarlyMinutes, OvertimeMinutes = body.OvertimeMinutes, Reason = body.Reason ?? "",
            }, api.Session);
            return Results.Created($"/api/hr/attendance/corrections/{saved.Id}", ToRow(saved, employee));
        }).RequireAuthorization(Need(Permissions.HrAttendance));
        api.MapPost("/hr/attendance/corrections/{id:guid}/approve", (Guid id, DecisionRequest body, ApiRequest api) => DecideCorrectionAsync(id, true, body, api))
            .RequireAuthorization(Need(Permissions.HrAttendance));
        api.MapPost("/hr/attendance/corrections/{id:guid}/reject", (Guid id, DecisionRequest body, ApiRequest api) => DecideCorrectionAsync(id, false, body, api))
            .RequireAuthorization(Need(Permissions.HrAttendance));

        // Leave.
        api.MapGet("/hr/leave-requests", LeaveRequestsAsync).RequireAuthorization(Need(Permissions.HrView));
        api.MapPost("/hr/leave-requests", async (LeaveRequestBody body, ApiRequest api) =>
        {
            var employee = await VisibleEmployeeAsync(api, body.EmployeeId);
            var saved = await new LeaveService(await api.ForChangeAsync(employee.StoreId)).RequestAsync(new LeaveRequest
            {
                EmployeeId = body.EmployeeId, LeaveTypeId = body.LeaveTypeId, FromDate = body.FromDate, ToDate = body.ToDate, FirstDayHalf = body.FirstDayHalf,
                LastDayHalf = body.LastDayHalf, Reason = body.Reason,
            }, api.Session);
            return Results.Created($"/api/hr/leave-requests/{saved.Id}", await LeaveRowAsync(api, saved));
        }).RequireAuthorization(Need(Permissions.HrView));
        api.MapPost("/hr/leave-requests/{id:guid}/approve", (Guid id, DecisionRequest body, ApiRequest api) => DecideLeaveAsync(id, "approve", body, api))
            .RequireAuthorization(Need(Permissions.HrView));
        api.MapPost("/hr/leave-requests/{id:guid}/reject", (Guid id, DecisionRequest body, ApiRequest api) => DecideLeaveAsync(id, "reject", body, api))
            .RequireAuthorization(Need(Permissions.HrView));
        api.MapPost("/hr/leave-requests/{id:guid}/cancel", (Guid id, DecisionRequest body, ApiRequest api) => DecideLeaveAsync(id, "cancel", body, api))
            .RequireAuthorization(Need(Permissions.HrView));
        api.MapGet("/hr/leave-balances", LeaveBalancesAsync).RequireAuthorization(Need(Permissions.HrView));
        api.MapPost("/hr/leave-adjustments", async (LeaveAdjustmentBody body, ApiRequest api) =>
        {
            var employee = await VisibleEmployeeAsync(api, body.EmployeeId);
            var saved = await new LeaveService(await api.ForChangeAsync(employee.StoreId)).AdjustAsync(new LeaveAdjustment
            {
                EmployeeId = body.EmployeeId, LeaveTypeId = body.LeaveTypeId, Year = body.Year, Days = body.Days, Reason = body.Reason ?? "",
            }, api.Session);
            return Results.Created($"/api/hr/leave-adjustments/{saved.Id}", new { saved.Id, saved.EmployeeId, saved.LeaveTypeId, saved.Year, saved.Days, saved.Reason, saved.MadeBy });
        }).RequireAuthorization(Need(Permissions.HrLeave));

        MapPayroll(api);
    }

    // Who sees which employee -------------------------------------------------------------------------------------------

    /// <summary>An employee of a store is the store's record; one of head office (no store) is seen by those who see every store.</summary>
    private static bool Sees(ApiRequest api, Guid? store) => store is Guid s ? api.User.CanSee(s) : api.User.AllStores;

    /// <summary>The employee, or 404 when there is none or the user may not see their store.</summary>
    private static async Task<Employee> VisibleEmployeeAsync(ApiRequest api, Guid id, string what = "That employee")
    {
        await using var db = api.Root.Create();
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id);
        if (employee is null || !Sees(api, employee.StoreId)) throw ApiException.NotFound(what);
        return employee;
    }

    /// <summary>The employees of the stores in the list's scope (head office ones only for those who see every store).</summary>
    private static IQueryable<Employee> ScopedEmployees(RetailPlusDbContext db, ApiRequest api, Guid? store) => Scoped(db.Employees.AsNoTracking(), api, store);

    private static IQueryable<Employee> Scoped(IQueryable<Employee> q, ApiRequest api, Guid? store)
    {
        if (store is Guid s) return q.Where(e => e.StoreId == s);
        if (api.User.AllStores) return q;
        var own = api.User.StoreId ?? Guid.Empty;
        return q.Where(e => e.StoreId == own);
    }

    // Lists -----------------------------------------------------------------------------------------------------------------

    private static async Task<List<HrListItem>> DepartmentsAsync(ApiRequest api)
    {
        await using var db = api.Root.Create();
        var counts = await db.Employees.AsNoTracking().Where(e => e.HrDepartmentId != null).GroupBy(e => e.HrDepartmentId!.Value)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count);
        return [.. (await db.HrDepartments.AsNoTracking().OrderBy(d => d.Name).ToListAsync())
            .Select(d => new HrListItem(d.Id, ApiRequest.Version(d.UpdatedAtUtc), d.Name, d.IsActive, counts.GetValueOrDefault(d.Id)))];
    }

    private static async Task<List<HrListItem>> DesignationsAsync(ApiRequest api)
    {
        await using var db = api.Root.Create();
        var counts = await db.Employees.AsNoTracking().Where(e => e.DesignationId != null).GroupBy(e => e.DesignationId!.Value)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count);
        return [.. (await db.Designations.AsNoTracking().OrderBy(d => d.Name).ToListAsync())
            .Select(d => new HrListItem(d.Id, ApiRequest.Version(d.UpdatedAtUtc), d.Name, d.IsActive, counts.GetValueOrDefault(d.Id)))];
    }

    private static async Task<IResult> SaveDepartmentAsync(Guid? id, HrListItem body, ApiRequest api)
    {
        await api.RequireCompanyChangeAsync();
        if (id is Guid existing)
        {
            await using var db = api.Root.Create();
            var stored = await db.HrDepartments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == existing) ?? throw ApiException.NotFound("That department");
            ApiRequest.CheckVersion(body.Version, ApiRequest.Version(stored.UpdatedAtUtc));
        }
        var saved = await new HrService(await api.ForChangeAsync()).SaveDepartmentAsync(new HrDepartment { Id = id ?? Guid.Empty, Name = body.Name, IsActive = body.IsActive });
        var row = (await DepartmentsAsync(api)).Single(d => d.Id == saved.Id);
        return id is null ? Results.Created($"/api/hr/departments/{saved.Id}", row) : Results.Ok(row);
    }

    private static async Task<IResult> SaveDesignationAsync(Guid? id, HrListItem body, ApiRequest api)
    {
        await api.RequireCompanyChangeAsync();
        if (id is Guid existing)
        {
            await using var db = api.Root.Create();
            var stored = await db.Designations.AsNoTracking().SingleOrDefaultAsync(d => d.Id == existing) ?? throw ApiException.NotFound("That designation");
            ApiRequest.CheckVersion(body.Version, ApiRequest.Version(stored.UpdatedAtUtc));
        }
        var saved = await new HrService(await api.ForChangeAsync()).SaveDesignationAsync(new Designation { Id = id ?? Guid.Empty, Name = body.Name, IsActive = body.IsActive });
        var row = (await DesignationsAsync(api)).Single(d => d.Id == saved.Id);
        return id is null ? Results.Created($"/api/hr/designations/{saved.Id}", row) : Results.Ok(row);
    }

    // Settings, rule sets, leave types, holidays ------------------------------------------------------------------------------

    private static async Task<HrSettingsInfo> HrSettingsAsync(ApiRequest api)
    {
        var hr = new HrService(api.Root);
        var company = await hr.CompanyCountryAsync();
        var countries = await hr.StoreCountriesAsync();
        await using var db = api.Root.Create();
        var own = (await db.StoreSettingExtras.AsNoTracking().Where(x => x.Name == HrService.CountryExtra).Select(x => x.StoreId).ToListAsync()).ToHashSet();
        var stores = await api.User.Visible(db.Stores.AsNoTracking()).OrderBy(s => s.Code).ToListAsync();
        var known = (await db.PayrollRuleSets.AsNoTracking().Select(r => r.Country).Distinct().ToListAsync())
            .Concat(await db.LeaveTypes.AsNoTracking().Select(t => t.Country).Distinct().ToListAsync()).Append(company).Distinct().Order(StringComparer.Ordinal).ToList();
        return new HrSettingsInfo(company, known, [.. stores.Select(s => new HrStoreCountry(s.Id, s.Code, s.Name, countries.GetValueOrDefault(s.Id) ?? company, own.Contains(s.Id)))]);
    }

    private static RuleSetInfo ToInfo(PayrollRuleSet r) => new(r.Id, ApiRequest.Version(r.Version), r.Country, r.Name, r.EffectiveFrom, r.EffectiveTo, r.TaxYearStartMonth,
        r.MinimumWage, r.EobiEmployerRate, r.EobiEmployeeRate, r.SocialSecurityProvince, r.SocialSecurityRate, r.SocialSecurityWageCeiling, r.SocialSecurityBasis,
        r.WorkingDaysPerWeek, r.HoursPerWeek, r.OvertimeMultiplier, r.OvertimeMinimumMinutes, r.DailyRateBasis, r.LatenessRule, r.LatesPerDay, r.DeductAbsences,
        r.SurchargeThreshold, r.SurchargeRate, r.GratuityEnabled, r.GratuityDaysPerYear, r.GratuityMinimumMonths, r.GratuityPartYearMonths, r.GratuityOnBasic,
        r.CnicPattern, r.CnicExample, r.IbanPattern, r.IbanExample, r.Notes,
        [.. r.TaxSlabs.OrderBy(s => s.IncomeFrom).Select(s => new TaxSlabInfo(s.IncomeFrom, s.IncomeTo, s.FixedTax, s.Rate))],
        [.. r.Allowances.OrderBy(a => a.SortOrder).Select(a => new AllowanceRuleInfo(a.Code, a.Name, a.IsTaxable, a.ExemptPercentOfBasic, a.SortOrder))]);

    private static async Task<IResult> SaveRuleSetAsync(Guid? id, RuleSetInfo body, ApiRequest api)
    {
        await api.RequireCompanyChangeAsync();
        if (id is Guid existing)
        {
            var stored = await new HrService(api.Root).GetRuleSetAsync(existing) ?? throw ApiException.NotFound("That rule set");
            ApiRequest.CheckVersion(body.Version, ApiRequest.Version(stored.Version));
        }
        var set = new PayrollRuleSet
        {
            Id = id ?? Guid.Empty, Country = body.Country ?? "", Name = body.Name ?? "", EffectiveFrom = body.EffectiveFrom, EffectiveTo = body.EffectiveTo,
            TaxYearStartMonth = body.TaxYearStartMonth, MinimumWage = body.MinimumWage, EobiEmployerRate = body.EobiEmployerRate, EobiEmployeeRate = body.EobiEmployeeRate,
            SocialSecurityProvince = body.SocialSecurityProvince, SocialSecurityRate = body.SocialSecurityRate, SocialSecurityWageCeiling = body.SocialSecurityWageCeiling,
            SocialSecurityBasis = body.SocialSecurityBasis, WorkingDaysPerWeek = body.WorkingDaysPerWeek, HoursPerWeek = body.HoursPerWeek,
            OvertimeMultiplier = body.OvertimeMultiplier, OvertimeMinimumMinutes = body.OvertimeMinimumMinutes, DailyRateBasis = body.DailyRateBasis,
            LatenessRule = body.LatenessRule, LatesPerDay = body.LatesPerDay, DeductAbsences = body.DeductAbsences, SurchargeThreshold = body.SurchargeThreshold,
            SurchargeRate = body.SurchargeRate, GratuityEnabled = body.GratuityEnabled, GratuityDaysPerYear = body.GratuityDaysPerYear,
            GratuityMinimumMonths = body.GratuityMinimumMonths, GratuityPartYearMonths = body.GratuityPartYearMonths, GratuityOnBasic = body.GratuityOnBasic,
            CnicPattern = body.CnicPattern, CnicExample = body.CnicExample, IbanPattern = body.IbanPattern, IbanExample = body.IbanExample, Notes = body.Notes,
            TaxSlabs = [.. (body.TaxSlabs ?? []).Select(s => new PayrollTaxSlab { IncomeFrom = s.IncomeFrom, IncomeTo = s.IncomeTo, FixedTax = s.FixedTax, Rate = s.Rate })],
            Allowances = [.. (body.Allowances ?? []).Select(a => new PayrollAllowanceRule
                { Code = a.Code ?? "", Name = a.Name ?? "", IsTaxable = a.IsTaxable, ExemptPercentOfBasic = a.ExemptPercentOfBasic, SortOrder = a.SortOrder })],
        };
        var saved = await new HrService(await api.ForChangeAsync()).SaveRuleSetAsync(set);
        var info = ToInfo((await new HrService(api.Root).GetRuleSetAsync(saved.Id))!);
        return id is null ? Results.Created($"/api/hr/rule-sets/{saved.Id}", info) : Results.Ok(info);
    }

    private static LeaveTypeInfo ToInfo(LeaveType t) => new(t.Id, ApiRequest.Version(t.UpdatedAtUtc), t.Country, t.Code, t.Name, t.Kind, t.DaysPerYear, t.IsPaid,
        t.AccruesMonthly, t.CarryOverMaxDays, t.Encashable, t.CountsCalendarDays, t.OnlyFor, t.MinimumServiceMonths, t.IsActive, t.SortOrder);

    private static async Task<IResult> SaveLeaveTypeAsync(Guid? id, LeaveTypeInfo body, ApiRequest api)
    {
        await api.RequireCompanyChangeAsync();
        if (id is Guid existing)
        {
            await using var db = api.Root.Create();
            var stored = await db.LeaveTypes.AsNoTracking().SingleOrDefaultAsync(t => t.Id == existing) ?? throw ApiException.NotFound("That leave type");
            ApiRequest.CheckVersion(body.Version, ApiRequest.Version(stored.UpdatedAtUtc));
        }
        var saved = await new HrService(await api.ForChangeAsync()).SaveLeaveTypeAsync(new LeaveType
        {
            Id = id ?? Guid.Empty, Country = body.Country ?? "", Code = body.Code ?? "", Name = body.Name ?? "", Kind = body.Kind, DaysPerYear = body.DaysPerYear,
            IsPaid = body.IsPaid, AccruesMonthly = body.AccruesMonthly, CarryOverMaxDays = body.CarryOverMaxDays, Encashable = body.Encashable,
            CountsCalendarDays = body.CountsCalendarDays, OnlyFor = body.OnlyFor, MinimumServiceMonths = body.MinimumServiceMonths, IsActive = body.IsActive, SortOrder = body.SortOrder,
        });
        await using var check = api.Root.Create();
        var info = ToInfo(await check.LeaveTypes.AsNoTracking().SingleAsync(t => t.Id == saved.Id));
        return id is null ? Results.Created($"/api/hr/leave-types/{saved.Id}", info) : Results.Ok(info);
    }

    private static HolidayInfo ToInfo(Holiday h) => new(h.Id, ApiRequest.Version(h.UpdatedAtUtc), h.Country, h.Date, h.Name);

    private static async Task<IResult> SaveHolidayAsync(Guid? id, HolidayInfo body, ApiRequest api)
    {
        await api.RequireCompanyChangeAsync();
        if (id is Guid existing)
        {
            await using var db = api.Root.Create();
            var stored = await db.Holidays.AsNoTracking().SingleOrDefaultAsync(h => h.Id == existing) ?? throw ApiException.NotFound("That holiday");
            ApiRequest.CheckVersion(body.Version, ApiRequest.Version(stored.UpdatedAtUtc));
        }
        var saved = await new HrService(await api.ForChangeAsync()).SaveHolidayAsync(new Holiday { Id = id ?? Guid.Empty, Country = body.Country ?? "", Date = body.Date, Name = body.Name ?? "" });
        await using var check = api.Root.Create();
        var info = ToInfo(await check.Holidays.AsNoTracking().SingleAsync(h => h.Id == saved.Id));
        return id is null ? Results.Created($"/api/hr/holidays/{saved.Id}", info) : Results.Ok(info);
    }

    // Employees -------------------------------------------------------------------------------------------------------------

    private static async Task<IResult> EmployeesAsync(ApiRequest api)
    {
        var store = api.ListStore();
        await using var db = api.Root.Create();
        var status = api.Text("status")?.ToLowerInvariant();
        var query = new EmployeeQuery(api.Text("search"), api.Id("departmentId"), api.Id("designationId"),
            status switch { "left" => false, "all" => null, _ => true }, await api.TodayAsync(store));
        var rows = Scoped(HrService.Query(db, query, null), api, store).OrderBy(e => e.FullName).ThenBy(e => e.Code);
        var names = await NamesAsync(db);
        var today = await api.TodayAsync(store);
        return Results.Ok(await api.PageAsync(rows, e => new EmployeeRow(e.Id, e.Code, e.FullName,
            e.DesignationId is Guid d ? names.Designations.GetValueOrDefault(d) : null, e.HrDepartmentId is Guid h ? names.Departments.GetValueOrDefault(h) : null,
            e.StoreId, e.StoreId is Guid s ? names.Stores.GetValueOrDefault(s) : null, e.EmploymentType, e.JoiningDate, e.LeavingDate,
            e.LeavingDate is null || e.LeavingDate >= today, e.Phone, e.UserAccountId is Guid u ? names.Users.GetValueOrDefault(u) : null)));
    }

    private sealed record HrNames(Dictionary<Guid, string> Departments, Dictionary<Guid, string> Designations, Dictionary<Guid, string> Stores, Dictionary<Guid, string> Users);

    private static async Task<HrNames> NamesAsync(RetailPlusDbContext db) => new(
        await db.HrDepartments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name),
        await db.Designations.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name),
        await db.Stores.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Code),
        await db.UserAccounts.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.UserName));

    private static async Task<EmployeeDetail> EmployeeDetailAsync(ApiRequest api, Employee e)
    {
        await using var db = api.Root.Create();
        var manager = e.ManagerId is Guid m ? await db.Employees.AsNoTracking().Where(x => x.Id == m).Select(x => x.FullName).FirstOrDefaultAsync() : null;
        var user = e.UserAccountId is Guid u ? await db.UserAccounts.AsNoTracking().Where(x => x.Id == u).Select(x => x.UserName).FirstOrDefaultAsync() : null;
        var country = await new HrService(api.Root).CountryOfStoreAsync(e.StoreId);
        return new EmployeeDetail(e.Id, ApiRequest.Version(e.Version), e.Code, e.FullName, e.FatherOrHusbandName, e.Cnic, e.DateOfBirth, e.Gender, e.MaritalStatus,
            e.Address, e.City, e.Phone, e.Email, e.EmergencyContactName, e.EmergencyContactPhone, e.EmergencyContactRelation, e.JoiningDate, e.ConfirmationDate,
            e.LeavingDate, e.LeavingReason, e.EmploymentType, e.HrDepartmentId, e.DesignationId, e.StoreId, e.ManagerId, e.UserAccountId, e.ShiftId,
            e.TracksAttendance, e.OvertimeEligible, e.EobiApplies, e.SocialSecurityApplies, e.PaymentMethod, e.BankName, e.Iban, e.Ntn, e.EobiNumber,
            e.SocialSecurityNumber, e.PriorTaxYear, e.PriorTaxableIncome, e.PriorTaxDeducted, e.Notes, manager, user, country);
    }

    private static async Task<IResult> SaveEmployeeAsync(Guid? id, EmployeeDetail body, ApiRequest api)
    {
        if (id is Guid existing)
        {
            var stored = await VisibleEmployeeAsync(api, existing);
            ApiRequest.CheckVersion(body.Version, ApiRequest.Version(stored.Version));
        }
        // A store administrator keeps their employees at their own store; head office employees are for those who see every store.
        if (!Sees(api, body.StoreId))
            throw ApiException.Forbidden(body.StoreId is null ? "Employees of head office (no store) are kept by a user who sees every store." : "You may not see that store.");
        if (body.ManagerId is Guid manager) await VisibleEmployeeAsync(api, manager, "That manager");
        var employee = new Employee
        {
            Id = id ?? Guid.Empty, Code = body.Code ?? "", FullName = body.FullName ?? "", FatherOrHusbandName = body.FatherOrHusbandName, Cnic = body.Cnic,
            DateOfBirth = body.DateOfBirth, Gender = body.Gender, MaritalStatus = body.MaritalStatus, Address = body.Address, City = body.City, Phone = body.Phone,
            Email = body.Email, EmergencyContactName = body.EmergencyContactName, EmergencyContactPhone = body.EmergencyContactPhone,
            EmergencyContactRelation = body.EmergencyContactRelation, JoiningDate = body.JoiningDate, ConfirmationDate = body.ConfirmationDate,
            LeavingDate = body.LeavingDate, LeavingReason = body.LeavingReason, EmploymentType = body.EmploymentType, HrDepartmentId = body.DepartmentId,
            DesignationId = body.DesignationId, StoreId = body.StoreId, ManagerId = body.ManagerId, UserAccountId = body.UserId, ShiftId = body.ShiftId,
            TracksAttendance = body.TracksAttendance, OvertimeEligible = body.OvertimeEligible, EobiApplies = body.EobiApplies,
            SocialSecurityApplies = body.SocialSecurityApplies, PaymentMethod = body.PaymentMethod, BankName = body.BankName, Iban = body.Iban, Ntn = body.Ntn,
            EobiNumber = body.EobiNumber, SocialSecurityNumber = body.SocialSecurityNumber, PriorTaxYear = body.PriorTaxYear,
            PriorTaxableIncome = body.PriorTaxableIncome, PriorTaxDeducted = body.PriorTaxDeducted, Notes = body.Notes,
        };
        var saved = await new HrService(await api.ForChangeAsync(body.StoreId)).SaveEmployeeAsync(employee);
        var detail = await EmployeeDetailAsync(api, (await new HrService(api.Root).GetEmployeeAsync(saved.Id))!);
        return id is null ? Results.Created($"/api/hr/employees/{saved.Id}", detail) : Results.Ok(detail);
    }

    private static EmployeeDocumentRow ToRow(EmployeeDocumentInfo d) =>
        new(d.Id, d.EmployeeId, d.Title, d.FileName, d.ContentType, d.Size, ApiRequest.Utc(d.UploadedAtUtc), d.UploadedBy);

    // Shifts and the roster -------------------------------------------------------------------------------------------------

    private static readonly DayOfWeek[] Week = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    private static ShiftInfo ToInfo(Shift s) => new(s.Id, ApiRequest.Version(s.UpdatedAtUtc), s.Name, s.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        s.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture), s.BreakMinutes, s.GraceMinutes,
        [.. Week.Where(d => AttendanceRules.Works(s.Days, d)).Select(d => d.ToString())], s.StoreId, s.IsActive);

    private static async Task<Shift> StoredShiftAsync(ApiRequest api, Guid id)
    {
        await using var db = api.Root.Create();
        var shift = await db.Shifts.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id);
        if (shift is null || (shift.StoreId is Guid store && !api.User.CanSee(store))) throw ApiException.NotFound("That shift");
        return shift;
    }

    /// <summary>A store's shift is changed by those who see the store; a shift of every store follows the rule for shared records.</summary>
    private static async Task RequireShiftChangeAsync(ApiRequest api, Guid? store)
    {
        if (store is Guid s)
        {
            if (!api.User.CanSee(s)) throw ApiException.Forbidden("You may not see that store.");
        }
        else await api.RequireCompanyChangeAsync();
    }

    private static async Task<IResult> SaveShiftAsync(Guid? id, ShiftInfo body, ApiRequest api)
    {
        if (id is Guid existing)
        {
            var stored = await StoredShiftAsync(api, existing);
            ApiRequest.CheckVersion(body.Version, ApiRequest.Version(stored.UpdatedAtUtc));
            await RequireShiftChangeAsync(api, stored.StoreId);
        }
        await RequireShiftChangeAsync(api, body.StoreId);
        if (!TimeOnly.TryParse(body.StartTime, CultureInfo.InvariantCulture, out var start) || !TimeOnly.TryParse(body.EndTime, CultureInfo.InvariantCulture, out var end))
            throw ApiException.Bad("Times are like 09:00 or 17:30.");
        var days = WorkDays.None;
        foreach (var name in body.Days ?? [])
            days |= Enum.TryParse<DayOfWeek>(name, true, out var day) ? AttendanceRules.Flag(day) : throw ApiException.Bad($"{name} is not a day of the week.");
        var saved = await new AttendanceService(await api.ForChangeAsync(body.StoreId)).SaveShiftAsync(new Shift
        {
            Id = id ?? Guid.Empty, Name = body.Name ?? "", StartTime = start, EndTime = end, BreakMinutes = body.BreakMinutes, GraceMinutes = body.GraceMinutes,
            Days = days, StoreId = body.StoreId, IsActive = body.IsActive,
        });
        var info = ToInfo(await StoredShiftAsync(api, saved.Id));
        return id is null ? Results.Created($"/api/hr/shifts/{saved.Id}", info) : Results.Ok(info);
    }

    private static async Task<IResult> RosterAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var week = DateOnly.TryParseExact(api.Text("week"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var w) ? w : await api.TodayAsync(store);
        var start = TimeClock.WeekStart(week);
        var rows = await new AttendanceService(api.For(store)).GetRosterAsync(start);
        rows = [.. rows.Where(r => Sees(api, r.StoreId))];
        return Results.Ok(new RosterInfo(start, [.. Enumerable.Range(0, 7).Select(start.AddDays)],
            [.. rows.Select(r => new RosterRowInfo(r.EmployeeId, r.Code, r.Name, r.StoreId, r.UsualShiftId, r.WeekShiftId,
                [.. r.Days.Select(d => new RosterChangeInfo(d.Id, d.EmployeeId, d.Date, d.ShiftId, d.Note))]))]));
    }

    // Attendance --------------------------------------------------------------------------------------------------------------

    /// <summary>The employees a list of attendance or leave covers: one asked for (404 when not visible), else the list's store.</summary>
    private static async Task<List<Employee>> ListEmployeesAsync(ApiRequest api, Guid? store, DateOnly from, DateOnly to)
    {
        if (api.Id("employeeId") is Guid one) return [await VisibleEmployeeAsync(api, one)];
        await using var db = api.Root.Create();
        return await ScopedEmployees(db, api, store).Where(e => e.JoiningDate <= to && (e.LeavingDate == null || e.LeavingDate >= from))
            .OrderBy(e => e.FullName).ThenBy(e => e.Code).ToListAsync();
    }

    private static async Task<IResult> AttendanceAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var (from, to) = await api.PeriodAsync(store);
        if (to.DayNumber - from.DayNumber > 92) throw ApiException.Bad("Attendance is listed for at most 93 days at a time.");
        var employees = await ListEmployeesAsync(api, store, from, to);
        var days = await new AttendanceService(api.For(store)).DaysAsync(from, to, [.. employees.Select(e => e.Id)], await api.TodayAsync(store));
        var byId = employees.ToDictionary(e => e.Id);
        return Results.Ok(days.Select(d => new AttendanceRow(d.EmployeeId, byId[d.EmployeeId].Code, byId[d.EmployeeId].FullName, d.Date, d.Status, d.Shift, d.ShiftStart, d.ShiftEnd,
            d.FirstIn, d.LastOut, d.WorkedMinutes, d.LateMinutes, d.EarlyMinutes, d.OvertimeMinutes, d.Holiday, d.LeaveCode, d.LeaveDays, d.Corrected)));
    }

    /// <summary>The mark of a day on the monthly sheet.</summary>
    private static string Mark(AttendanceDay d) => d.Status switch
    {
        AttendanceStatus.Present when d.LeaveDays > 0 => "P½",
        AttendanceStatus.Present when d.LateMinutes > 0 => "L",
        AttendanceStatus.Present when d.EarlyMinutes > 0 => "E",
        AttendanceStatus.Present => "P",
        AttendanceStatus.Absent => "A",
        AttendanceStatus.Leave => d.LeaveDays < 1 ? "½" + (d.LeaveCode ?? "LV") : d.LeaveCode ?? "LV",
        AttendanceStatus.Holiday => "H",
        AttendanceStatus.Off => "O",
        _ => "·",
    };

    private static async Task<IResult> AttendanceSheetAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var today = await api.TodayAsync(store);
        var month = api.Text("month") is { } text
            ? DateOnly.TryParseExact(text + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m) ? m : throw ApiException.Bad($"month must be like 2026-11, not {text}.")
            : new DateOnly(today.Year, today.Month, 1);
        var from = new DateOnly(month.Year, month.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var employees = await ListEmployeesAsync(api, store, from, to);
        var days = (await new AttendanceService(api.For(store)).DaysAsync(from, to, [.. employees.Select(e => e.Id)], today)).ToLookup(d => d.EmployeeId);
        var dates = Enumerable.Range(0, to.Day).Select(from.AddDays).ToList();
        var rows = employees.Select(e =>
        {
            var own = days[e.Id].ToDictionary(d => d.Date);
            var list = own.Values.ToList();
            return new AttendanceSheetRow(e.Id, e.Code, e.FullName, [.. dates.Select(d => own.TryGetValue(d, out var day) ? Mark(day) : "")],
                list.Count(d => d.Status == AttendanceStatus.Present), list.Count(d => d.LateMinutes > 0), list.Count(d => d.EarlyMinutes > 0),
                list.Count(d => d.Status == AttendanceStatus.Absent), list.Sum(d => d.LeaveDays), list.Count(d => d.Status == AttendanceStatus.Holiday),
                list.Count(d => d.Status == AttendanceStatus.Off), Math.Round(list.Sum(d => d.OvertimeMinutes) / 60m, 2));
        }).ToList();
        return Results.Ok(new AttendanceSheet(from.ToString("yyyy-MM", CultureInfo.InvariantCulture), dates, rows));
    }

    private static CorrectionRow ToRow(AttendanceCorrection c, Employee e) => new(c.Id, c.EmployeeId, e.Code, e.FullName, c.Date, c.Status, c.WorkedMinutes, c.LateMinutes,
        c.EarlyMinutes, c.OvertimeMinutes, c.Reason, c.Approval, c.RequestedBy, ApiRequest.Utc(c.CreatedAtUtc), c.DecidedBy, ApiRequest.Utc(c.DecidedAtUtc), c.DecisionNote);

    private static async Task<IResult> CorrectionsAsync(ApiRequest api)
    {
        var store = api.ListStore();
        HrApproval? approval = Enum.TryParse<HrApproval>(api.Text("status"), true, out var a) ? a : null;
        var from = DateOnly.TryParseExact(api.Text("from"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : (DateOnly?)null;
        var to = DateOnly.TryParseExact(api.Text("to"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : (DateOnly?)null;
        var rows = await new AttendanceService(api.For(store)).GetCorrectionsAsync(from, to, approval, api.Id("employeeId"));
        await using var db = api.Root.Create();
        var ids = rows.Select(r => r.EmployeeId).Distinct().ToList();
        var employees = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id);
        return Results.Ok(rows.Where(r => employees.TryGetValue(r.EmployeeId, out var e) && Sees(api, e.StoreId)).Select(r => ToRow(r, employees[r.EmployeeId])));
    }

    private static async Task<IResult> DecideCorrectionAsync(Guid id, bool approve, DecisionRequest body, ApiRequest api)
    {
        Guid employeeId;
        await using (var db = api.Root.Create())
            employeeId = await db.AttendanceCorrections.AsNoTracking().Where(c => c.Id == id).Select(c => (Guid?)c.EmployeeId).SingleOrDefaultAsync()
                ?? throw ApiException.NotFound("That correction");
        var employee = await VisibleEmployeeAsync(api, employeeId, "That correction");
        var saved = await new AttendanceService(await api.ForChangeAsync(employee.StoreId)).DecideCorrectionAsync(id, approve, body?.Note, api.Session);
        return Results.Ok(ToRow(saved, employee));
    }

    // Leave -------------------------------------------------------------------------------------------------------------------

    private static async Task<LeaveRequestRow> LeaveRowAsync(ApiRequest api, LeaveRequest r)
    {
        await using var db = api.Root.Create();
        var e = await db.Employees.AsNoTracking().SingleAsync(x => x.Id == r.EmployeeId);
        var type = await db.LeaveTypes.AsNoTracking().Where(t => t.Id == r.LeaveTypeId).Select(t => t.Name).SingleAsync();
        var may = r.Status == LeaveRequestStatus.Pending && await new LeaveService(api.Root).MayDecideAsync(e.Id, api.Session);
        return ToRow(r, e, type, may);
    }

    private static LeaveRequestRow ToRow(LeaveRequest r, Employee e, string type, bool mayDecide) => new(r.Id, r.EmployeeId, e.Code, e.FullName, r.LeaveTypeId, type,
        r.FromDate, r.ToDate, r.FirstDayHalf, r.LastDayHalf, r.Days, r.Reason, r.Status, r.RequestedBy, ApiRequest.Utc(r.CreatedAtUtc), r.DecidedBy,
        ApiRequest.Utc(r.DecidedAtUtc), r.DecisionNote, mayDecide);

    private static async Task<IResult> LeaveRequestsAsync(ApiRequest api)
    {
        var store = api.ListStore();
        await using var db = api.Root.Create();
        var scoped = ScopedEmployees(db, api, store);
        var q = db.LeaveRequests.AsNoTracking().Where(r => scoped.Any(e => e.Id == r.EmployeeId));
        if (Enum.TryParse<LeaveRequestStatus>(api.Text("status"), true, out var status)) q = q.Where(r => r.Status == status);
        if (api.Id("employeeId") is Guid employeeId)
        {
            await VisibleEmployeeAsync(api, employeeId);
            q = q.Where(r => r.EmployeeId == employeeId);
        }
        if (DateOnly.TryParseExact(api.Text("from"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)) q = q.Where(r => r.ToDate >= from);
        if (DateOnly.TryParseExact(api.Text("to"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to)) q = q.Where(r => r.FromDate <= to);
        var page = await api.PageAsync(q.OrderByDescending(r => r.FromDate).ThenByDescending(r => r.CreatedAtUtc), r => r);
        var ids = page.Items.Select(r => r.EmployeeId).Distinct().ToList();
        var employees = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id);
        var types = await db.LeaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name);
        var leave = new LeaveService(api.Root);
        var may = new Dictionary<Guid, bool>();
        foreach (var id in ids) may[id] = await leave.MayDecideAsync(id, api.Session);
        return Results.Ok(new PageOf<LeaveRequestRow>([.. page.Items.Select(r => ToRow(r, employees[r.EmployeeId], types.GetValueOrDefault(r.LeaveTypeId) ?? "",
            r.Status == LeaveRequestStatus.Pending && may[r.EmployeeId]))], page.Total, page.Page, page.PageSize));
    }

    private static async Task<IResult> DecideLeaveAsync(Guid id, string what, DecisionRequest body, ApiRequest api)
    {
        Guid employeeId;
        await using (var db = api.Root.Create())
            employeeId = await db.LeaveRequests.AsNoTracking().Where(r => r.Id == id).Select(r => (Guid?)r.EmployeeId).SingleOrDefaultAsync()
                ?? throw ApiException.NotFound("That leave request");
        var employee = await VisibleEmployeeAsync(api, employeeId, "That leave request");
        var leave = new LeaveService(await api.ForChangeAsync(employee.StoreId));
        if (what != "cancel" && !await leave.MayDecideAsync(employeeId, api.Session))
            throw ApiException.Forbidden($"Only {employee.FullName}'s manager, or someone who approves leave, decides on it.");
        var saved = what switch
        {
            "approve" => await leave.DecideAsync(id, true, body?.Note, api.Session),
            "reject" => await leave.DecideAsync(id, false, body?.Note, api.Session),
            _ => await leave.CancelAsync(id, api.Session),
        };
        return Results.Ok(await LeaveRowAsync(api, saved));
    }

    private static async Task<IResult> LeaveBalancesAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var today = await api.TodayAsync(store);
        int year = api.Number("year") ?? today.Year;
        if (year is < 2000 or > 2100) throw ApiException.Bad("Give a year like 2026.");
        var employees = await ListEmployeesAsync(api, store, new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));
        var balances = await new LeaveService(api.For(store)).BalancesAsync(year, [.. employees.Select(e => e.Id)]);
        var byId = employees.ToDictionary(e => e.Id);
        return Results.Ok(balances.Select(b => new LeaveBalanceRow(b.EmployeeId, byId[b.EmployeeId].Code, byId[b.EmployeeId].FullName, b.LeaveTypeId, b.Code, b.Name, b.Paid,
            b.Limited, b.Entitled, b.Earned, b.CarriedIn, b.Adjusted, b.Taken, b.Pending, b.Available)));
    }
}
