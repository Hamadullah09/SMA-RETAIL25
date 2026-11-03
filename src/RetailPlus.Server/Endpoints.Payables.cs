using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.Server;

public sealed record BillListRow(Guid Id, long Number, SupplierBillKind Kind, SupplierBillStatus Status, Guid SupplierId, string Supplier, Guid StoreId, string StoreCode,
    DateOnly BillDate, DateOnly DueDate, string? SupplierReference, string Currency, decimal Total, decimal Paid, decimal Open, int DaysOverdue);
public sealed record BillLineInfo(int LineNumber, Guid? AccountId, string? AccountCode, Guid? ItemId, string Description, decimal Quantity, decimal UnitPrice, decimal Amount,
    decimal TaxRate, decimal TaxAmount, Guid? PurchaseOrderId, Guid? PurchaseOrderLineId);
public sealed record BillPaymentInfo(Guid PaymentId, long Number, DateOnly Date, decimal Amount, SupplierPaymentStatus Status);
public sealed record BillDetail(Guid Id, string Version, long Number, SupplierBillKind Kind, SupplierBillStatus Status, Guid SupplierId, string Supplier, Guid StoreId, string StoreCode,
    DateOnly BillDate, DateOnly DueDate, string? SupplierReference, string Currency, decimal ExchangeRate, string? Notes, decimal Subtotal, decimal TaxTotal, decimal Total,
    decimal Paid, decimal Open, Guid? OriginalBillId, Guid? ExpenseId, Guid? JournalEntryId, string? CreatedBy, string? PostedBy, DateTime? PostedAtUtc, string? VoidReason,
    IReadOnlyList<BillLineInfo> Lines, IReadOnlyList<BillPaymentInfo> Payments, IReadOnlyList<AttachmentInfo> Attachments);
public sealed record BillLineRequest(Guid? AccountId, Guid? ItemId, string? Description, decimal Quantity, decimal UnitPrice, decimal TaxRate, decimal TaxAmount = 0,
    Guid? PurchaseOrderId = null, Guid? PurchaseOrderLineId = null);
public sealed record BillRequest(SupplierBillKind Kind, Guid SupplierId, Guid StoreId, DateOnly BillDate, DateOnly? DueDate, string? SupplierReference, string? Currency,
    decimal ExchangeRate, string? Notes, Guid? OriginalBillId, IReadOnlyList<BillLineRequest> Lines, string? Version = null);
public sealed record VoidRequest(DateOnly Date, string Reason);
public sealed record PaymentListRow(Guid Id, long Number, DateOnly Date, Guid SupplierId, string Supplier, string StoreCode, Guid BankAccountId, string BankAccount, string Currency,
    decimal Amount, decimal WithholdingAmount, string? Reference, SupplierPaymentStatus Status, Guid? PaymentRunId);
public sealed record PaymentAllocationInfo(Guid BillId, long BillNumber, SupplierBillKind Kind, decimal Amount);
public sealed record PaymentDetail(Guid Id, long Number, DateOnly Date, Guid SupplierId, string Supplier, Guid StoreId, string StoreCode, Guid BankAccountId, string BankAccount,
    string Currency, decimal ExchangeRate, decimal Amount, decimal WithholdingAmount, WithholdingKind WithholdingKind, decimal WithholdingRate, string? Reference, string? Notes,
    SupplierPaymentStatus Status, Guid? PaymentRunId, Guid? JournalEntryId, IReadOnlyList<PaymentAllocationInfo> Allocations);
public sealed record BillToPayRequest(Guid BillId, decimal Amount);
public sealed record PaymentRequest(Guid BankAccountId, DateOnly Date, IReadOnlyList<BillToPayRequest> Bills, decimal? WithholdingAmount, decimal ExchangeRate = 1,
    string? Reference = null, string? Notes = null);
public sealed record PaymentRunInfo(Guid Id, long Number, DateOnly Date, Guid BankAccountId, decimal Total, int Payments, string? Notes, string? PaidBy, DateTime PaidAtUtc);
public sealed record PaymentRunResult(PaymentRunInfo Run, IReadOnlyList<PaymentListRow> Payments);
public sealed record WithholdingInfo(WithholdingKind Kind, decimal Rate, decimal Threshold, string? Section, string? RuleSet);
public sealed record SupplierFinanceInfo(Guid SupplierId, string? TaxNumber, string? SalesTaxNumber, bool IsFiler, WithholdingKind WithholdingKind, bool WithholdingExempt,
    int PaymentTermsDays, Guid? DefaultAccountId);
public sealed record StatementLineInfo(DateOnly Date, long EntryNumber, JournalKind Kind, string Memo, string? Reference, decimal Billed, decimal Paid, decimal Balance);
public sealed record SupplierStatementInfo(Guid SupplierId, string Supplier, DateOnly From, DateOnly To, decimal Opening, decimal Closing, IReadOnlyList<StatementLineInfo> Lines);
public sealed record ExpenseRow(Guid Id, string Version, long Number, DateOnly Date, Guid StoreId, string StoreCode, string Payee, Guid? SupplierId, Guid AccountId, string AccountCode,
    string AccountName, string? Description, decimal Amount, decimal TaxAmount, decimal Total, ExpensePaidBy PaidBy, Guid? BankAccountId, string? Reference, ExpenseStatus Status,
    Guid? SupplierBillId, Guid? RecurringExpenseId, Guid? JournalEntryId, int Attachments);
public sealed record ExpenseDetail(ExpenseRow Expense, IReadOnlyList<AttachmentInfo> Attachments);
public sealed record ExpenseRequest(DateOnly Date, Guid StoreId, string Payee, Guid? SupplierId, Guid AccountId, string? Description, decimal Amount, decimal TaxAmount,
    ExpensePaidBy PaidBy, Guid? BankAccountId, string? Reference, bool Post = false, string? Version = null);
public sealed record RecurringExpenseRow(Guid? Id, string? Version, string Name, Guid StoreId, string Payee, Guid? SupplierId, Guid AccountId, decimal Amount, decimal TaxAmount,
    ExpensePaidBy PaidBy, Guid? BankAccountId, RecurrenceFrequency Frequency, DateOnly NextDate, DateOnly? EndDate, bool PostAutomatically, bool IsActive, DateOnly? LastMadeOn);

/// <summary>Accounts payable (bills, debit notes, payments, payment runs, supplier details) and expenses, under <c>/api/finance</c>.</summary>
public static partial class Endpoints
{
    private static void MapPayables(RouteGroupBuilder api)
    {
        api.MapGet("/bills", BillsAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapGet("/bills/due", BillsDueAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapGet("/bills/{id:guid}", BillAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapPost("/bills", (BillRequest body, ApiRequest a) => SaveBillAsync(null, body, a)).RequireAuthorization(Need(Permissions.FinancePayables));
        api.MapPut("/bills/{id:guid}", (Guid id, BillRequest body, ApiRequest a) => SaveBillAsync(id, body, a)).RequireAuthorization(Need(Permissions.FinancePayables));
        api.MapDelete("/bills/{id:guid}", async (Guid id, ApiRequest a) =>
        {
            var bill = await BillInScopeAsync(id, a);
            await new PayablesService(await a.ForChangeAsync(bill.StoreId)).DeleteDraftAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.FinancePayables));
        api.MapPost("/bills/{id:guid}/post", async (Guid id, ApiRequest a) =>
        {
            var bill = await BillInScopeAsync(id, a);
            await new PayablesService(await a.ForChangeAsync(bill.StoreId)).PostBillAsync(id);
            return Results.Ok(await BillDetailAsync(id, a));
        }).RequireAuthorization(Need(Permissions.FinancePayables));
        api.MapPost("/bills/{id:guid}/void", async (Guid id, VoidRequest body, ApiRequest a) =>
        {
            var bill = await BillInScopeAsync(id, a);
            await new PayablesService(await a.ForChangeAsync(bill.StoreId)).VoidBillAsync(id, body.Date, body.Reason ?? "");
            return Results.Ok(await BillDetailAsync(id, a));
        }).RequireAuthorization(Need(Permissions.FinancePayables));
        api.MapPost("/bills/{id:guid}/attachments", async (Guid id, HttpRequest request, ApiRequest a) =>
        {
            var bill = await BillInScopeAsync(id, a);
            return await AttachAsync(AttachmentOwner.SupplierBill, id, bill.StoreId, request, a);
        }).RequireAuthorization(Need(Permissions.FinancePayables)).DisableAntiforgery();

        api.MapGet("/payments", PaymentsAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapGet("/payments/{id:guid}", PaymentAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapPost("/payments", PayAsync).RequireAuthorization(Need(Permissions.FinancePayables));
        api.MapPost("/payments/{id:guid}/void", async (Guid id, VoidRequest body, ApiRequest a) =>
        {
            var payment = await PaymentInScopeAsync(id, a);
            await new PayablesService(await a.ForChangeAsync(payment.StoreId)).VoidPaymentAsync(id, body.Date, body.Reason ?? "");
            return Results.Ok(await PaymentDetailAsync(id, a));
        }).RequireAuthorization(Need(Permissions.FinancePayables));
        api.MapGet("/payment-runs", PaymentRunsAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapPost("/payment-runs", PayRunAsync).RequireAuthorization(Need(Permissions.FinancePayables));
        api.MapGet("/withholding", WithholdingAsync).RequireAuthorization(Need(Permissions.FinanceView));

        api.MapGet("/suppliers/{id:guid}", SupplierFinanceAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapPut("/suppliers/{id:guid}", SaveSupplierFinanceAsync).RequireAuthorization(Need(Permissions.FinancePayables));
        api.MapGet("/suppliers/{id:guid}/statement", SupplierStatementAsync).RequireAuthorization(Need(Permissions.FinanceView));

        api.MapGet("/expenses", ExpensesAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapGet("/expenses/{id:guid}", ExpenseAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapPost("/expenses", (ExpenseRequest body, ApiRequest a) => SaveExpenseAsync(null, body, a)).RequireAuthorization(Need(Permissions.FinancePost));
        api.MapPut("/expenses/{id:guid}", (Guid id, ExpenseRequest body, ApiRequest a) => SaveExpenseAsync(id, body, a)).RequireAuthorization(Need(Permissions.FinancePost));
        api.MapDelete("/expenses/{id:guid}", async (Guid id, ApiRequest a) =>
        {
            var expense = await ExpenseInScopeAsync(id, a);
            await new ExpenseService(await a.ForChangeAsync(expense.StoreId)).DeleteDraftAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.FinancePost));
        api.MapPost("/expenses/{id:guid}/post", async (Guid id, ApiRequest a) =>
        {
            var expense = await ExpenseInScopeAsync(id, a);
            await new ExpenseService(await a.ForChangeAsync(expense.StoreId)).PostAsync(id);
            return Results.Ok(await ExpenseDetailAsync(id, a));
        }).RequireAuthorization(Need(Permissions.FinancePost));
        api.MapPost("/expenses/{id:guid}/void", async (Guid id, VoidRequest body, ApiRequest a) =>
        {
            var expense = await ExpenseInScopeAsync(id, a);
            await new ExpenseService(await a.ForChangeAsync(expense.StoreId)).VoidAsync(id, body.Date, body.Reason ?? "");
            return Results.Ok(await ExpenseDetailAsync(id, a));
        }).RequireAuthorization(Need(Permissions.FinancePost));
        api.MapPost("/expenses/{id:guid}/attachments", async (Guid id, HttpRequest request, ApiRequest a) =>
        {
            var expense = await ExpenseInScopeAsync(id, a);
            return await AttachAsync(AttachmentOwner.Expense, id, expense.StoreId, request, a);
        }).RequireAuthorization(Need(Permissions.FinancePost)).DisableAntiforgery();

        api.MapGet("/recurring-expenses", RecurringAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapPost("/recurring-expenses", (RecurringExpenseRow body, ApiRequest a) => SaveRecurringAsync(null, body, a)).RequireAuthorization(Need(Permissions.FinancePost));
        api.MapPut("/recurring-expenses/{id:guid}", (Guid id, RecurringExpenseRow body, ApiRequest a) => SaveRecurringAsync(id, body, a)).RequireAuthorization(Need(Permissions.FinancePost));
        api.MapDelete("/recurring-expenses/{id:guid}", async (Guid id, ApiRequest a) =>
        {
            var row = await RecurringInScopeAsync(id, a);
            await new ExpenseService(await a.ForChangeAsync(row.StoreId)).DeleteRecurringAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.FinancePost));
        api.MapPost("/recurring-expenses/run", async (ApiRequest a) =>
        {
            var store = a.ListStore();
            var made = await new ExpenseService(await a.ForChangeAsync(store)).RunDueAsync(await a.TodayAsync(store));
            return Results.Ok(new { made = made.Count });
        }).RequireAuthorization(Need(Permissions.FinancePost));
    }

    // Bills -----------------------------------------------------------------------------------------------------------

    private static async Task<IResult> BillsAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var today = await api.TodayAsync(store);
        await using var db = api.Root.Create();
        var q = db.SupplierBills.AsNoTracking().AsQueryable();
        if (store is Guid s) q = q.Where(b => b.StoreId == s);
        if (api.Id("supplierId") is Guid supplier) q = q.Where(b => b.SupplierId == supplier);
        if (api.Text("kind") is { } kind)
            q = Enum.TryParse<SupplierBillKind>(kind, true, out var k) ? q.Where(b => b.Kind == k) : throw ApiException.Bad("kind is Bill or DebitNote.");
        switch (api.Text("status")?.ToLowerInvariant())
        {
            case null: break;
            case "open": q = q.Where(b => b.Status == SupplierBillStatus.Posted || b.Status == SupplierBillStatus.PartPaid); break;
            case var other when Enum.TryParse<SupplierBillStatus>(other, true, out var st): q = q.Where(b => b.Status == st); break;
            default: throw ApiException.Bad("status is open, Draft, Posted, PartPaid, Paid or Void.");
        }
        if (api.Text("from") is not null || api.Text("to") is not null)
        {
            var (from, to) = await api.PeriodAsync(store);
            q = q.Where(b => b.BillDate >= from && b.BillDate <= to);
        }
        if (api.Text("search") is { } search)
        {
            long? number = long.TryParse(search, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
            q = q.Where(b => b.Number == number || (b.SupplierReference != null && b.SupplierReference.Contains(search))
                || (b.Supplier!.Company != null && b.Supplier.Company.Contains(search)));
        }
        var codes = await StoreCodesAsync(db);
        var rows = q.OrderByDescending(b => b.BillDate).ThenByDescending(b => b.Number).ThenByDescending(b => b.CreatedAtUtc)
            .Select(b => new { Bill = b, Supplier = b.Supplier!.Company ?? b.Supplier.LastName ?? "" });
        return Results.Ok(await api.PageAsync(rows, r => ToRow(r.Bill, r.Supplier, codes, today)));
    }

    private static BillListRow ToRow(SupplierBill b, string supplier, Dictionary<Guid, string> codes, DateOnly today) =>
        new(b.Id, b.Number, b.Kind, b.Status, b.SupplierId, supplier, b.StoreId, codes.GetValueOrDefault(b.StoreId) ?? "", b.BillDate, b.DueDate, b.SupplierReference, b.Currency,
            b.Total, b.Paid, b.Total - b.Paid,
            b.Kind == SupplierBillKind.Bill && b.Status is SupplierBillStatus.Posted or SupplierBillStatus.PartPaid && today > b.DueDate ? today.DayNumber - b.DueDate.DayNumber : 0);

    private static async Task<IResult> BillsDueAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var today = await api.TodayAsync(store);
        var until = DateOnly.TryParseExact(api.Text("until"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var u) ? u : today.AddDays(7);
        var bills = await new PayablesService(api.Root).BillsDueAsync(until, store);
        await using var db = api.Root.Create();
        var codes = await StoreCodesAsync(db);
        return Results.Ok(bills.Select(b => ToRow(b, b.Supplier?.Company ?? b.Supplier?.LastName ?? "", codes, today)));
    }

    private static async Task<SupplierBill> BillInScopeAsync(Guid id, ApiRequest api)
    {
        await using var db = api.Root.Create();
        var bill = await db.SupplierBills.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id) ?? throw ApiException.NotFound("That bill");
        api.RequireStore(bill.StoreId, "That bill");
        return bill;
    }

    private static async Task<IResult> BillAsync(Guid id, ApiRequest api)
    {
        await BillInScopeAsync(id, api);
        return Results.Ok(await BillDetailAsync(id, api));
    }

    private static async Task<BillDetail> BillDetailAsync(Guid id, ApiRequest api)
    {
        var bill = await new PayablesService(api.Root).GetBillAsync(id) ?? throw ApiException.NotFound("That bill");
        var attachments = await new ExpenseService(api.Root).AttachmentsAsync(AttachmentOwner.SupplierBill, id);
        await using var db = api.Root.Create();
        var codes = await StoreCodesAsync(db);
        var accounts = await db.LedgerAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Code);
        var payments = await db.SupplierPaymentAllocations.AsNoTracking().Where(a => a.SupplierBillId == id)
            .Join(db.SupplierPayments.AsNoTracking(), a => a.SupplierPaymentId, p => p.Id, (a, p) => new BillPaymentInfo(p.Id, p.Number, p.Date, a.Amount, p.Status))
            .ToListAsync();
        return new BillDetail(bill.Id, ApiRequest.Version(bill.Version), bill.Number, bill.Kind, bill.Status, bill.SupplierId, bill.Supplier?.Company ?? bill.Supplier?.LastName ?? "",
            bill.StoreId, codes.GetValueOrDefault(bill.StoreId) ?? "", bill.BillDate, bill.DueDate, bill.SupplierReference, bill.Currency, bill.ExchangeRate, bill.Notes, bill.Subtotal,
            bill.TaxTotal, bill.Total, bill.Paid, bill.Total - bill.Paid, bill.OriginalBillId, bill.ExpenseId, bill.JournalEntryId, bill.CreatedBy, bill.PostedBy,
            ApiRequest.Utc(bill.PostedAtUtc), bill.VoidReason,
            [.. bill.Lines.Select(l => new BillLineInfo(l.LineNumber, l.AccountId, l.AccountId is Guid a ? accounts.GetValueOrDefault(a) : null, l.ItemId, l.Description, l.Quantity,
                l.UnitPrice, l.Amount, l.TaxRate, l.TaxAmount, l.PurchaseOrderId, l.PurchaseOrderLineId))],
            [.. payments.OrderBy(p => p.Date).ThenBy(p => p.Number)], [.. attachments.Select(ToInfo)]);
    }

    private static async Task<IResult> SaveBillAsync(Guid? id, BillRequest body, ApiRequest api)
    {
        if (!api.User.CanSee(body.StoreId)) throw ApiException.Forbidden("You may not work for that store.");
        Guid version = Guid.Empty;
        if (id is Guid existing)
        {
            await BillInScopeAsync(existing, api);
            version = ApiRequest.ParseVersion(body.Version);
        }
        var saved = await new PayablesService(await api.ForChangeAsync(body.StoreId)).SaveBillAsync(new SupplierBill
        {
            Id = id ?? Guid.Empty, Version = version, Kind = body.Kind, SupplierId = body.SupplierId, StoreId = body.StoreId, BillDate = body.BillDate,
            DueDate = body.DueDate ?? default, SupplierReference = body.SupplierReference, Currency = body.Currency ?? "", ExchangeRate = body.ExchangeRate <= 0 ? 1 : body.ExchangeRate,
            Notes = body.Notes, OriginalBillId = body.OriginalBillId,
            Lines = [.. (body.Lines ?? []).Select(l => new SupplierBillLine
            {
                AccountId = l.AccountId, ItemId = l.ItemId, Description = l.Description ?? "", Quantity = l.Quantity, UnitPrice = l.UnitPrice, TaxRate = l.TaxRate, TaxAmount = l.TaxAmount,
                PurchaseOrderId = l.PurchaseOrderId, PurchaseOrderLineId = l.PurchaseOrderLineId,
            })],
        });
        var detail = await BillDetailAsync(saved.Id, api);
        return id is null ? Results.Created($"/api/finance/bills/{saved.Id}", detail) : Results.Ok(detail);
    }

    // Payments --------------------------------------------------------------------------------------------------------

    private static async Task<IResult> PaymentsAsync(ApiRequest api)
    {
        var store = api.ListStore();
        await using var db = api.Root.Create();
        var q = db.SupplierPayments.AsNoTracking().AsQueryable();
        if (store is Guid s) q = q.Where(p => p.StoreId == s);
        if (api.Id("supplierId") is Guid supplier) q = q.Where(p => p.SupplierId == supplier);
        if (api.Id("paymentRunId") is Guid run) q = q.Where(p => p.PaymentRunId == run);
        if (api.Text("from") is not null || api.Text("to") is not null)
        {
            var (from, to) = await api.PeriodAsync(store);
            q = q.Where(p => p.Date >= from && p.Date <= to);
        }
        var codes = await StoreCodesAsync(db);
        var names = await PaymentNamesAsync(db);
        return Results.Ok(await api.PageAsync(q.OrderByDescending(p => p.Date).ThenByDescending(p => p.Number), p => ToRow(p, names, codes)));
    }

    private sealed record PaymentNames(Dictionary<Guid, string> Suppliers, Dictionary<Guid, string> Banks);

    private static async Task<PaymentNames> PaymentNamesAsync(Data.RetailPlusDbContext db) => new(
        await db.Suppliers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Company ?? ((s.FirstName ?? "") + " " + (s.LastName ?? "")).Trim()),
        await db.BankAccounts.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name));

    private static PaymentListRow ToRow(SupplierPayment p, PaymentNames names, Dictionary<Guid, string> codes) =>
        new(p.Id, p.Number, p.Date, p.SupplierId, names.Suppliers.GetValueOrDefault(p.SupplierId) ?? "", codes.GetValueOrDefault(p.StoreId) ?? "", p.BankAccountId,
            names.Banks.GetValueOrDefault(p.BankAccountId) ?? "", p.Currency, p.Amount, p.WithholdingAmount, p.Reference, p.Status, p.PaymentRunId);

    private static async Task<SupplierPayment> PaymentInScopeAsync(Guid id, ApiRequest api)
    {
        await using var db = api.Root.Create();
        var payment = await db.SupplierPayments.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id) ?? throw ApiException.NotFound("That payment");
        api.RequireStore(payment.StoreId, "That payment");
        return payment;
    }

    private static async Task<IResult> PaymentAsync(Guid id, ApiRequest api)
    {
        await PaymentInScopeAsync(id, api);
        return Results.Ok(await PaymentDetailAsync(id, api));
    }

    private static async Task<PaymentDetail> PaymentDetailAsync(Guid id, ApiRequest api)
    {
        var p = await new PayablesService(api.Root).GetPaymentAsync(id) ?? throw ApiException.NotFound("That payment");
        await using var db = api.Root.Create();
        var codes = await StoreCodesAsync(db);
        var names = await PaymentNamesAsync(db);
        var billIds = p.Allocations.Select(a => a.SupplierBillId).ToList();
        var bills = await db.SupplierBills.AsNoTracking().Where(b => billIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id);
        return new PaymentDetail(p.Id, p.Number, p.Date, p.SupplierId, names.Suppliers.GetValueOrDefault(p.SupplierId) ?? "", p.StoreId, codes.GetValueOrDefault(p.StoreId) ?? "",
            p.BankAccountId, names.Banks.GetValueOrDefault(p.BankAccountId) ?? "", p.Currency, p.ExchangeRate, p.Amount, p.WithholdingAmount, p.WithholdingKind, p.WithholdingRate,
            p.Reference, p.Notes, p.Status, p.PaymentRunId, p.JournalEntryId,
            [.. p.Allocations.Select(a => new PaymentAllocationInfo(a.SupplierBillId, bills.GetValueOrDefault(a.SupplierBillId)?.Number ?? 0,
                bills.GetValueOrDefault(a.SupplierBillId)?.Kind ?? SupplierBillKind.Bill, a.Amount))]);
    }

    private static async Task CheckBillsAsync(ApiRequest api, IEnumerable<BillToPayRequest> bills)
    {
        var ids = bills.Select(b => b.BillId).ToList();
        await using var db = api.Root.Create();
        var stores = await db.SupplierBills.AsNoTracking().Where(b => ids.Contains(b.Id)).Select(b => b.StoreId).Distinct().ToListAsync();
        if (stores.Any(s => !api.User.CanSee(s))) throw ApiException.NotFound("A bill");
    }

    private static async Task<IResult> PayAsync(PaymentRequest body, ApiRequest api)
    {
        await CheckBillsAsync(api, body.Bills ?? []);
        await CheckBankAsync(api, body.BankAccountId);
        var store = await FirstBillStoreAsync(api, body.Bills ?? []);
        var payment = await new PayablesService(await api.ForChangeAsync(store)).PayAsync(new PaymentDraft(body.BankAccountId, body.Date,
            [.. (body.Bills ?? []).Select(b => new BillToPay(b.BillId, b.Amount))])
        {
            WithholdingAmount = body.WithholdingAmount, ExchangeRate = body.ExchangeRate <= 0 ? 1 : body.ExchangeRate, Reference = body.Reference, Notes = body.Notes,
        });
        return Results.Created($"/api/finance/payments/{payment.Id}", await PaymentDetailAsync(payment.Id, api));
    }

    private static async Task<Guid?> FirstBillStoreAsync(ApiRequest api, IEnumerable<BillToPayRequest> bills)
    {
        var first = bills.Select(b => b.BillId).FirstOrDefault();
        await using var db = api.Root.Create();
        return await db.SupplierBills.AsNoTracking().Where(b => b.Id == first).Select(b => (Guid?)b.StoreId).FirstOrDefaultAsync();
    }

    private static async Task<IResult> PaymentRunsAsync(ApiRequest api)
    {
        await using var db = api.Root.Create();
        var runs = await db.PaymentRuns.AsNoTracking().OrderByDescending(r => r.Date).ThenByDescending(r => r.Number).Take(200).ToListAsync();
        var store = api.ListStore();
        if (store is Guid s)
        {
            var mine = await db.SupplierPayments.AsNoTracking().Where(p => p.StoreId == s && p.PaymentRunId != null).Select(p => p.PaymentRunId!.Value).Distinct().ToListAsync();
            runs = runs.Where(r => mine.Contains(r.Id)).ToList();
        }
        return Results.Ok(runs.Select(ToInfo));
    }

    private static PaymentRunInfo ToInfo(PaymentRun r) => new(r.Id, r.Number, r.Date, r.BankAccountId, r.Total, r.Payments, r.Notes, r.PaidBy, ApiRequest.Utc(r.PaidAtUtc));

    private static async Task<IResult> PayRunAsync(PaymentRequest body, ApiRequest api)
    {
        await CheckBillsAsync(api, body.Bills ?? []);
        await CheckBankAsync(api, body.BankAccountId);
        var store = await FirstBillStoreAsync(api, body.Bills ?? []);
        var (run, payments) = await new PayablesService(await api.ForChangeAsync(store)).PayRunAsync(body.BankAccountId, body.Date,
            [.. (body.Bills ?? []).Select(b => new BillToPay(b.BillId, b.Amount))], body.Reference, body.ExchangeRate <= 0 ? 1 : body.ExchangeRate, body.Notes);
        await using var db = api.Root.Create();
        var codes = await StoreCodesAsync(db);
        var names = await PaymentNamesAsync(db);
        return Results.Ok(new PaymentRunResult(ToInfo(run), [.. payments.Select(p => ToRow(p, names, codes))]));
    }

    private static async Task<IResult> WithholdingAsync(ApiRequest api)
    {
        var supplier = api.Id("supplierId") ?? throw ApiException.Bad("Choose the supplier (supplierId).");
        var store = await RequiredStoreAsync(api, api.AskedStore);
        var q = await new FinanceSetupService(api.Root).WithholdingForAsync(supplier, store);
        return Results.Ok(new WithholdingInfo(q.Kind, q.Rate, q.Threshold, q.Section, q.RuleSet));
    }

    // Suppliers -------------------------------------------------------------------------------------------------------

    private static async Task<IResult> SupplierFinanceAsync(Guid id, ApiRequest api)
    {
        await using (var db = api.Root.Create())
            if (!await db.Suppliers.AnyAsync(s => s.Id == id)) throw ApiException.NotFound("That supplier");
        var s = await new FinanceSetupService(api.Root).GetSupplierAsync(id);
        return Results.Ok(new SupplierFinanceInfo(id, s.TaxNumber, s.SalesTaxNumber, s.IsFiler, s.WithholdingKind, s.WithholdingExempt, s.PaymentTermsDays, s.DefaultAccountId));
    }

    private static async Task<IResult> SaveSupplierFinanceAsync(Guid id, SupplierFinanceInfo body, ApiRequest api)
    {
        await api.RequireCompanyChangeAsync();
        await using (var db = api.Root.Create())
            if (!await db.Suppliers.AnyAsync(s => s.Id == id)) throw ApiException.NotFound("That supplier");
        await new FinanceSetupService(await api.ForChangeAsync()).SaveSupplierAsync(new SupplierFinance
        {
            SupplierId = id, TaxNumber = body.TaxNumber, SalesTaxNumber = body.SalesTaxNumber, IsFiler = body.IsFiler, WithholdingKind = body.WithholdingKind,
            WithholdingExempt = body.WithholdingExempt, PaymentTermsDays = body.PaymentTermsDays, DefaultAccountId = body.DefaultAccountId,
        });
        return await SupplierFinanceAsync(id, api);
    }

    private static async Task<IResult> SupplierStatementAsync(Guid id, ApiRequest api)
    {
        var store = api.ListStore();
        var (from, to) = await api.PeriodAsync(store);
        var s = await new PayablesService(api.Root).StatementAsync(id, from, to, store);
        return Results.Ok(new SupplierStatementInfo(s.SupplierId, s.Supplier, s.From, s.To, s.Opening, s.Closing,
            [.. s.Lines.Select(l => new StatementLineInfo(l.Date, l.EntryNumber, l.Kind, l.Memo, l.Reference, l.Billed, l.Paid, l.Balance))]));
    }

    // Expenses --------------------------------------------------------------------------------------------------------

    private static async Task<IResult> ExpensesAsync(ApiRequest api)
    {
        var store = api.ListStore();
        await using var db = api.Root.Create();
        var q = db.Expenses.AsNoTracking().AsQueryable();
        if (store is Guid s) q = q.Where(e => e.StoreId == s);
        if (api.Text("from") is not null || api.Text("to") is not null)
        {
            var (from, to) = await api.PeriodAsync(store);
            q = q.Where(e => e.Date >= from && e.Date <= to);
        }
        if (api.Text("status") is { } status)
            q = Enum.TryParse<ExpenseStatus>(status, true, out var st) ? q.Where(e => e.Status == st) : throw ApiException.Bad("status is Draft, Posted or Void.");
        if (api.Text("search") is { } search)
        {
            long? number = long.TryParse(search, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
            q = q.Where(e => e.Number == number || e.Payee.Contains(search) || (e.Description != null && e.Description.Contains(search)));
        }
        var lookups = await ExpenseLookupsAsync(db);
        return Results.Ok(await api.PageAsync(q.OrderByDescending(e => e.Date).ThenByDescending(e => e.Number).ThenByDescending(e => e.CreatedAtUtc), e => ToRow(e, lookups)));
    }

    private sealed record ExpenseLookups(Dictionary<Guid, string> Stores, Dictionary<Guid, (string Code, string Name)> Accounts, Dictionary<Guid, int> Attachments);

    private static async Task<ExpenseLookups> ExpenseLookupsAsync(Data.RetailPlusDbContext db) => new(
        await StoreCodesAsync(db),
        (await db.LedgerAccounts.AsNoTracking().Select(a => new { a.Id, a.Code, a.Name }).ToListAsync()).ToDictionary(a => a.Id, a => (a.Code, a.Name)),
        (await db.FinanceAttachments.AsNoTracking().Where(a => a.OwnerKind == AttachmentOwner.Expense).GroupBy(a => a.OwnerId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync())
            .ToDictionary(a => a.Key, a => a.Count));

    private static ExpenseRow ToRow(Expense e, ExpenseLookups l)
    {
        var account = l.Accounts.GetValueOrDefault(e.AccountId);
        return new ExpenseRow(e.Id, ApiRequest.Version(e.Version), e.Number, e.Date, e.StoreId, l.Stores.GetValueOrDefault(e.StoreId) ?? "", e.Payee, e.SupplierId, e.AccountId,
            account.Code ?? "", account.Name ?? "", e.Description, e.Amount, e.TaxAmount, e.Amount + e.TaxAmount, e.PaidBy, e.BankAccountId, e.Reference, e.Status, e.SupplierBillId,
            e.RecurringExpenseId, e.JournalEntryId, l.Attachments.GetValueOrDefault(e.Id));
    }

    private static async Task<Expense> ExpenseInScopeAsync(Guid id, ApiRequest api)
    {
        await using var db = api.Root.Create();
        var expense = await db.Expenses.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id) ?? throw ApiException.NotFound("That expense");
        api.RequireStore(expense.StoreId, "That expense");
        return expense;
    }

    private static async Task<IResult> ExpenseAsync(Guid id, ApiRequest api)
    {
        await ExpenseInScopeAsync(id, api);
        return Results.Ok(await ExpenseDetailAsync(id, api));
    }

    private static async Task<ExpenseDetail> ExpenseDetailAsync(Guid id, ApiRequest api)
    {
        var expense = await new ExpenseService(api.Root).GetAsync(id) ?? throw ApiException.NotFound("That expense");
        var attachments = await new ExpenseService(api.Root).AttachmentsAsync(AttachmentOwner.Expense, id);
        await using var db = api.Root.Create();
        return new ExpenseDetail(ToRow(expense, await ExpenseLookupsAsync(db)), [.. attachments.Select(ToInfo)]);
    }

    private static async Task<IResult> SaveExpenseAsync(Guid? id, ExpenseRequest body, ApiRequest api)
    {
        if (!api.User.CanSee(body.StoreId)) throw ApiException.Forbidden("You may not work for that store.");
        await CheckBankAsync(api, body.BankAccountId);
        Guid version = Guid.Empty;
        if (id is Guid existing)
        {
            await ExpenseInScopeAsync(existing, api);
            version = ApiRequest.ParseVersion(body.Version);
        }
        var service = new ExpenseService(await api.ForChangeAsync(body.StoreId));
        var saved = await service.SaveAsync(new Expense
        {
            Id = id ?? Guid.Empty, Version = version, Date = body.Date, StoreId = body.StoreId, Payee = body.Payee ?? "", SupplierId = body.SupplierId, AccountId = body.AccountId,
            Description = body.Description, Amount = body.Amount, TaxAmount = body.TaxAmount, PaidBy = body.PaidBy, BankAccountId = body.BankAccountId, Reference = body.Reference,
        });
        if (body.Post) await service.PostAsync(saved.Id);
        var detail = await ExpenseDetailAsync(saved.Id, api);
        return id is null ? Results.Created($"/api/finance/expenses/{saved.Id}", detail) : Results.Ok(detail);
    }

    private static RecurringExpenseRow ToRow(RecurringExpense r) => new(r.Id, ApiRequest.Version(r.UpdatedAtUtc), r.Name, r.StoreId, r.Payee, r.SupplierId, r.AccountId, r.Amount,
        r.TaxAmount, r.PaidBy, r.BankAccountId, r.Frequency, r.NextDate, r.EndDate, r.PostAutomatically, r.IsActive, r.LastMadeOn);

    private static async Task<IResult> RecurringAsync(ApiRequest api) =>
        Results.Ok((await new ExpenseService(api.Root).GetRecurringAsync(api.ListStore())).Select(ToRow));

    private static async Task<RecurringExpense> RecurringInScopeAsync(Guid id, ApiRequest api)
    {
        await using var db = api.Root.Create();
        var row = await db.RecurringExpenses.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id) ?? throw ApiException.NotFound("That recurring expense");
        api.RequireStore(row.StoreId, "That recurring expense");
        return row;
    }

    private static async Task<IResult> SaveRecurringAsync(Guid? id, RecurringExpenseRow body, ApiRequest api)
    {
        if (!api.User.CanSee(body.StoreId)) throw ApiException.Forbidden("You may not work for that store.");
        await CheckBankAsync(api, body.BankAccountId);
        if (id is Guid existing)
        {
            var stored = await RecurringInScopeAsync(existing, api);
            ApiRequest.CheckVersion(body.Version, ApiRequest.Version(stored.UpdatedAtUtc));
        }
        var saved = await new ExpenseService(await api.ForChangeAsync(body.StoreId)).SaveRecurringAsync(new RecurringExpense
        {
            Id = id ?? Guid.Empty, Name = body.Name ?? "", StoreId = body.StoreId, Payee = body.Payee ?? "", SupplierId = body.SupplierId, AccountId = body.AccountId, Amount = body.Amount,
            TaxAmount = body.TaxAmount, PaidBy = body.PaidBy, BankAccountId = body.BankAccountId, Frequency = body.Frequency, NextDate = body.NextDate, EndDate = body.EndDate,
            PostAutomatically = body.PostAutomatically, IsActive = body.IsActive,
        });
        await using var db = api.Root.Create();
        var row = ToRow(await db.RecurringExpenses.AsNoTracking().SingleAsync(r => r.Id == saved.Id));
        return id is null ? Results.Created($"/api/finance/recurring-expenses/{saved.Id}", row) : Results.Ok(row);
    }
}
