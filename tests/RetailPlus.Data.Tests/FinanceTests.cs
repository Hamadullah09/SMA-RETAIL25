using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;
using RetailPlus.Reports;

namespace RetailPlus.Data.Tests;

/// <summary>
/// Finance on top of the ledger: supplier bills, debit notes and payments with withholding tax, payment runs, ageing and
/// statements; expenses (cash, bank, on account, recurring) and their receipts; bank accounts, transfers, deposits,
/// statements and reconciliation; closing a year and opening balances; budgets; the statements and reports; the Tally and
/// QuickBooks exports; the FBR queue with its fake sender; and the automatic posting of business days. Each test works in
/// financial years of its own (2040 on), because the tests share one database.
/// </summary>
public abstract class FinanceTests(DatabaseFixture db)
{
    private DataContext Data
    {
        get
        {
            var data = db.Data;
            data.CurrentUser = new SessionInfo { UserId = Guid.NewGuid(), UserName = "accountant" };
            return data;
        }
    }

    private Guid Store => db.Station!.StoreId;

    private async Task SeedAsync()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        await new LedgerService(Data).SeedAsync();
        await new FinanceSetupService(Data).SeedAsync();
    }

    private async Task<Guid> AccountAsync(string code)
    {
        await using var ctx = db.CreateContext();
        return await ctx.LedgerAccounts.Where(a => a.Code == code).Select(a => a.Id).SingleAsync();
    }

    private async Task<Supplier> SupplierAsync(string name, bool filer = true, WithholdingKind kind = WithholdingKind.Goods)
    {
        var supplier = await new PartyService(Data).SaveSupplierAsync(new Supplier { Company = name + " " + Guid.NewGuid().ToString("N")[..5] });
        await new FinanceSetupService(Data).SaveSupplierAsync(new SupplierFinance { SupplierId = supplier.Id, IsFiler = filer, WithholdingKind = kind, PaymentTermsDays = 30 });
        return supplier;
    }

    private async Task<BankAccount> BankAsync(string currency = "", decimal opening = 0, DateOnly? on = null) =>
        await new BankService(Data).SaveAccountAsync(new BankAccount
        {
            Name = "Bank " + Guid.NewGuid().ToString("N")[..6], Kind = BankAccountKind.Bank, BankName = "Test bank " + Guid.NewGuid().ToString("N")[..4], Currency = currency,
            OpeningBalance = opening, OpeningDate = on,
        });

    private async Task<List<JournalLine>> LinesAsync(Guid? entryId)
    {
        await using var ctx = db.CreateContext();
        return await ctx.JournalLines.AsNoTracking().Include(l => l.Account).Where(l => l.JournalEntryId == entryId).ToListAsync();
    }

    private static decimal Dr(List<JournalLine> lines, string code) => lines.Where(l => l.Account!.Code == code).Sum(l => l.Debit - l.Credit);

    private static SupplierBillLine Line(Guid? account, decimal price, decimal rate = 0, decimal quantity = 1, Guid? item = null, Guid? po = null) =>
        new() { AccountId = account, ItemId = item, PurchaseOrderId = po, Description = "Line", Quantity = quantity, UnitPrice = price, TaxRate = rate };

    // Setup -----------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Pakistan_is_seeded_with_its_rules_and_the_chart_gains_the_new_roles_once()
    {
        await SeedAsync();
        await new FinanceSetupService(Data).SeedAsync();
        var sets = await new FinanceSetupService(Data).GetRuleSetsAsync();
        var pk = Assert.Single(sets, s => s.Name == FinanceSetupService.PakistanRuleSet);
        Assert.Equal(("PK", true), (pk.Country, pk.InputTaxClaimable));
        Assert.Equal(18m, pk.SalesTaxRates.Single(r => r.IsDefault).Rate);
        var goods = pk.WithholdingRules.Single(r => r.Kind == WithholdingKind.Goods);
        Assert.True(goods.NonFilerRate > goods.FilerRate);
        Assert.Contains("budget", pk.Notes!, StringComparison.OrdinalIgnoreCase);

        var accounts = await new LedgerService(Data).GetAccountsAsync();
        Assert.Single(accounts, a => a.Role == LedgerRoles.WithholdingTaxPayable);
        Assert.Equal("6400", accounts.Single(a => a.Role == LedgerRoles.BankCharges).Code);
        Assert.Equal(pk.Id, (await new FinanceSetupService(Data).RuleSetForStoreAsync(Store))!.Id);

        // A role account cannot be made inactive (the program posts to it).
        var cash = accounts.Single(a => a.Role == LedgerRoles.Cash);
        cash.IsActive = false;
        await Assert.ThrowsAsync<BusinessRuleException>(() => new LedgerService(Data).SaveAccountAsync(cash));
    }

    [SkippableFact]
    public async Task The_daily_mapping_takes_accounts_from_the_chart_and_the_csv_export_keeps_working()
    {
        await SeedAsync();
        var setup = new FinanceSetupService(Data);
        var rows = await new AccountingService(Data).MappingTableAsync();
        rows.Single(r => r.Kind == AccountMappingKind.Discounts).AccountCode = "4020";
        await setup.SaveMappingAsync(Store, rows);
        var saved = await new AccountingService(Data).GetMappingsAsync();
        Assert.Equal("Discounts given", saved.Single(m => m.Kind == AccountMappingKind.Discounts).AccountName);
        rows.Single(r => r.Kind == AccountMappingKind.Discounts).AccountCode = "6000";
        await Assert.ThrowsAsync<BusinessRuleException>(() => setup.SaveMappingAsync(Store, rows));
        rows.Single(r => r.Kind == AccountMappingKind.Discounts).AccountCode = "NOPE";
        await Assert.ThrowsAsync<BusinessRuleException>(() => setup.SaveMappingAsync(Store, rows));
        var csv = (await new AccountingService(Data).ExportAsync(new DateOnly(2040, 1, 1), new DateOnly(2040, 1, 1))).ToCsv();
        Assert.StartsWith("Date,Journal,Store,Account", csv, StringComparison.Ordinal);
    }

    // Payables --------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task A_bill_posts_expense_stock_goods_received_and_input_tax_against_the_supplier()
    {
        await SeedAsync();
        var payables = new PayablesService(Data);
        var supplier = await SupplierAsync("Bill supplier");
        var location = await new SetupService(Data).AddLocationAsync(db.NextLocationCode(), "Bills");
        var item = await new InventoryService(Data).SaveAsync(new Item { LocationId = location.Id, Plu = "FB" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), Name = "Billed item" }, null, null, null);
        Guid po;
        await using (var ctx = db.CreateContext())
        {
            var order = new PurchaseOrder { StoreId = Store, StationNumber = 1, Number = Random.Shared.NextInt64(1, 9_000_000), LocationId = location.Id, CreatedAt = DateTime.Now };
            ctx.PurchaseOrders.Add(order);
            await ctx.SaveChangesAsync();
            po = order.Id;
        }
        var date = new DateOnly(2040, 8, 10);
        var draft = await payables.SaveBillAsync(new SupplierBill
        {
            SupplierId = supplier.Id, StoreId = Store, BillDate = date, SupplierReference = "INV-77",
            Lines = [Line(await AccountAsync("6110"), 1000m, 18m), Line(null, 50m, 0, 4, item: item.Id), Line(null, 300m, 0, po: po)],
        });
        Assert.Equal((SupplierBillStatus.Draft, 0L, 1500m, 180m, 1680m, date.AddDays(30)), (draft.Status, draft.Number, draft.Subtotal, draft.TaxTotal, draft.Total, draft.DueDate));

        var bill = await payables.PostBillAsync(draft.Id);
        Assert.Equal(SupplierBillStatus.Posted, bill.Status);
        Assert.True(bill.Number > LedgerService.LedgerStation * NumberCounter.NumbersPerStation);
        var lines = await LinesAsync(bill.JournalEntryId);
        Assert.Equal((1000m, 180m, 200m, 300m, -1680m), (Dr(lines, "6110"), Dr(lines, "1300"), Dr(lines, "1200"), Dr(lines, "2020"), Dr(lines, "2010")));
        var ap = lines.Single(l => l.Account!.Code == "2010");
        Assert.Equal((LedgerPartyKind.Supplier, supplier.Id, Store), (ap.PartyKind!.Value, ap.PartyId!.Value, ap.StoreId!.Value));
        await Assert.ThrowsAsync<BusinessRuleException>(() => payables.PostBillAsync(bill.Id));
        var light = await AccountAsync("6110");
        await Assert.ThrowsAsync<BusinessRuleException>(() => payables.SaveBillAsync(new SupplierBill { Id = bill.Id, SupplierId = supplier.Id, StoreId = Store, BillDate = date, Lines = [Line(light, 1m)] }));

        var voided = await payables.VoidBillAsync(bill.Id, date, "Entered twice");
        Assert.Equal(SupplierBillStatus.Void, voided.Status);
        await using (var ctx = db.CreateContext())
            Assert.Equal(JournalStatus.Reversed, (await ctx.JournalEntries.SingleAsync(e => e.Id == bill.JournalEntryId)).Status);
    }

    [SkippableFact]
    public async Task Payments_settle_bills_less_debit_notes_and_withhold_at_the_filer_or_non_filer_rate()
    {
        await SeedAsync();
        var payables = new PayablesService(Data);
        var filer = await SupplierAsync("Filer");
        var nonFiler = await SupplierAsync("Non-filer", filer: false);
        var bank = await BankAsync();
        var rent = await AccountAsync("6100");
        var date = new DateOnly(2041, 9, 1);

        async Task<SupplierBill> PostedAsync(Guid supplier, decimal price, SupplierBillKind kind = SupplierBillKind.Bill) =>
            await payables.PostBillAsync((await payables.SaveBillAsync(new SupplierBill
            {
                Kind = kind, SupplierId = supplier, StoreId = Store, BillDate = date, Lines = [Line(rent, price, 18m)],
            })).Id);

        var bill = await PostedAsync(filer.Id, 100_000m);           // 118,000 with tax
        var note = await PostedAsync(filer.Id, 10_000m, SupplierBillKind.DebitNote);   // 11,800 back
        var noteLines = await LinesAsync(note.JournalEntryId);
        Assert.Equal((11_800m, -10_000m, -1_800m), (Dr(noteLines, "2010"), Dr(noteLines, "6100"), Dr(noteLines, "1300")));

        var quote = await new FinanceSetupService(Data).WithholdingForAsync(filer.Id, Store);
        var payment = await payables.PayAsync(new PaymentDraft(bank.Id, date, [new BillToPay(bill.Id, 118_000m), new BillToPay(note.Id, 11_800m)]) { Reference = "CHQ-1" });
        // Withholding on the goods before sales tax: (100,000 - 10,000) at the filer rate.
        decimal withheld = Money.Round(90_000m * quote.Rate / 100m);
        Assert.Equal((withheld, 106_200m - withheld), (payment.WithholdingAmount, payment.Amount));
        var lines = await LinesAsync(payment.JournalEntryId);
        Assert.Equal((106_200m, -withheld), (Dr(lines, "2010"), Dr(lines, "2120")));
        Assert.Equal(-(106_200m - withheld), lines.Where(l => l.AccountId == bank.LedgerAccountId).Sum(l => l.Debit - l.Credit));
        Assert.Equal(SupplierBillStatus.Paid, (await payables.GetBillAsync(bill.Id))!.Status);
        Assert.Equal(SupplierBillStatus.Paid, (await payables.GetBillAsync(note.Id))!.Status);
        await Assert.ThrowsAsync<BusinessRuleException>(() => payables.PayAsync(new PaymentDraft(bank.Id, date, [new BillToPay(bill.Id, 1m)])));

        // The non-filer pays double; part payment leaves the bill part paid; voiding the payment opens it again.
        var other = await PostedAsync(nonFiler.Id, 200_000m);
        var part = await payables.PayAsync(new PaymentDraft(bank.Id, date, [new BillToPay(other.Id, 118_000m)]));
        var rule = (await new FinanceSetupService(Data).GetRuleSetsAsync()).Single(s => s.Name == FinanceSetupService.PakistanRuleSet).WithholdingRules.Single(r => r.Kind == WithholdingKind.Goods);
        Assert.Equal(Money.Round(100_000m * rule.NonFilerRate / 100m), part.WithholdingAmount);
        Assert.Equal(SupplierBillStatus.PartPaid, (await payables.GetBillAsync(other.Id))!.Status);
        await payables.VoidPaymentAsync(part.Id, date, "Cheque bounced");
        var reopened = (await payables.GetBillAsync(other.Id))!;
        Assert.Equal((SupplierBillStatus.Posted, 0m), (reopened.Status, reopened.Paid));
        await Assert.ThrowsAsync<BusinessRuleException>(() => payables.VoidBillAsync(bill.Id, date, "Paid already"));

        // The statement follows the ledger: billed, returned, paid; nothing owed at the end.
        var statement = await payables.StatementAsync(filer.Id, date, date);
        Assert.Equal((0m, 0m), (statement.Opening, statement.Closing));
        Assert.Equal(3, statement.Lines.Count);
    }

    [SkippableFact]
    public async Task A_payment_run_pays_several_suppliers_and_ageing_and_bills_due_follow_the_due_dates()
    {
        await SeedAsync();
        var payables = new PayablesService(Data);
        var a = await SupplierAsync("Run A");
        var b = await SupplierAsync("Run B", kind: WithholdingKind.Other);
        var bank = await BankAsync();
        var account = await AccountAsync("6200");
        var date = new DateOnly(2042, 1, 5);
        var bills = new List<SupplierBill>();
        foreach (var (supplier, days) in new[] { (a.Id, 0), (a.Id, 45), (b.Id, 100) })
        {
            var draft = await payables.SaveBillAsync(new SupplierBill
            {
                SupplierId = supplier, StoreId = Store, BillDate = date.AddDays(-days - 30), DueDate = date.AddDays(-days), Lines = [Line(account, 1000m)],
            });
            bills.Add(await payables.PostBillAsync(draft.Id));
        }
        var ageing = await payables.AgeingAsync(date.AddDays(1), Store);
        var rowA = ageing.Single(r => r.PartyId == a.Id);
        Assert.Equal((1000m, 1000m, 2000m), (rowA.Days1To30, rowA.Days31To60, rowA.Total));
        Assert.Equal(1000m, ageing.Single(r => r.PartyId == b.Id).Over90);
        Assert.Equal(3, (await payables.BillsDueAsync(date, Store)).Count(x => bills.Any(y => y.Id == x.Id)));

        var (run, payments) = await payables.PayRunAsync(bank.Id, date, [.. bills.Select(x => new BillToPay(x.Id, x.Total))], "RUN-1");
        Assert.Equal((2, 2, 3000m), (payments.Count, run.Payments, payments.Sum(p => p.Amount + p.WithholdingAmount)));
        Assert.All(payments, p => Assert.Equal(run.Id, p.PaymentRunId));
        Assert.DoesNotContain(await payables.AgeingAsync(date.AddDays(1), Store), r => r.PartyId == a.Id || r.PartyId == b.Id);
    }

    [SkippableFact]
    public async Task A_foreign_currency_bill_paid_at_another_rate_leaves_an_exchange_difference()
    {
        await SeedAsync();
        var payables = new PayablesService(Data);
        var supplier = await SupplierAsync("Importer", kind: WithholdingKind.Other);
        var usd = await BankAsync("USD");
        var date = new DateOnly(2043, 2, 1);
        var bill = await payables.PostBillAsync((await payables.SaveBillAsync(new SupplierBill
        {
            SupplierId = supplier.Id, StoreId = Store, BillDate = date, Currency = "USD", ExchangeRate = 280m, Lines = [Line(await AccountAsync("6300"), 100m)],
        })).Id);
        Assert.Equal(-28_000m, Dr(await LinesAsync(bill.JournalEntryId), "2010"));
        await Assert.ThrowsAsync<BusinessRuleException>(async () => await payables.PayAsync(new PaymentDraft((await BankAsync()).Id, date, [new BillToPay(bill.Id, 100m)])));
        var payment = await payables.PayAsync(new PaymentDraft(usd.Id, date.AddDays(10), [new BillToPay(bill.Id, 100m)]) { ExchangeRate = 285m });
        var lines = await LinesAsync(payment.JournalEntryId);
        Assert.Equal((28_000m, 500m), (Dr(lines, "2010"), Dr(lines, "6500")));
        Assert.Equal(-28_500m, lines.Where(l => l.AccountId == usd.LedgerAccountId).Sum(l => l.Debit - l.Credit));
    }

    // Expenses --------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Expenses_post_from_cash_a_bank_or_on_account_and_recurring_ones_are_made_on_schedule()
    {
        await SeedAsync();
        var expenses = new ExpenseService(Data);
        var bank = await BankAsync();
        var supplier = await SupplierAsync("Landlord", kind: WithholdingKind.Rent);
        var rent = await AccountAsync("6100");
        var date = new DateOnly(2044, 7, 3);

        var cash = await expenses.PostAsync((await expenses.SaveAsync(new Expense { Date = date, StoreId = Store, Payee = "Plumber", AccountId = await AccountAsync("6200"), Amount = 500m, TaxAmount = 90m })).Id);
        var cashLines = await LinesAsync(cash.JournalEntryId);
        Assert.Equal((500m, 90m, -590m), (Dr(cashLines, "6200"), Dr(cashLines, "1300"), Dr(cashLines, "1010")));

        var fromBank = await expenses.PostAsync((await expenses.SaveAsync(new Expense
        {
            Date = date, StoreId = Store, Payee = "Electric co", AccountId = await AccountAsync("6110"), Amount = 2000m, PaidBy = ExpensePaidBy.Bank, BankAccountId = bank.Id,
        })).Id);
        Assert.Equal(-2000m, (await LinesAsync(fromBank.JournalEntryId)).Where(l => l.AccountId == bank.LedgerAccountId).Sum(l => l.Debit - l.Credit));

        var owed = await expenses.PostAsync((await expenses.SaveAsync(new Expense
        {
            Date = date, StoreId = Store, Payee = "Landlord", SupplierId = supplier.Id, AccountId = rent, Amount = 50_000m, PaidBy = ExpensePaidBy.OnAccount, Reference = "RENT-JUL",
        })).Id);
        var bill = (await new PayablesService(Data).GetBillAsync(owed.SupplierBillId!.Value))!;
        Assert.Equal((SupplierBillStatus.Posted, 50_000m, owed.Id), (bill.Status, bill.Total, bill.ExpenseId!.Value));
        await Assert.ThrowsAsync<BusinessRuleException>(() => expenses.SaveAsync(new Expense { Date = date, StoreId = Store, Payee = "x", AccountId = rent, Amount = 1m, PaidBy = ExpensePaidBy.OnAccount }));

        await expenses.VoidAsync(cash.Id, date, "Wrong store");
        Assert.Equal(ExpenseStatus.Void, (await expenses.GetAsync(cash.Id))!.Status);

        // Monthly rent made three times when the schedule is three months behind: two drafts, or posted.
        var drafts = await expenses.SaveRecurringAsync(new RecurringExpense
        {
            Name = "Shop rent", StoreId = Store, Payee = "Landlord", AccountId = rent, Amount = 40_000m, NextDate = new DateOnly(2044, 8, 31), Frequency = RecurrenceFrequency.Monthly,
        });
        var posted = await expenses.SaveRecurringAsync(new RecurringExpense
        {
            Name = "Internet", StoreId = Store, Payee = "ISP", AccountId = await AccountAsync("6120"), Amount = 3000m, NextDate = new DateOnly(2044, 9, 15), PostAutomatically = true,
            PaidBy = ExpensePaidBy.Bank, BankAccountId = bank.Id,
        });
        var made = await expenses.RunDueAsync(new DateOnly(2044, 10, 31));
        var rentMade = made.Where(e => e.RecurringExpenseId == drafts.Id).ToList();
        Assert.Equal([new DateOnly(2044, 8, 31), new DateOnly(2044, 9, 30), new DateOnly(2044, 10, 31)], rentMade.Select(e => e.Date));
        Assert.All(rentMade, e => Assert.Equal(ExpenseStatus.Draft, e.Status));
        Assert.All(made.Where(e => e.RecurringExpenseId == posted.Id), e => Assert.Equal(ExpenseStatus.Posted, e.Status));
        Assert.Equal(new DateOnly(2044, 11, 30), (await expenses.GetRecurringAsync(Store)).Single(r => r.Id == drafts.Id).NextDate);
        Assert.DoesNotContain(await expenses.RunDueAsync(new DateOnly(2044, 10, 31)), e => e.RecurringExpenseId == drafts.Id);
    }

    [SkippableFact]
    public async Task Receipts_are_kept_when_they_are_pictures_or_pdfs_within_the_size_limit()
    {
        await SeedAsync();
        var expenses = new ExpenseService(Data);
        var expense = await expenses.SaveAsync(new Expense { Date = new DateOnly(2044, 1, 2), StoreId = Store, Payee = "Shop", AccountId = await AccountAsync("6900"), Amount = 10m });
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n%receipt\n");
        var saved = await expenses.AttachAsync(AttachmentOwner.Expense, expense.Id, "receipt.pdf", pdf);
        Assert.Equal(("application/pdf", pdf.Length), (saved.ContentType, saved.Size));
        Assert.Equal(pdf, (await expenses.AttachmentAsync(saved.Id))!.Data);
        Assert.Equal("receipt.pdf", Assert.Single(await expenses.AttachmentsAsync(AttachmentOwner.Expense, expense.Id)).FileName);
        await Assert.ThrowsAsync<BusinessRuleException>(() => expenses.AttachAsync(AttachmentOwner.Expense, expense.Id, "virus.exe", [0x4D, 0x5A]));
        await Assert.ThrowsAsync<BusinessRuleException>(() => expenses.AttachAsync(AttachmentOwner.Expense, expense.Id, "fake.pdf", [0x4D, 0x5A, 0, 0]));
        var limit = (await new FinanceSetupService(Data).GetSettingsAsync()).AttachmentMaxKilobytes;
        var big = new byte[limit * 1024 + 1];
        "%PDF"u8.CopyTo(big);
        await Assert.ThrowsAsync<BusinessRuleException>(() => expenses.AttachAsync(AttachmentOwner.Expense, expense.Id, "big.pdf", big));
        await expenses.DeleteAttachmentAsync(saved.Id);
        Assert.Empty(await expenses.AttachmentsAsync(AttachmentOwner.Expense, expense.Id));
    }

    // Bank and cash ---------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task A_bank_account_gets_its_ledger_account_and_opening_balance_and_money_moves_between_accounts()
    {
        await SeedAsync();
        var service = new BankService(Data);
        var date = new DateOnly(2045, 7, 1);
        var bank = await BankAsync(opening: 10_000m, on: date);
        var ledger = (await new LedgerService(Data).GetAccountsAsync()).Single(a => a.Id == bank.LedgerAccountId);
        Assert.Equal((AccountType.Asset, true), (ledger.Type, ledger.IsPosting));
        Assert.Equal(10_000m, (await service.GetAccountsAsync()).Single(b => b.Account.Id == bank.Id).Balance);
        // Changing the opening balance replaces the opening entry.
        bank.OpeningBalance = 12_000m;
        await service.SaveAccountAsync(bank);
        Assert.Equal(12_000m, (await service.GetAccountsAsync()).Single(b => b.Account.Id == bank.Id).Balance);

        var petty = await new BankService(Data).SaveAccountAsync(new BankAccount { Name = "Petty " + Guid.NewGuid().ToString("N")[..5], Kind = BankAccountKind.Cash });
        var transfer = await service.TransferAsync(bank.Id, petty.Id, date, 2_500m, reference: "WD-1");
        var balances = await service.GetAccountsAsync();
        Assert.Equal((9_500m, 2_500m), (balances.Single(b => b.Account.Id == bank.Id).Balance, balances.Single(b => b.Account.Id == petty.Id).Balance));
        Assert.True(transfer.Number > 0);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.TransferAsync(bank.Id, bank.Id, date, 1m));

        // An End of Day's cash goes to the bank once.
        Guid session;
        await using (var ctx = db.CreateContext())
        {
            var location = await ctx.Locations.Where(l => l.StoreId == Store).Select(l => l.Id).FirstAsync();
            var row = new DrawerSession
            {
                StoreId = Store, StationNumber = 1, LocationId = location, OpenedAt = new DateTime(2045, 7, 2, 9, 0, 0), ClosedAt = new DateTime(2045, 7, 2, 21, 0, 0),
                Tenders = [new DrawerSessionTender { TenderType = "Cash", Amount = 7_000m }, new DrawerSessionTender { TenderType = "Visa", Amount = 3_000m }],
            };
            ctx.DrawerSessions.Add(row);
            await ctx.SaveChangesAsync();
            session = row.Id;
        }
        var candidate = Assert.Single(await service.DepositCandidatesAsync(new DateOnly(2045, 7, 2), new DateOnly(2045, 7, 2), Store));
        Assert.Equal((session, 7_000m, (Guid?)null), (candidate.DrawerSessionId, candidate.Cash, candidate.DepositId));
        var deposit = await service.DepositAsync(Store, bank.Id, new DateOnly(2045, 7, 3), 7_000m, session, "DEP-1");
        var lines = await LinesAsync(deposit.JournalEntryId);
        Assert.Equal((7_000m, -7_000m), (lines.Where(l => l.AccountId == bank.LedgerAccountId).Sum(l => l.Debit), Dr(lines, "1010")));
        Assert.Equal(deposit.Id, Assert.Single(await service.DepositCandidatesAsync(new DateOnly(2045, 7, 2), new DateOnly(2045, 7, 2), Store)).DepositId);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.DepositAsync(Store, bank.Id, new DateOnly(2045, 7, 3), 7_000m, session));
    }

    [SkippableFact]
    public async Task A_statement_is_read_with_the_banks_layout_matched_to_the_ledger_and_the_rest_entered()
    {
        await SeedAsync();
        var service = new BankService(Data);
        var bank = await BankAsync(opening: 50_000m, on: new DateOnly(2046, 7, 1));
        // The ledger: a supplier paid by cheque 1001, a deposit.
        var supplier = await SupplierAsync("Reconciled", kind: WithholdingKind.Other);
        var payables = new PayablesService(Data);
        var bill = await payables.PostBillAsync((await payables.SaveBillAsync(new SupplierBill
        {
            SupplierId = supplier.Id, StoreId = Store, BillDate = new DateOnly(2046, 7, 2), Lines = [Line(await AccountAsync("6200"), 4_000m)],
        })).Id);
        await payables.PayAsync(new PaymentDraft(bank.Id, new DateOnly(2046, 7, 3), [new BillToPay(bill.Id, 4_000m)]) { Reference = "1001" });
        await service.DepositAsync(Store, bank.Id, new DateOnly(2046, 7, 4), 6_000m, reference: "DEP-9");

        var csv = "Txn date;Details;Ref;Withdrawal;Deposit;Balance\r\n" +
                  "01/07/2046;Opening;;;50,000.00;50,000.00\r\n" +
                  "04/07/2046;Cheque paid;1001;4,000.00;;46,000.00\r\n" +
                  "05/07/2046;Cash deposit;DEP-9;;6,000.00;52,000.00\r\n" +
                  "06/07/2046;\"Service charge; July\";;150.00;;51,850.00\r\n";
        var format = new BankStatementFormat
        {
            BankName = bank.BankName!, Delimiter = ";", DateColumn = "Txn date", DateFormat = "dd/MM/yyyy", DescriptionColumn = "Details", ReferenceColumn = "Ref",
            DebitColumn = "Withdrawal", CreditColumn = "Deposit", BalanceColumn = "Balance",
        };
        var statement = await service.ImportStatementAsync(bank.Id, "july.csv", csv, format);
        Assert.Equal((4, (decimal?)51_850m), (statement.LineCount, statement.ClosingBalance));
        Assert.Equal("Service charge; July", statement.Lines[3].Description);
        Assert.Equal("Ref", (await service.FormatForAsync(bank.BankName))!.ReferenceColumn);
        // The same file again brings nothing new.
        Assert.Equal(0, (await service.ImportStatementAsync(bank.Id, "july.csv", csv, format)).LineCount);

        Assert.Equal(3, await service.AutoMatchAsync(bank.Id));
        var charge = Assert.Single(await service.StatementLinesAsync(bank.Id, status: StatementLineStatus.Unmatched));
        Assert.Equal(-150m, charge.Amount);
        var before = await service.SummaryAsync(bank.Id, new DateOnly(2046, 7, 31));
        Assert.Equal(((decimal?)51_850m, 52_000m, 1, -150m), (before.StatementBalance, before.LedgerBalance, before.UnmatchedLines, before.UnmatchedStatement));
        Assert.Equal((decimal?)0m, before.Difference);

        var entry = await service.CreateEntryAsync(charge.Id, null, null);
        Assert.Equal(150m, Dr(await LinesAsync(entry.Id), "6400"));
        var after = await service.SummaryAsync(bank.Id, new DateOnly(2046, 7, 31));
        Assert.Equal((51_850m, 0, 0m, 0m), (after.LedgerBalance, after.UnmatchedLines, after.Uncleared, after.Difference!.Value));

        // A match can be taken off and made again by hand; amounts must agree.
        var matched = (await service.StatementLinesAsync(bank.Id, status: StatementLineStatus.Matched)).First();
        var ledgerLine = matched.JournalLineId!.Value;
        await service.UnmatchAsync(matched.Id);
        var other = (await service.StatementLinesAsync(bank.Id, status: StatementLineStatus.Matched)).First();
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.MatchAsync(matched.Id, other.JournalLineId!.Value));
        await service.MatchAsync(matched.Id, ledgerLine);
        Assert.Contains(await service.LedgerLinesAsync(bank.Id), l => l.JournalLineId == ledgerLine && l.Matched);
    }

    // Years, opening balances, budgets --------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Closing_a_year_moves_its_profit_to_retained_earnings_per_store_and_opens_the_next_year()
    {
        await SeedAsync();
        var ledger = new LedgerService(Data);
        var years = new FinanceYearService(Data);
        var other = Guid.CreateVersion7();
        await using (var ctx = db.CreateContext())
        {
            ctx.Stores.Add(new Store { Id = other, Code = "Y" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant(), Name = "Year store" });
            await ctx.SaveChangesAsync();
        }
        var start = new DateOnly(2047, 7, 1);
        await ledger.PostAsync(new JournalDraft(start.AddDays(5), JournalKind.Manual, "Sales", [JournalDraftLine.Code("1010", 1000, 0, Store), JournalDraftLine.Code("4900", 0, 1000, Store)]));
        await ledger.PostAsync(new JournalDraft(start.AddDays(6), JournalKind.Manual, "Rent", [JournalDraftLine.Code("6100", 300, 0, Store), JournalDraftLine.Code("1010", 0, 300, Store)]));
        await ledger.PostAsync(new JournalDraft(start.AddDays(7), JournalKind.Manual, "Rent B", [JournalDraftLine.Code("6100", 200, 0, other), JournalDraftLine.Code("1010", 0, 200, other)]));
        var year = (await ledger.GetYearsAsync()).Single(y => y.StartDate == start);
        var last = year.Periods.Last();
        await ledger.SetPeriodStatusAsync(last.Id, FiscalStatus.Locked);

        var closed = await years.CloseAsync(year.Id);
        Assert.Equal(FiscalStatus.Closed, closed.Status);
        Assert.All((await ledger.GetYearsAsync()).Single(y => y.Id == year.Id).Periods, p => Assert.Equal(FiscalStatus.Closed, p.Status));
        Assert.Contains(await ledger.GetYearsAsync(), y => y.StartDate == start.AddYears(1));
        var end = start.AddYears(1).AddDays(-1);
        var a = await ledger.BalancesAsync(start, end, Store);
        Assert.Equal((0m, 0m, -700m), (Net(a, "4900"), Net(a, "6100"), Net(a, "3100")));
        Assert.Equal(200m, Net(await ledger.BalancesAsync(start, end, other), "3100"));
        // The profit and loss of the year still shows what it earned (closing entries are left out).
        var pl = await new FinanceReports(Data).ProfitAndLossAsync(new FinanceReportOptions(start, end, Store));
        Assert.Equal(700m, pl.Rows.Single(r => (string?)r[1] == "Net profit")[2]);

        await Assert.ThrowsAsync<BusinessRuleException>(() => years.CloseAsync(year.Id));
        await Assert.ThrowsAsync<BusinessRuleException>(() => ledger.PostAsync(new JournalDraft(start.AddDays(9), JournalKind.Manual, "Late", [JournalDraftLine.Code("6100", 1, 0), JournalDraftLine.Code("1010", 0, 1)])));

        await years.ReopenAsync(year.Id, "Audit found a missing bill");
        Assert.Equal(0m, Net(await ledger.BalancesAsync(start, end, Store), "3100"));
        Assert.Equal(-700m, Net(await ledger.BalancesAsync(start, end, Store), "4900") + Net(await ledger.BalancesAsync(start, end, Store), "6100"));
        var again = await years.CloseAsync(year.Id);
        Assert.Equal(FiscalStatus.Closed, again.Status);
        Assert.Equal(-700m, Net(await ledger.BalancesAsync(start, end, Store), "3100"));
    }

    [SkippableFact]
    public async Task Opening_balances_are_one_entry_and_a_difference_goes_to_retained_earnings_only_when_asked()
    {
        await SeedAsync();
        var years = new FinanceYearService(Data);
        var date = new DateOnly(2058, 1, 1);
        var lines = new List<OpeningLine> { new(await AccountAsync("1010"), 5_000m, 0), new(await AccountAsync("1200"), 20_000m, 0), new(await AccountAsync("3010"), 0, 22_000m) };
        await Assert.ThrowsAsync<BusinessRuleException>(() => years.PostOpeningAsync(date, Store, lines, balanceToRetainedEarnings: false));
        var entry = await years.PostOpeningAsync(date, Store, lines, balanceToRetainedEarnings: true);
        Assert.Equal(JournalKind.Opening, entry.Kind);
        Assert.Equal(-3_000m, Dr(await LinesAsync(entry.Id), "3100"));
        // Posted again for the same day: replaced, not added.
        var again = await years.PostOpeningAsync(date, Store, [.. lines.Take(2), new(await AccountAsync("3010"), 0, 25_000m)], false);
        Assert.Equal(entry.Id, again.Id);
        Assert.Equal(0m, Dr(await LinesAsync(entry.Id), "3100"));
    }

    [SkippableFact]
    public async Task Budgets_are_entered_or_copied_from_last_year_and_compared_with_actuals()
    {
        await SeedAsync();
        var ledger = new LedgerService(Data);
        var budgets = new BudgetService(Data);
        var lastYear = await ledger.EnsureYearAsync(new DateOnly(2049, 8, 1));
        var year = await ledger.EnsureYearAsync(new DateOnly(2050, 8, 1));
        await ledger.PostAsync(new JournalDraft(new DateOnly(2049, 8, 15), JournalKind.Manual, "Rent", [JournalDraftLine.Code("6100", 1000, 0, Store), JournalDraftLine.Code("1010", 0, 1000, Store)]));
        Assert.Equal(1, await budgets.CopyFromLastYearAsync(year.Id, Store, fromBudget: false, percent: 10m));
        var line = Assert.Single(await budgets.GetAsync(year.Id, Store));
        Assert.Equal((new DateOnly(2050, 8, 1), 1100m), (line.Month, line.Amount));
        var rent = await AccountAsync("6100");
        await budgets.SaveAsync(year.Id, Store, [new BudgetAmount(rent, new DateOnly(2050, 9, 1), 1200m), new BudgetAmount(rent, new DateOnly(2050, 8, 1), 0m)]);
        Assert.Equal(new DateOnly(2050, 9, 1), Assert.Single(await budgets.GetAsync(year.Id, Store)).Month);
        await Assert.ThrowsAsync<BusinessRuleException>(() => budgets.SaveAsync(year.Id, Store, [new BudgetAmount(rent, new DateOnly(2052, 1, 1), 5m)]));
        await ledger.PostAsync(new JournalDraft(new DateOnly(2050, 9, 10), JournalKind.Manual, "Rent", [JournalDraftLine.Code("6100", 900, 0, Store), JournalDraftLine.Code("1010", 0, 900, Store)]));
        var compare = (await budgets.CompareAsync(year.StartDate, year.EndDate, Store)).Single(r => r.Code == "6100");
        Assert.Equal((1200m, 900m, -300m), (compare.Budget, compare.Actual, compare.Difference));
        Assert.Equal(1000m, (await budgets.CompareAsync(lastYear.StartDate, lastYear.EndDate, Store)).Single(r => r.Code == "6100").Actual);
        var report = await new FinanceReports(Data).ProfitAndLossAsync(new FinanceReportOptions(new DateOnly(2050, 9, 1), new DateOnly(2050, 9, 30), Store, FinanceComparison.Budget));
        var row = report.Rows.Single(r => (string?)r[0] == "6100");
        Assert.Equal((900m, 1200m, -300m), ((decimal)row[2]!, (decimal)row[3]!, (decimal)row[4]!));
    }

    // Reports and exports ---------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task The_statements_agree_with_each_other_for_a_store_and_the_company()
    {
        await SeedAsync();
        var ledger = new LedgerService(Data);
        var reports = new FinanceReports(Data);
        var from = new DateOnly(2051, 7, 1);
        var to = new DateOnly(2051, 7, 31);
        var bank = await BankAsync(opening: 1_000m, on: from);
        await ledger.PostAsync(new JournalDraft(from.AddDays(1), JournalKind.Manual, "Capital", [JournalDraftLine.Role(LedgerRoles.Cash, 9_000, 0, Store), JournalDraftLine.Code("3010", 0, 9_000, Store)]));
        await ledger.PostAsync(new JournalDraft(from.AddDays(2), JournalKind.Manual, "Stock", [JournalDraftLine.Code("1200", 4_000, 0, Store), JournalDraftLine.Code("2010", 0, 4_000, Store)]));
        await ledger.PostAsync(new JournalDraft(from.AddDays(3), JournalKind.Manual, "Sale", [JournalDraftLine.Role(LedgerRoles.Cash, 2_500, 0, Store), JournalDraftLine.Code("4900", 0, 2_500, Store)]));
        await ledger.PostAsync(new JournalDraft(from.AddDays(4), JournalKind.Manual, "Rent", [JournalDraftLine.Code("6100", 700, 0, Store), JournalDraftLine.Role(LedgerRoles.Cash, 0, 700, Store)]));

        var tb = await reports.TrialBalanceAsync(new FinanceReportOptions(from, to, Store));
        var totals = tb.TotalsRow();
        Assert.Equal(totals[4], totals[5]);
        Assert.Equal(totals[6], totals[7]);

        var pl = await reports.ProfitAndLossAsync(new FinanceReportOptions(from, to, Store, FinanceComparison.PreviousYear));
        Assert.Equal(5, pl.Columns.Count);
        Assert.Equal(1_800m, pl.Rows.Single(r => (string?)r[1] == "Net profit")[2]);

        var bs = await reports.BalanceSheetAsync(new FinanceReportOptions(from, to, Store, FinanceComparison.PreviousPeriod));
        decimal Value(ReportTable t, string name) => (decimal)t.Rows.Single(r => (string?)r[1] == name)[2]!;
        Assert.Equal(Value(bs, "Total assets"), Value(bs, "Total liabilities and equity"));
        var company = await reports.BalanceSheetAsync(new FinanceReportOptions(from, to));
        Assert.Equal(Value(company, "Total assets"), Value(company, "Total liabilities and equity"));

        var flow = await reports.CashFlowAsync(new FinanceReportOptions(from, to, Store));
        decimal Line(string name) => (decimal)flow.Rows.Single(r => (string?)r[2] == name)[3]!;
        Assert.Equal(Line("Cash and bank at the end") - Line("Cash and bank at the start"), Line("Net change in cash and bank"));
        Assert.Equal(1_800m, Line("Profit for the period"));

        var exports = await new LedgerExportService(Data).EntriesAsync(from, to, Store);
        Assert.True(exports.Count >= 4);
        var xml = XDocument.Parse(LedgerExportService.ToTallyXml(exports, "SMA"));
        Assert.Equal(exports.Count, xml.Descendants("VOUCHER").Count());
        Assert.All(xml.Descendants("VOUCHER"), v => Assert.Equal(0m, v.Descendants("AMOUNT").Sum(a => decimal.Parse(a.Value, System.Globalization.CultureInfo.InvariantCulture))));
        var iif = LedgerExportService.ToIif(exports);
        Assert.Equal(exports.Count, iif.Split("\r\n").Count(l => l == "ENDTRNS"));
        Assert.StartsWith("!ACCNT", iif, StringComparison.Ordinal);
        Assert.Contains("Capital", LedgerExportService.ToCsv(exports), StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task The_sales_tax_return_sets_input_tax_on_bills_and_expenses_against_output_tax()
    {
        await SeedAsync();
        var from = new DateOnly(2052, 3, 1);
        var to = new DateOnly(2052, 3, 31);
        var supplier = await SupplierAsync("Taxed");
        var payables = new PayablesService(Data);
        await payables.PostBillAsync((await payables.SaveBillAsync(new SupplierBill
        {
            SupplierId = supplier.Id, StoreId = Store, BillDate = from.AddDays(2), Lines = [Line(await AccountAsync("6200"), 10_000m, 18m)],
        })).Id);
        var expenses = new ExpenseService(Data);
        await expenses.PostAsync((await expenses.SaveAsync(new Expense { Date = from.AddDays(3), StoreId = Store, Payee = "Shop", AccountId = await AccountAsync("6900"), Amount = 1_000m, TaxAmount = 180m })).Id);
        await new LedgerService(Data).PostAsync(new JournalDraft(from.AddDays(4), JournalKind.Daily, "Day", [JournalDraftLine.Role(LedgerRoles.Cash, 11_800, 0, Store),
            JournalDraftLine.Code("4010", 0, 10_000, Store), JournalDraftLine.Code("2100", 0, 1_800, Store)]) { Source = $"test-tax:{Guid.NewGuid():N}", StoreId = Store });
        var report = await new FinanceReports(Data).SalesTaxAsync(new FinanceReportOptions(from, to, Store));
        decimal Tax(string line) => (decimal)report.Rows.Single(r => ((string)r[1]!).StartsWith(line, StringComparison.Ordinal))[3]!;
        Assert.Equal((1_800m, 1_800m, 180m, 1_980m), (Tax("Sales from the daily journal"), Tax("Supplier bills"), Tax("Expenses"), Tax("Input tax to claim")));
        Assert.Equal(-180m, Tax("Tax to carry forward"));
    }

    // FBR and automatic posting ---------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Sales_arriving_after_the_integration_is_on_are_sent_and_get_the_fiscal_number()
    {
        await SeedAsync();
        var fiscal = new FiscalService(Data);
        var sender = new FakeFiscalInvoiceSender();
        await fiscal.EnqueueAsync(sender);   // the first look only marks where to start
        await Task.Delay(5);
        var location = await new SetupService(Data).AddLocationAsync(db.NextLocationCode(), "Fiscal");
        var item = await new InventoryService(Data).SaveAsync(new Item { LocationId = location.Id, Plu = "FI" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), Name = "Fiscal item", Price1 = 50m, OnHand = 5 }, null, null, null);
        var session = new SessionInfo { StationNumber = 1, LocationId = location.Id, LocationCode = location.Code, UserName = "tester" };
        var sale = await new SalesService(Data).PostSaleAsync(new SaleDraft
        {
            Lines = [new SaleDraftLine { ItemId = item.Id, ItemType = item.ItemType, Plu = item.Plu, Description = item.Name, Quantity = 2, UnitPrice = 50m }],
        }, [new TenderEntry("Cash", TenderKind.Cash, 100m, 100m)], session);
        Assert.True(await fiscal.EnqueueAsync(sender) >= 1);
        Assert.Equal(0, await fiscal.EnqueueAsync(sender));

        sender.Refuse = "Service unavailable";
        await fiscal.SendDueAsync(sender);
        var failed = (await fiscal.QueueAsync(Store, null)).Single(r => r.Item.SaleId == sale.Id).Item;
        Assert.Equal((FiscalInvoiceStatus.Failed, 1, "Service unavailable"), (failed.Status, failed.Attempts, failed.LastError));
        Assert.Equal(0, await fiscal.SendDueAsync(sender));   // waits before the next try
        await fiscal.RetryAsync(failed.Id);
        sender.Refuse = null;
        Assert.True(await fiscal.SendDueAsync(sender) >= 1);
        var sent = sender.Sent.Single(r => r.SaleId == sale.Id);
        Assert.Equal((100m, 2m, FiscalPaymentMode.Cash, sale.InvoiceNumber), (sent.TotalBillAmount, sent.TotalQuantity, sent.PaymentMode, sent.InvoiceNumber));
        await using (var ctx = db.CreateContext())
        {
            var stored = await ctx.Sales.AsNoTracking().SingleAsync(s => s.Id == sale.Id);
            Assert.StartsWith("FAKE-", stored.FiscalInvoiceNumber, StringComparison.Ordinal);
            var receipt = ReceiptFormatter.Sale(stored, new StoreSettings(), null, null);
            Assert.Contains("Fiscal invoice " + stored.FiscalInvoiceNumber, receipt, StringComparison.Ordinal);
            // A copy of the sale without the number (from a till that had not heard of it) gets it back.
            await ctx.Sales.Where(s => s.Id == sale.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.FiscalInvoiceNumber, (string?)null));
        }
        Assert.True(await fiscal.RestampAsync() >= 1);
        await Assert.ThrowsAsync<BusinessRuleException>(() => fiscal.RetryAsync(failed.Id));
    }

    [SkippableFact]
    public async Task Days_touched_by_an_applied_package_are_posted_and_post_days_runs_the_same()
    {
        await SeedAsync();
        var automation = new LedgerAutomation(Data);
        await automation.PostSyncedDaysAsync();   // the first look only marks where to start
        await Task.Delay(5);
        var location = await new SetupService(Data).AddLocationAsync(db.NextLocationCode(), "Auto post");
        var item = await new InventoryService(Data).SaveAsync(new Item { LocationId = location.Id, Plu = "AP" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), Name = "Auto item", Price1 = 30m, OnHand = 5 }, null, null, null);
        var session = new SessionInfo { StationNumber = 1, LocationId = location.Id, LocationCode = location.Code, UserName = "tester" };
        var sale = await new SalesService(Data).PostSaleAsync(new SaleDraft
        {
            Lines = [new SaleDraftLine { ItemId = item.Id, ItemType = item.ItemType, Plu = item.Plu, Description = item.Name, Quantity = 1, UnitPrice = 30m }],
        }, [new TenderEntry("Cash", TenderKind.Cash, 30m, 30m)], session);
        var day = new DateOnly(2053, 3, 10);
        await using (var ctx = db.CreateContext())
        {
            var soldAt = day.ToDateTime(new TimeOnly(11, 0));
            var now = SyncColumns.Now();
            await ctx.Sales.Where(s => s.Id == sale.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SoldAt, soldAt).SetProperty(x => x.UpdatedAtUtc, now));
            await ctx.StockMovements.Where(m => m.ItemId == item.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.At, soldAt).SetProperty(x => x.UpdatedAtUtc, now));
            ctx.SyncBatches.Add(new SyncBatch
            {
                BatchId = Guid.NewGuid(), StoreId = Store, StationNumber = 1, Direction = SyncDirection.Upload, Status = SyncBatchStatus.Applied, ReceivedAtUtc = now, AppliedAtUtc = now,
            });
            await ctx.SaveChangesAsync();
        }
        var result = await automation.PostSyncedDaysAsync();
        Assert.Empty(result.Problems);
        Assert.True(result.Entries >= 1);
        await using (var ctx = db.CreateContext())
            Assert.True(await ctx.JournalEntries.AnyAsync(e => e.Source == $"daily:{Store:N}:{day:yyyy-MM-dd}"));
        Assert.Equal(0, (await automation.PostSyncedDaysAsync()).Entries);
        Assert.Equal(0, (await automation.PostDaysAsync(day, day, Store)).Entries);
        await Assert.ThrowsAsync<BusinessRuleException>(() => automation.PostDaysAsync(day, day.AddDays(500)));
    }

    private static decimal Net(List<AccountBalance> balances, string code) => balances.Where(b => b.Code == code).Sum(b => b.Debit - b.Credit);
}

public sealed class FinanceTestsOnSqlite(SqliteFixture db) : FinanceTests(db), IClassFixture<SqliteFixture>;

public sealed class FinanceTestsOnSqlServer(SqlServerFixture db) : FinanceTests(db), IClassFixture<SqlServerFixture>;
