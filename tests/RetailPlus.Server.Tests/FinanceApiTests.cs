using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;
using RetailPlus.Sync.Tests;
using static RetailPlus.Server.Tests.BackOffice;

namespace RetailPlus.Server.Tests;

/// <summary>
/// The finance API (<c>/api/finance</c>) over the seeded company of <see cref="BackOffice"/>: every group works, refuses
/// users without its permission, keeps a store administrator to their own store (and away from what the company
/// shares), and answers 409 to an old version. Plus the finance jobs posting the tills' days, and a sale reported to the
/// (fake) FBR whose number reaches the till.
/// </summary>
public sealed class FinanceApiTests(BackOffice office) : IClassFixture<BackOffice>
{
    private void Require() => Skip.IfNot(office.Available, office.UnavailableReason);

    private string Day(int daysAgo) => office.Today.AddDays(-daysAgo).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task ExpectAsync(HttpStatusCode expected, Task<HttpResponseMessage> request)
    {
        using var response = await request;
        if (response.StatusCode != expected)
            Assert.Fail($"Expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    private static Task<HttpResponseMessage> Post<T>(HttpClient client, string url, T body) => client.PostAsJsonAsync(url, body, SyncWorld.Json);

    private static Task<HttpResponseMessage> Put<T>(HttpClient client, string url, T body) => client.PutAsJsonAsync(url, body, SyncWorld.Json);

    private static async Task<T> SendAsync<T>(Task<HttpResponseMessage> request)
    {
        using var response = await request;
        return await ReadAsync<T>(response);
    }

    private static async Task<Guid> AccountAsync(HttpClient client, string code) =>
        (await GetAsync<List<FinanceAccountRow>>(client, "/api/finance/accounts")).Single(a => a.Code == code).Id;

    private async Task<Guid> SupplierAsync(string name)
    {
        var supplier = await new PartyService(office.World.ServerData).SaveSupplierAsync(new Supplier { Company = name + " " + Guid.NewGuid().ToString("N")[..5] });
        return supplier.Id;
    }

    // Home, posting days, suspense ------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Posting_days_fills_the_ledger_per_store_and_the_home_page_shows_it()
    {
        Require();
        using var super = await office.SignInAsync();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var viewer = await office.SignInAsync(office.ViewerA);

        await ExpectAsync(HttpStatusCode.Forbidden, viewer.GetAsync("/api/finance/home"));
        await ExpectAsync(HttpStatusCode.Forbidden, Post(adminA, "/api/finance/post-days", new PostDaysRequest(office.Today.AddDays(-5), office.Today, office.StoreB.Id)));

        var posted = await SendAsync<PostDaysResult>(Post(adminA, "/api/finance/post-days", new PostDaysRequest(office.Today.AddDays(-5), office.Today, null)));
        Assert.Empty(posted.Problems);
        await SendAsync<PostDaysResult>(Post(super, "/api/finance/post-days", new PostDaysRequest(office.Today.AddDays(-5), office.Today, null)));

        var journals = await GetAsync<PageOf<JournalListRow>>(adminA, $"/api/finance/journals?kind=Daily&from={Day(5)}&to={Day(0)}");
        Assert.NotEmpty(journals.Items);
        Assert.All(journals.Items, j => Assert.Equal(office.StoreA.Code, j.StoreCode));
        Assert.Contains((await GetAsync<PageOf<JournalListRow>>(super, $"/api/finance/journals?kind=Daily&from={Day(5)}&to={Day(0)}&pageSize=500")).Items,
            j => j.StoreCode == office.StoreB.Code);
        await ExpectAsync(HttpStatusCode.Forbidden, adminA.GetAsync($"/api/finance/journals?storeId={office.StoreB.Id}"));
        var bDay = (await GetAsync<PageOf<JournalListRow>>(super, $"/api/finance/journals?kind=Daily&storeId={office.StoreB.Id}&from={Day(5)}&to={Day(0)}")).Items[0];
        await ExpectAsync(HttpStatusCode.NotFound, adminA.GetAsync($"/api/finance/journals/{bDay.Id}"));
        var detail = await GetAsync<JournalDetail>(super, $"/api/finance/journals/{bDay.Id}");
        Assert.Equal(detail.Lines.Sum(l => l.Debit), detail.Lines.Sum(l => l.Credit));
        // The tills' cash sales reach Cash in hand through the standard mapping a new store is given.
        Assert.Contains(detail.Lines, l => l.Code == "1010" && l.Debit > 0);

        // The home page's figures are the ledger's (other tests here spend and deposit the cash too).
        var home = await GetAsync<FinanceHomeInfo>(adminA, "/api/finance/home");
        var cash = (await GetAsync<List<FinanceAccountRow>>(adminA, "/api/finance/accounts")).Single(a => a.Code == "1010");
        Assert.Equal(cash.Balance, home.CashInHand);
        Assert.True(home.MonthIncome > 0 || office.Today.Day <= 4);
        var suspense = await GetAsync<List<SuspenseRow>>(super, "/api/finance/suspense");
        Assert.All(suspense, s => Assert.True(s.Debit != 0 || s.Credit != 0));

        var csv = await adminA.GetStringAsync($"/api/finance/daily-journal?from={Day(5)}&to={Day(0)}");
        Assert.Contains("Date,Journal,Store,Account", csv, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task The_finance_jobs_post_the_days_a_tills_package_brings()
    {
        Require();
        var jobs = office.World.Server.Services.GetRequiredService<FinanceJobs>();
        await jobs.RunOnceAsync(DateTime.Now);   // the first run marks where to start
        var session = await office.A2.SessionAsync();
        session.UserId = office.CashierA.Id;
        session.UserName = office.CashierA.UserName;
        var item = (await office.A2.Inventory.GetAsync(office.ItemA1.Id))!;
        await office.A2.Sales.PostSaleAsync(Draft(item, 1), [new TenderEntry("Cash", TenderKind.Cash, 10m, 10m)], session);
        await office.A2.SyncAsync();
        await jobs.RunOnceAsync(DateTime.Now);
        await using var db = office.World.ServerData.Create();
        Assert.True(await db.JournalEntries.AnyAsync(e => e.Source == $"daily:{office.StoreA.Id:N}:{office.Today:yyyy-MM-dd}"));
    }

    // Chart, settings, rules, mapping ---------------------------------------------------------------------------------

    [SkippableFact]
    public async Task The_chart_and_settings_are_changed_for_the_company_with_versions()
    {
        Require();
        using var super = await office.SignInAsync();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var reporter = await office.SignInAsync(office.ReporterA);
        await ExpectAsync(HttpStatusCode.Forbidden, reporter.GetAsync("/api/finance/accounts"));

        var accounts = await GetAsync<List<FinanceAccountRow>>(adminA, "/api/finance/accounts");
        var expenses = accounts.Single(a => a.Code == "6000");
        var request = new FinanceAccountRequest("6" + Random.Shared.Next(700, 899).ToString(CultureInfo.InvariantCulture), "Security", AccountType.Expense, expenses.Id, true, true,
            null, "Guards", CashFlowSection.Operating, null);
        // Two stores: a store administrator may not change what the company shares.
        await ExpectAsync(HttpStatusCode.Forbidden, Post(adminA, "/api/finance/accounts", request));
        var created = await SendAsync<FinanceAccountRow>(Post(super, "/api/finance/accounts", request));
        Assert.Equal((expenses.Id, CashFlowSection.Operating), (created.ParentId!.Value, created.CashFlow!.Value));
        var moved = await SendAsync<FinanceAccountRow>(Put(super, $"/api/finance/accounts/{created.Id}", request with { Name = "Security guards", Version = created.Version }));
        await ExpectAsync(HttpStatusCode.Conflict, Put(super, $"/api/finance/accounts/{created.Id}", request with { Name = "Old", Version = created.Version }));
        await ExpectAsync(HttpStatusCode.BadRequest, Put(super, $"/api/finance/accounts/{created.Id}", request with { ParentId = created.Id, Version = moved.Version }));
        await ExpectAsync(HttpStatusCode.NoContent, super.DeleteAsync($"/api/finance/accounts/{created.Id}"));

        var settings = await GetAsync<FinanceSettingsInfo>(adminA, "/api/finance/settings");
        Assert.Equal(("PKR", 7, "PK"), (settings.BaseCurrency, settings.FiscalYearStartMonth, settings.Country));
        await ExpectAsync(HttpStatusCode.Forbidden, Put(adminA, "/api/finance/settings", settings with { NightlyDays = 10 }));
        var saved = await SendAsync<FinanceSettingsInfo>(Put(super, "/api/finance/settings", settings with { NightlyDays = 10 }));
        Assert.Equal(10, saved.NightlyDays);
        await ExpectAsync(HttpStatusCode.Conflict, Put(super, "/api/finance/settings", settings with { NightlyDays = 9 }));
        await SendAsync<FinanceSettingsInfo>(Put(super, "/api/finance/settings", saved with { NightlyDays = 7 }));

        var rules = await GetAsync<List<TaxRuleSetInfo>>(adminA, "/api/finance/tax-rules");
        var pk = rules.Single(r => r.Name == FinanceSetupService.PakistanRuleSet);
        var copy = await SendAsync<TaxRuleSetInfo>(Post(super, "/api/finance/tax-rules", pk with { Id = null, Version = null, Name = "Test " + Guid.NewGuid().ToString("N")[..5], Country = "AE" }));
        await ExpectAsync(HttpStatusCode.Conflict, Put(super, $"/api/finance/tax-rules/{copy.Id}", copy with { Version = "1" }));
        var store = await GetAsync<StoreFinanceInfo>(adminA, $"/api/finance/stores/{office.StoreA.Id}");
        Assert.Equal(FinanceSetupService.PakistanRuleSet, store.RuleSet);
        await ExpectAsync(HttpStatusCode.NotFound, adminA.GetAsync($"/api/finance/stores/{office.StoreB.Id}"));
        await ExpectAsync(HttpStatusCode.NoContent, super.DeleteAsync($"/api/finance/tax-rules/{copy.Id}"));

        var mapping = await GetAsync<List<MappingRow>>(adminA, "/api/finance/mapping");
        Assert.Equal("4010", mapping.Single(m => m.Kind == AccountMappingKind.Sales).AccountCode);
        var changed = mapping.Select(m => m.Kind == AccountMappingKind.Rounding ? m with { AccountCode = "4900" } : m).ToList();
        var after = await SendAsync<List<MappingRow>>(Put(adminA, "/api/finance/mapping", changed));
        Assert.Equal("4900", after.Single(m => m.Kind == AccountMappingKind.Rounding).AccountCode);
        await ExpectAsync(HttpStatusCode.BadRequest, Put(adminA, "/api/finance/mapping", mapping.Select(m => m.Kind == AccountMappingKind.Rounding ? m with { AccountCode = "4000" } : m).ToList()));
        await SendAsync<List<MappingRow>>(Put(adminA, "/api/finance/mapping", mapping));
        await ExpectAsync(HttpStatusCode.Forbidden, adminA.GetAsync($"/api/finance/mapping?storeId={office.StoreB.Id}"));
    }

    // Payables --------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Bills_are_entered_posted_and_paid_by_the_store_they_belong_to()
    {
        Require();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var adminB = await office.SignInAsync(office.AdminB);
        using var reporter = await office.SignInAsync(office.ReporterA);
        var supplier = await SupplierAsync("Web bills");
        var rent = await AccountAsync(adminA, "6100");
        var bank = await SendAsync<BankAccountRow>(Post(adminA, "/api/finance/bank-accounts",
            new BankAccountRequest("A bank " + Guid.NewGuid().ToString("N")[..5], BankAccountKind.Bank, "HBL", "001", null, null, null, office.StoreA.Id, 0, null, true)));
        Assert.Equal(office.StoreA.Code, bank.StoreCode);

        var request = new BillRequest(SupplierBillKind.Bill, supplier, office.StoreA.Id, office.Today, null, "SUP-1", null, 1, null, null,
            [new BillLineRequest(rent, null, "Shop rent", 1, 100_000m, 0)]);
        await ExpectAsync(HttpStatusCode.Forbidden, Post(reporter, "/api/finance/bills", request));
        await ExpectAsync(HttpStatusCode.Forbidden, Post(adminB, "/api/finance/bills", request));
        var draft = await SendAsync<BillDetail>(Post(adminA, "/api/finance/bills", request));
        Assert.Equal((SupplierBillStatus.Draft, 100_000m, office.Today.AddDays(30)), (draft.Status, draft.Total, draft.DueDate));
        var edited = await SendAsync<BillDetail>(Put(adminA, $"/api/finance/bills/{draft.Id}", request with { Version = draft.Version, Notes = "July" }));
        await ExpectAsync(HttpStatusCode.Conflict, Put(adminA, $"/api/finance/bills/{draft.Id}", request with { Version = draft.Version }));
        await ExpectAsync(HttpStatusCode.NotFound, adminB.GetAsync($"/api/finance/bills/{draft.Id}"));
        var bill = await SendAsync<BillDetail>(adminA.PostAsync($"/api/finance/bills/{edited.Id}/post", null));
        Assert.Equal(SupplierBillStatus.Posted, bill.Status);
        Assert.Contains((await GetAsync<PageOf<BillListRow>>(adminA, "/api/finance/bills?status=open")).Items, b => b.Id == bill.Id);
        Assert.DoesNotContain((await GetAsync<PageOf<BillListRow>>(adminB, "/api/finance/bills")).Items, b => b.Id == bill.Id);
        Assert.Contains(await GetAsync<List<BillListRow>>(adminA, $"/api/finance/bills/due?until={office.Today.AddDays(40):yyyy-MM-dd}"), b => b.Id == bill.Id);

        var quote = await GetAsync<WithholdingInfo>(adminA, $"/api/finance/withholding?supplierId={supplier}");
        Assert.Equal(WithholdingKind.Goods, quote.Kind);
        await ExpectAsync(HttpStatusCode.NotFound, Post(adminB, "/api/finance/payments", new PaymentRequest(bank.Id, office.Today, [new BillToPayRequest(bill.Id, 100_000m)], null)));
        var payment = await SendAsync<PaymentDetail>(Post(adminA, "/api/finance/payments", new PaymentRequest(bank.Id, office.Today, [new BillToPayRequest(bill.Id, 100_000m)], null, Reference: "CHQ-77")));
        Assert.Equal(Money.Round(100_000m * quote.Rate / 100m), payment.WithholdingAmount);
        Assert.Equal(SupplierBillStatus.Paid, (await GetAsync<BillDetail>(adminA, $"/api/finance/bills/{bill.Id}")).Status);
        var statement = await GetAsync<SupplierStatementInfo>(adminA, $"/api/finance/suppliers/{supplier}/statement?from={Day(0)}&to={Day(0)}");
        Assert.Equal((2, 0m), (statement.Lines.Count, statement.Closing));
        var voided = await SendAsync<PaymentDetail>(Post(adminA, $"/api/finance/payments/{payment.Id}/void", new VoidRequest(office.Today, "Wrong amount")));
        Assert.Equal(SupplierPaymentStatus.Void, voided.Status);

        // A payment run, and a supplier's tax details (shared by the stores).
        var run = await SendAsync<PaymentRunResult>(Post(adminA, "/api/finance/payment-runs", new PaymentRequest(bank.Id, office.Today, [new BillToPayRequest(bill.Id, 50_000m)], 0m)));
        Assert.Equal((1, 50_000m), (run.Run.Payments, run.Run.Total));
        Assert.Contains(await GetAsync<List<PaymentRunInfo>>(adminA, "/api/finance/payment-runs"), r => r.Id == run.Run.Id);
        Assert.Equal(bill.Id, Assert.Single((await GetAsync<PaymentDetail>(adminA, $"/api/finance/payments/{payment.Id}")).Allocations).BillId);
        await ExpectAsync(HttpStatusCode.NotFound, adminB.GetAsync($"/api/finance/payments/{payment.Id}"));
        await ExpectAsync(HttpStatusCode.Forbidden, Put(adminA, $"/api/finance/suppliers/{supplier}", new SupplierFinanceInfo(supplier, "1234567-8", null, false, WithholdingKind.Services, false, 15, null)));
        using var super = await office.SignInAsync();
        var profile = await SendAsync<SupplierFinanceInfo>(Put(super, $"/api/finance/suppliers/{supplier}", new SupplierFinanceInfo(supplier, "1234567-8", null, false, WithholdingKind.Services, false, 15, null)));
        Assert.Equal((false, 15), (profile.IsFiler, profile.PaymentTermsDays));
    }

    // Expenses --------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Expenses_are_posted_with_their_receipt_and_recurring_ones_kept_per_store()
    {
        Require();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var adminB = await office.SignInAsync(office.AdminB);
        var account = await AccountAsync(adminA, "6200");
        var request = new ExpenseRequest(office.Today, office.StoreA.Id, "Plumber", null, account, "Leak", 500m, 0m, ExpensePaidBy.Cash, null, null);
        var draft = await SendAsync<ExpenseDetail>(Post(adminA, "/api/finance/expenses", request));
        await ExpectAsync(HttpStatusCode.Conflict, Put(adminA, $"/api/finance/expenses/{draft.Expense.Id}", request with { Version = Guid.NewGuid().ToString("N") }));
        await ExpectAsync(HttpStatusCode.NotFound, adminB.GetAsync($"/api/finance/expenses/{draft.Expense.Id}"));

        using (var form = new MultipartFormDataContent())
        {
            form.Add(new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4 receipt")), "file", "receipt.pdf");
            await ExpectAsync(HttpStatusCode.Created, adminA.PostAsync($"/api/finance/expenses/{draft.Expense.Id}/attachments", form));
        }
        using (var bad = new MultipartFormDataContent())
        {
            bad.Add(new ByteArrayContent([0x4D, 0x5A]), "file", "tool.exe");
            await ExpectAsync(HttpStatusCode.BadRequest, adminA.PostAsync($"/api/finance/expenses/{draft.Expense.Id}/attachments", bad));
        }
        var posted = await SendAsync<ExpenseDetail>(adminA.PostAsync($"/api/finance/expenses/{draft.Expense.Id}/post", null));
        Assert.Equal((ExpenseStatus.Posted, 1), (posted.Expense.Status, posted.Expense.Attachments));
        var file = Assert.Single(posted.Attachments);
        Assert.StartsWith("%PDF", await adminA.GetStringAsync($"/api/finance/attachments/{file.Id}"), StringComparison.Ordinal);
        await ExpectAsync(HttpStatusCode.NotFound, adminB.GetAsync($"/api/finance/attachments/{file.Id}"));
        Assert.Contains((await GetAsync<PageOf<ExpenseRow>>(adminA, $"/api/finance/expenses?from={Day(0)}&to={Day(0)}")).Items, e => e.Id == draft.Expense.Id);

        var recurring = await SendAsync<RecurringExpenseRow>(Post(adminA, "/api/finance/recurring-expenses", new RecurringExpenseRow(null, null, "Water", office.StoreA.Id, "Water board",
            null, account, 900m, 0m, ExpensePaidBy.Cash, null, RecurrenceFrequency.Monthly, office.Today.AddDays(20), null, false, true, null)));
        await ExpectAsync(HttpStatusCode.Conflict, Put(adminA, $"/api/finance/recurring-expenses/{recurring.Id}", recurring with { Version = "1" }));
        Assert.DoesNotContain(await GetAsync<List<RecurringExpenseRow>>(adminB, "/api/finance/recurring-expenses"), r => r.Id == recurring.Id);
        await ExpectAsync(HttpStatusCode.NotFound, adminB.DeleteAsync($"/api/finance/recurring-expenses/{recurring.Id}"));
        await ExpectAsync(HttpStatusCode.NoContent, adminA.DeleteAsync($"/api/finance/recurring-expenses/{recurring.Id}"));
    }

    // Bank ------------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task A_statement_is_uploaded_and_reconciled_and_till_cash_deposited()
    {
        Require();
        using var super = await office.SignInAsync();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var adminB = await office.SignInAsync(office.AdminB);
        var opening = office.Today.AddDays(-10);
        var request = new BankAccountRequest("Company bank " + Guid.NewGuid().ToString("N")[..5], BankAccountKind.Bank, "Bank " + Guid.NewGuid().ToString("N")[..5], "9", null, null,
            null, null, 20_000m, opening, true);
        await ExpectAsync(HttpStatusCode.Forbidden, Post(adminA, "/api/finance/bank-accounts", request));
        var bank = await SendAsync<BankAccountRow>(Post(super, "/api/finance/bank-accounts", request));
        Assert.Equal(20_000m, bank.Balance);
        await ExpectAsync(HttpStatusCode.Conflict, Put(super, $"/api/finance/bank-accounts/{bank.Id}", request with { Version = Guid.NewGuid().ToString("N") }));
        Assert.Contains(await GetAsync<List<BankAccountRow>>(adminA, "/api/finance/bank-accounts"), b => b.Id == bank.Id);

        var csv = $"Date,Description,Reference,Amount,Balance\n{opening:yyyy-MM-dd},Opening,,20000.00,20000.00\n{office.Today:yyyy-MM-dd},Charges,,-75.00,19925.00\n";
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "statement.csv");
            foreach (var (name, value) in new[] { ("dateColumn", "Date"), ("descriptionColumn", "Description"), ("referenceColumn", "Reference"), ("amountColumn", "Amount"), ("balanceColumn", "Balance") })
                form.Add(new StringContent(value), name);
            await ExpectAsync(HttpStatusCode.Forbidden, adminA.PostAsync($"/api/finance/bank-accounts/{bank.Id}/statements", form));
            var imported = await SendAsync<StatementImportResult>(super.PostAsync($"/api/finance/bank-accounts/{bank.Id}/statements", form));
            Assert.Equal((2, (decimal?)19_925m), (imported.Lines, imported.ClosingBalance));
        }
        Assert.Equal("Amount", (await GetAsync<StatementFormatInfo>(super, $"/api/finance/bank-accounts/{bank.Id}/statement-format")).AmountColumn);
        var matched = await SendAsync<Dictionary<string, int>>(super.PostAsync($"/api/finance/bank-accounts/{bank.Id}/auto-match", null));
        Assert.Equal(1, matched["matched"]);
        var rec = await GetAsync<ReconciliationInfo>(super, $"/api/finance/bank-accounts/{bank.Id}/reconciliation");
        Assert.Equal((1, -75m, 0m), (rec.UnmatchedLines, rec.UnmatchedStatement, rec.Difference!.Value));
        var charge = rec.StatementLines.Single(l => l.Status == StatementLineStatus.Unmatched);
        await SendAsync<JournalDetail>(Post(super, $"/api/finance/statement-lines/{charge.Id}/entry", new StatementEntryRequest(null, "Bank charges")));
        var after = await GetAsync<ReconciliationInfo>(super, $"/api/finance/bank-accounts/{bank.Id}/reconciliation");
        Assert.Equal((0, 19_925m), (after.UnmatchedLines, after.LedgerBalance));

        // A transfer to a store's cash box, and till cash deposited.
        var box = await SendAsync<BankAccountRow>(Post(adminA, "/api/finance/bank-accounts", new BankAccountRequest("A cash box " + Guid.NewGuid().ToString("N")[..4], BankAccountKind.Cash,
            null, null, null, null, null, office.StoreA.Id, 0, null, true)));
        await ExpectAsync(HttpStatusCode.NotFound, adminB.GetAsync($"/api/finance/bank-accounts/{box.Id}/reconciliation"));
        await SendAsync<BankTransferRow>(Post(adminA, "/api/finance/bank-transfers", new BankTransferRequest(bank.Id, box.Id, office.Today, 1_000m, null)));
        Assert.Contains(await GetAsync<List<BankTransferRow>>(adminA, $"/api/finance/bank-transfers?from={Day(0)}&to={Day(0)}"), t => t.ToAccountId == box.Id);
        var candidates = await GetAsync<List<DepositCandidateRow>>(adminA, $"/api/finance/deposits/candidates?from={Day(5)}&to={Day(0)}");
        Assert.All(candidates, c => Assert.Equal(office.StoreA.Id, c.StoreId));
        await SendAsync<DepositRow>(Post(adminA, "/api/finance/deposits", new DepositRequest(office.StoreA.Id, bank.Id, office.Today, 500m, null, "DEP-1")));
        await ExpectAsync(HttpStatusCode.Forbidden, Post(adminB, "/api/finance/deposits", new DepositRequest(office.StoreA.Id, bank.Id, office.Today, 500m, null, null)));
        Assert.Single(await GetAsync<List<DepositRow>>(adminA, $"/api/finance/deposits?from={Day(0)}&to={Day(0)}"), d => d.BankAccountId == bank.Id);
    }

    [SkippableFact]
    public async Task Statement_lines_are_matched_by_hand_or_left_out_and_a_tab_separated_file_is_read()
    {
        Require();
        using var super = await office.SignInAsync();
        using var adminA = await office.SignInAsync(office.AdminA);
        var opening = office.Today.AddDays(-3);
        var bank = await SendAsync<BankAccountRow>(Post(super, "/api/finance/bank-accounts", new BankAccountRequest("Tab bank " + Guid.NewGuid().ToString("N")[..5],
            BankAccountKind.Bank, "Tab bank " + Guid.NewGuid().ToString("N")[..5], "7", null, null, null, null, 1_000m, opening, true)));

        var tsv = $"Date\tDescription\tAmount\tBalance\n{opening:yyyy-MM-dd}\tOpening\t1000.00\t1000.00\n{office.Today:yyyy-MM-dd}\tFee\t-10.00\t990.00\n";
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(tsv)), "file", "statement.txt");
            foreach (var (name, value) in new[] { ("delimiter", "\t"), ("dateColumn", "Date"), ("descriptionColumn", "Description"), ("amountColumn", "Amount"), ("balanceColumn", "Balance") })
                form.Add(new StringContent(value), name);
            var imported = await SendAsync<StatementImportResult>(super.PostAsync($"/api/finance/bank-accounts/{bank.Id}/statements", form));
            Assert.Equal((2, (decimal?)990m), (imported.Lines, imported.ClosingBalance));
        }
        Assert.Equal("\t", (await GetAsync<StatementFormatInfo>(super, $"/api/finance/bank-accounts/{bank.Id}/statement-format")).Delimiter);

        var rec = await GetAsync<ReconciliationInfo>(super, $"/api/finance/bank-accounts/{bank.Id}/reconciliation");
        var first = rec.StatementLines.Single(l => l.Amount == 1_000m);
        var fee = rec.StatementLines.Single(l => l.Amount == -10m);
        var ledger = rec.LedgerLines.Single(l => l.Amount == 1_000m);
        // A company account's statement is matched by those who change what the company shares.
        await ExpectAsync(HttpStatusCode.Forbidden, Post(adminA, $"/api/finance/statement-lines/{first.Id}/match", new MatchRequest(ledger.JournalLineId)));
        await ExpectAsync(HttpStatusCode.NoContent, Post(super, $"/api/finance/statement-lines/{first.Id}/match", new MatchRequest(ledger.JournalLineId)));
        rec = await GetAsync<ReconciliationInfo>(super, $"/api/finance/bank-accounts/{bank.Id}/reconciliation");
        Assert.Equal(StatementLineStatus.Matched, rec.StatementLines.Single(l => l.Id == first.Id).Status);
        Assert.True(rec.LedgerLines.Single(l => l.JournalLineId == ledger.JournalLineId).Matched);
        await ExpectAsync(HttpStatusCode.NoContent, super.PostAsync($"/api/finance/statement-lines/{first.Id}/unmatch", null));
        await ExpectAsync(HttpStatusCode.NoContent, super.PostAsync($"/api/finance/statement-lines/{fee.Id}/ignore", null));
        rec = await GetAsync<ReconciliationInfo>(super, $"/api/finance/bank-accounts/{bank.Id}/reconciliation");
        Assert.Equal((StatementLineStatus.Unmatched, StatementLineStatus.Ignored, 1),
            (rec.StatementLines.Single(l => l.Id == first.Id).Status, rec.StatementLines.Single(l => l.Id == fee.Id).Status, rec.UnmatchedLines));
        await ExpectAsync(HttpStatusCode.NotFound, Post(super, $"/api/finance/statement-lines/{Guid.NewGuid()}/match", new MatchRequest(ledger.JournalLineId)));
    }

    [SkippableFact]
    public async Task Bills_and_expenses_are_voided_drafts_deleted_recurring_ones_made_and_budgets_copied()
    {
        Require();
        using var super = await office.SignInAsync();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var adminB = await office.SignInAsync(office.AdminB);
        var rent = await AccountAsync(adminA, "6100");
        var supplier = await SupplierAsync("Void bills");

        var bill = await SendAsync<BillDetail>(Post(adminA, "/api/finance/bills", new BillRequest(SupplierBillKind.Bill, supplier, office.StoreA.Id, office.Today, null, null, null, 1,
            null, null, [new BillLineRequest(rent, null, "Rent", 1, 800m, 0)])));
        bill = await SendAsync<BillDetail>(adminA.PostAsync($"/api/finance/bills/{bill.Id}/post", null));
        await ExpectAsync(HttpStatusCode.NotFound, Post(adminB, $"/api/finance/bills/{bill.Id}/void", new VoidRequest(office.Today, "Not ours")));
        await ExpectAsync(HttpStatusCode.BadRequest, Post(adminA, $"/api/finance/bills/{bill.Id}/void", new VoidRequest(office.Today, " ")));
        var voidBill = await SendAsync<BillDetail>(Post(adminA, $"/api/finance/bills/{bill.Id}/void", new VoidRequest(office.Today, "Entered twice")));
        Assert.Equal((SupplierBillStatus.Void, "Entered twice"), (voidBill.Status, voidBill.VoidReason));
        Assert.Equal(JournalStatus.Reversed, (await GetAsync<JournalDetail>(adminA, $"/api/finance/journals/{bill.JournalEntryId}")).Status);

        var expense = await SendAsync<ExpenseDetail>(Post(adminA, "/api/finance/expenses", new ExpenseRequest(office.Today, office.StoreA.Id, "Cleaner", null, rent, null, 150m, 0m,
            ExpensePaidBy.Cash, null, null, Post: true)));
        Assert.Equal(ExpenseStatus.Posted, expense.Expense.Status);
        var voidExpense = await SendAsync<ExpenseDetail>(Post(adminA, $"/api/finance/expenses/{expense.Expense.Id}/void", new VoidRequest(office.Today, "Paid by the landlord")));
        Assert.Equal(ExpenseStatus.Void, voidExpense.Expense.Status);

        var cash = await AccountAsync(super, "1010");
        var draft = await SendAsync<JournalDetail>(Post(super, "/api/finance/journals", new JournalRequest(office.Today, office.StoreA.Id, "To delete", null,
            [new JournalLineRequest(rent, 10m, 0), new JournalLineRequest(cash, 0, 10m)])));
        await ExpectAsync(HttpStatusCode.NotFound, adminB.DeleteAsync($"/api/finance/journals/{draft.Id}"));
        await ExpectAsync(HttpStatusCode.NoContent, super.DeleteAsync($"/api/finance/journals/{draft.Id}"));
        await ExpectAsync(HttpStatusCode.NotFound, super.GetAsync($"/api/finance/journals/{draft.Id}"));

        // A recurring expense due today is made when asked, by its own store only.
        var water = await SendAsync<RecurringExpenseRow>(Post(adminA, "/api/finance/recurring-expenses", new RecurringExpenseRow(null, null, "Water " + Guid.NewGuid().ToString("N")[..4],
            office.StoreA.Id, "Water board", null, rent, 90m, 0m, ExpensePaidBy.Cash, null, RecurrenceFrequency.Monthly, office.Today, null, false, true, null)));
        var gas = await SendAsync<RecurringExpenseRow>(Post(adminB, "/api/finance/recurring-expenses", new RecurringExpenseRow(null, null, "Gas " + Guid.NewGuid().ToString("N")[..4],
            office.StoreB.Id, "Gas company", null, rent, 60m, 0m, ExpensePaidBy.Cash, null, RecurrenceFrequency.Monthly, office.Today, null, false, true, null)));
        var made = await SendAsync<Dictionary<string, int>>(adminA.PostAsync("/api/finance/recurring-expenses/run", null));
        Assert.True(made["made"] >= 1);
        var after = (await GetAsync<List<RecurringExpenseRow>>(adminA, "/api/finance/recurring-expenses")).Single(r => r.Id == water.Id);
        Assert.Equal(office.Today, after.LastMadeOn!.Value);
        Assert.True(after.NextDate > office.Today);
        Assert.Equal(office.Today, (await GetAsync<List<RecurringExpenseRow>>(adminB, "/api/finance/recurring-expenses")).Single(r => r.Id == gas.Id).NextDate);
        await ExpectAsync(HttpStatusCode.NoContent, adminB.DeleteAsync($"/api/finance/recurring-expenses/{gas.Id}"));

        // A budget copied into the next year, raised by a tenth.
        await SendAsync<List<YearInfo>>(Post(super, "/api/finance/years", new NewYearRequest(new DateOnly(2064, 9, 1))));
        var years = await SendAsync<List<YearInfo>>(Post(super, "/api/finance/years", new NewYearRequest(new DateOnly(2065, 9, 1))));
        var first = years.Single(y => y.StartDate == new DateOnly(2064, 7, 1));
        var next = years.Single(y => y.StartDate == new DateOnly(2065, 7, 1));
        await SendAsync<BudgetInfo>(Put(super, $"/api/finance/budgets?yearId={first.Id}&storeId={office.StoreA.Id}",
            new BudgetSaveRequest([new BudgetAmountInfo(rent, new DateOnly(2064, 8, 1), 1_000m)])));
        await ExpectAsync(HttpStatusCode.Forbidden, Post(adminA, "/api/finance/budgets/copy", new BudgetCopyRequest(next.Id, office.StoreB.Id, true, 10m)));
        var copied = await SendAsync<Dictionary<string, int>>(Post(adminA, "/api/finance/budgets/copy", new BudgetCopyRequest(next.Id, office.StoreA.Id, true, 10m)));
        Assert.Equal(1, copied["copied"]);
        var budget = await GetAsync<BudgetInfo>(adminA, $"/api/finance/budgets?yearId={next.Id}&storeId={office.StoreA.Id}");
        Assert.Equal((new DateOnly(2065, 8, 1), 1_100m), (Assert.Single(budget.Amounts).Month, budget.Amounts[0].Amount));
    }

    // Journals, years, budgets ----------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task An_entry_made_by_hand_is_approved_by_someone_else_and_can_be_reversed()
    {
        Require();
        using var maker = await office.SignInAsync(await office.UserAsync(Permissions.AccountantRole, null));
        using var checker = await office.SignInAsync(await office.UserAsync(Permissions.AccountantRole, null));
        using var adminA = await office.SignInAsync(office.AdminA);
        var date = new DateOnly(2061, 8, 1);
        var bank = await AccountAsync(maker, "1050");
        var capital = await AccountAsync(maker, "3010");
        var request = new JournalRequest(date, office.StoreA.Id, "Owner's money in", "CAP-1", [new JournalLineRequest(bank, 5_000m, 0), new JournalLineRequest(capital, 0, 5_000m)]);
        await ExpectAsync(HttpStatusCode.BadRequest, Post(maker, "/api/finance/journals", request with { Lines = [new JournalLineRequest(bank, 5_000m, 0), new JournalLineRequest(capital, 0, 4_000m)] }));
        await ExpectAsync(HttpStatusCode.Forbidden, Post(adminA, "/api/finance/journals", request with { StoreId = null }));
        var draft = await SendAsync<JournalDetail>(Post(maker, "/api/finance/journals", request with { Post = true }));
        Assert.Equal((JournalStatus.Draft, true), (draft.Status, draft.NeedsApproval));
        var edited = await SendAsync<JournalDetail>(Put(maker, $"/api/finance/journals/{draft.Id}", request with { Memo = "Owner's capital", Version = draft.Version }));
        await ExpectAsync(HttpStatusCode.Conflict, Put(maker, $"/api/finance/journals/{draft.Id}", request with { Version = draft.Version }));
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 1, 2, 3]), "file", "deposit-slip.png");
            await ExpectAsync(HttpStatusCode.Created, maker.PostAsync($"/api/finance/journals/{draft.Id}/attachments", form));
        }
        await ExpectAsync(HttpStatusCode.BadRequest, maker.PostAsync($"/api/finance/journals/{draft.Id}/approve", null));
        var posted = await SendAsync<JournalDetail>(checker.PostAsync($"/api/finance/journals/{edited.Id}/approve", null));
        Assert.Equal((JournalStatus.Posted, 1), (posted.Status, posted.Attachments.Count));
        var reversal = await SendAsync<JournalDetail>(Post(checker, $"/api/finance/journals/{posted.Id}/reverse", new ReverseRequest(date, "Entered in the wrong year")));
        Assert.Equal((JournalKind.Reversal, posted.Id), (reversal.Kind, reversal.ReversalOfId!.Value));
        await ExpectAsync(HttpStatusCode.BadRequest, Post(checker, $"/api/finance/journals/{posted.Id}/reverse", new ReverseRequest(date, "Again")));
    }

    [SkippableFact]
    public async Task Years_are_locked_closed_and_opened_again_and_budgets_kept_per_store()
    {
        Require();
        using var super = await office.SignInAsync();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var viewer = await office.SignInAsync(office.ViewerA);
        var years = await SendAsync<List<YearInfo>>(Post(super, "/api/finance/years", new NewYearRequest(new DateOnly(2062, 9, 1))));
        var year = years.Single(y => y.StartDate == new DateOnly(2062, 7, 1));
        await ExpectAsync(HttpStatusCode.Forbidden, adminA.PostAsync($"/api/finance/periods/{year.Periods[0].Id}/lock", null));
        var locked = await SendAsync<List<YearInfo>>(super.PostAsync($"/api/finance/periods/{year.Periods[0].Id}/lock", null));
        Assert.Equal(FiscalStatus.Locked, locked.Single(y => y.Id == year.Id).Periods[0].Status);
        await SendAsync<List<YearInfo>>(super.PostAsync($"/api/finance/periods/{year.Periods[0].Id}/unlock", null));

        var rent = await AccountAsync(super, "6100");
        var cash = await AccountAsync(super, "1010");
        await SendAsync<JournalDetail>(Post(super, "/api/finance/opening-balances", new OpeningRequest(new DateOnly(2062, 7, 1), office.StoreA.Id, true,
            [new JournalLineRequest(cash, 3_000m, 0)])));
        var budget = await SendAsync<BudgetInfo>(Put(super, $"/api/finance/budgets?yearId={year.Id}&storeId={office.StoreA.Id}",
            new BudgetSaveRequest([new BudgetAmountInfo(rent, new DateOnly(2062, 8, 1), 2_000m)])));
        Assert.Equal((12, 2_000m), (budget.Months.Count, Assert.Single(budget.Amounts).Amount));
        await ExpectAsync(HttpStatusCode.Forbidden, Put(viewer, $"/api/finance/budgets?yearId={year.Id}&storeId={office.StoreA.Id}", new BudgetSaveRequest([])));
        await ExpectAsync(HttpStatusCode.Forbidden, Put(adminA, $"/api/finance/budgets?yearId={year.Id}&storeId={office.StoreB.Id}", new BudgetSaveRequest([])));
        await ExpectAsync(HttpStatusCode.NotFound, adminA.GetAsync($"/api/finance/budgets?yearId={year.Id}"));

        var closed = await SendAsync<List<YearInfo>>(super.PostAsync($"/api/finance/years/{year.Id}/close", null));
        Assert.Equal(FiscalStatus.Closed, closed.Single(y => y.Id == year.Id).Status);
        Assert.Contains(closed, y => y.StartDate == new DateOnly(2063, 7, 1));
        await ExpectAsync(HttpStatusCode.Forbidden, Post(adminA, $"/api/finance/years/{year.Id}/reopen", new ReasonRequest("Fix")));
        var reopened = await SendAsync<List<YearInfo>>(Post(super, $"/api/finance/years/{year.Id}/reopen", new ReasonRequest("A late bill")));
        Assert.Equal(FiscalStatus.Open, reopened.Single(y => y.Id == year.Id).Status);
    }

    // Reports, exports, FBR -------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Statements_and_exports_cover_a_store_or_the_company()
    {
        Require();
        using var super = await office.SignInAsync();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var viewer = await office.SignInAsync(office.ViewerA);
        await SendAsync<PostDaysResult>(Post(super, "/api/finance/post-days", new PostDaysRequest(office.Today.AddDays(-5), office.Today, null)));
        var catalog = await GetAsync<List<FinanceReportCatalogRow>>(adminA, "/api/finance/reports");
        Assert.Contains(catalog, r => r.Key == "balance-sheet" && r.AsOfDate);
        await ExpectAsync(HttpStatusCode.Forbidden, viewer.GetAsync("/api/finance/reports/trial-balance"));

        var tb = await GetAsync<ReportResult>(adminA, $"/api/finance/reports/trial-balance?from={Day(5)}&to={Day(0)}");
        Assert.Equal(((System.Text.Json.JsonElement)tb.Totals[4]!).GetDecimal(), ((System.Text.Json.JsonElement)tb.Totals[5]!).GetDecimal());
        foreach (var key in new[] { "profit-and-loss", "balance-sheet", "cash-flow", "general-ledger", "sales-tax", "receivables-ageing", "payables-ageing", "budget-vs-actual" })
            await ExpectAsync(HttpStatusCode.OK, super.GetAsync($"/api/finance/reports/{key}?from={Day(5)}&to={Day(0)}&compare=previous-year"));
        await ExpectAsync(HttpStatusCode.BadRequest, adminA.GetAsync("/api/finance/reports/account-ledger"));
        await ExpectAsync(HttpStatusCode.BadRequest, adminA.GetAsync("/api/finance/reports/profit-and-loss?compare=sometimes"));
        await ExpectAsync(HttpStatusCode.NotFound, adminA.GetAsync("/api/finance/reports/nothing"));
        await ExpectAsync(HttpStatusCode.Forbidden, adminA.GetAsync($"/api/finance/reports/trial-balance?storeId={office.StoreB.Id}"));
        var cash = await AccountAsync(adminA, "1010");
        var ledger = await GetAsync<ReportResult>(adminA, $"/api/finance/reports/account-ledger?accountId={cash}&from={Day(5)}&to={Day(0)}");
        Assert.True(ledger.Rows.Count > 1);
        using (var csv = await adminA.GetAsync($"/api/finance/reports/profit-and-loss/csv?from={Day(5)}&to={Day(0)}"))
        {
            Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
            Assert.StartsWith("Code,Account,Amount", (await csv.Content.ReadAsStringAsync()).TrimStart('﻿'), StringComparison.Ordinal);
        }

        Assert.Contains("<VOUCHER", await adminA.GetStringAsync($"/api/finance/export?format=tally&from={Day(5)}&to={Day(0)}"), StringComparison.Ordinal);
        Assert.StartsWith("!ACCNT", await adminA.GetStringAsync($"/api/finance/export?format=iif&from={Day(5)}&to={Day(0)}"), StringComparison.Ordinal);
        Assert.Contains("Date,Number,Kind", await adminA.GetStringAsync($"/api/finance/export?format=csv&from={Day(5)}&to={Day(0)}"), StringComparison.Ordinal);
        Assert.StartsWith("Journal No,Journal Date,Account,Debits,Credits", await adminA.GetStringAsync($"/api/finance/export?format=qbo&from={Day(5)}&to={Day(0)}"), StringComparison.Ordinal);
        await ExpectAsync(HttpStatusCode.BadRequest, adminA.GetAsync("/api/finance/export?format=pdf"));
        await ExpectAsync(HttpStatusCode.Forbidden, viewer.GetAsync("/api/finance/export?format=csv"));
    }

    [SkippableFact]
    public async Task A_sale_reported_to_the_tax_office_gets_its_number_on_the_web_and_at_the_till()
    {
        Require();
        using var adminA = await office.SignInAsync(office.AdminA);
        var fake = new FakeFiscalInvoiceSender();
        var fiscal = new FiscalService(office.World.ServerData);
        await fiscal.EnqueueAsync(fake);   // switched on now: only later sales go
        var session = await office.A1.SessionAsync();
        session.UserId = office.CashierA.Id;
        session.UserName = office.CashierA.UserName;
        var item = (await office.A1.Inventory.GetAsync(office.ItemA2.Id))!;
        var sale = await office.A1.Sales.PostSaleAsync(Draft(item, 1), [new TenderEntry("Cash", TenderKind.Cash, 25m, 25m)], session);
        await office.A1.SyncAsync();
        Assert.True(await fiscal.EnqueueAsync(fake) >= 1);
        Assert.True(await fiscal.SendDueAsync(fake) >= 1);

        var queue = await GetAsync<FiscalQueueInfo>(adminA, "/api/finance/fiscal");
        Assert.False(queue.Enabled);   // the server's own sender is off by default
        var row = queue.Rows.Single(r => r.SaleId == sale.Id);
        Assert.Equal(FiscalInvoiceStatus.Sent, row.Status);
        Assert.Equal(row.FiscalNumber, (await GetAsync<SaleDetail>(adminA, $"/api/sales/{sale.Id}")).FiscalInvoiceNumber);
        using var adminB = await office.SignInAsync(office.AdminB);
        Assert.DoesNotContain((await GetAsync<FiscalQueueInfo>(adminB, "/api/finance/fiscal")).Rows, r => r.SaleId == sale.Id);
        await ExpectAsync(HttpStatusCode.NotFound, adminB.PostAsync($"/api/finance/fiscal/{row.Id}/retry", null));

        // The number goes down to the till with the sale, for reprints.
        await office.A1.SyncAsync();
        await using var till = office.A1.Data.Create();
        Assert.Equal(row.FiscalNumber, (await till.Sales.AsNoTracking().SingleAsync(s => s.Id == sale.Id)).FiscalInvoiceNumber);
    }
}
