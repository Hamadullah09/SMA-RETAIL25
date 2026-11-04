using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.Data.Tests;

/// <summary>
/// The general ledger: the standard chart, posting and its checks, replacing what a source posted, locked periods,
/// reversals, approval of entries made by hand, balances, and the daily journal posted per store and day. Each test
/// works in a financial year of its own, because they share one database.
/// </summary>
public abstract class LedgerTests(DatabaseFixture db)
{
    private static int _station = 600;

    private DataContext Data => db.Data;

    private async Task<LedgerService> LedgerAsync()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        var ledger = new LedgerService(Data);
        await ledger.SeedAsync();
        return ledger;
    }

    private static JournalDraft Entry(DateOnly date, decimal amount, string debit = "6100", string credit = "1010", string? source = null) =>
        new(date, JournalKind.Expense, "Rent", [JournalDraftLine.Code(debit, amount, 0), JournalDraftLine.Code(credit, 0, amount)]) { Source = source };

    /// <summary>What the period moved on an account (debit positive); the opening balance holds the other tests' years.</summary>
    private static decimal Net(List<AccountBalance> balances, string code) => balances.Where(b => b.Code == code).Sum(b => b.Debit - b.Credit);

    [SkippableFact]
    public async Task A_new_ledger_gets_the_standard_chart_and_every_store_its_mappings_once()
    {
        var ledger = await LedgerAsync();
        await ledger.SeedAsync();
        var accounts = await ledger.GetAccountsAsync();
        Assert.Equal(LedgerService.StandardChart.Count, accounts.Count);
        Assert.All(LedgerRoles.All, role => Assert.Single(accounts, a => a.Role == role));
        Assert.All(accounts.Where(a => a.ParentId is not null), a => Assert.False(accounts.Single(p => p.Id == a.ParentId).IsPosting));

        await using var ctx = db.CreateContext();
        var store = db.Station!.StoreId;
        var mappings = await ctx.AccountMappings.AsNoTracking().Where(m => m.StoreId == store).ToListAsync();
        Assert.Equal("4010", mappings.Single(m => m.Kind == AccountMappingKind.Sales).AccountCode);
        Assert.Equal("1010", mappings.Single(m => m.Kind == AccountMappingKind.Tender && m.Key == "Cash").AccountCode);
        Assert.Equal(mappings.Count, mappings.Select(m => (m.Kind, m.Key)).Distinct().Count());
    }

    [SkippableFact]
    public async Task A_balanced_entry_is_numbered_and_wrong_ones_are_refused()
    {
        var ledger = await LedgerAsync();
        var date = new DateOnly(2030, 8, 10);
        var entry = await ledger.PostAsync(Entry(date, 1000m));
        Assert.Equal(JournalStatus.Posted, entry.Status);
        Assert.True(entry.Number > LedgerService.LedgerStation * NumberCounter.NumbersPerStation);
        var year = Assert.Single(await ledger.GetYearsAsync(), y => y.StartDate <= date && y.EndDate >= date);
        Assert.Equal((new DateOnly(2030, 7, 1), new DateOnly(2031, 6, 30), "2030-31", 12), (year.StartDate, year.EndDate, year.Name, year.Periods.Count));

        await Assert.ThrowsAsync<BusinessRuleException>(() => ledger.PostAsync(new JournalDraft(date, JournalKind.Manual, "Off",
            [JournalDraftLine.Code("6100", 10, 0), JournalDraftLine.Code("1010", 0, 9)])));
        await Assert.ThrowsAsync<BusinessRuleException>(() => ledger.PostAsync(Entry(date, 10m, debit: "6000")));
        await Assert.ThrowsAsync<BusinessRuleException>(() => ledger.PostAsync(Entry(date, 10m, debit: "NONE")));
        await Assert.ThrowsAsync<BusinessRuleException>(() => ledger.PostAsync(new JournalDraft(date, JournalKind.Manual, "One line", [JournalDraftLine.Code("6100", 10, 0)])));
        await Assert.ThrowsAsync<BusinessRuleException>(() => ledger.PostAsync(new JournalDraft(date, JournalKind.Manual, "Both sides",
            [JournalDraftLine.Code("6100", 10, 10), JournalDraftLine.Code("1010", 0, 0)])));

        var rent = (await ledger.GetAccountsAsync()).Single(a => a.Code == "6100");
        rent.IsActive = false;
        await ledger.SaveAccountAsync(rent);
        await Assert.ThrowsAsync<BusinessRuleException>(() => ledger.PostAsync(Entry(date, 10m)));
        rent.IsActive = true;
        await ledger.SaveAccountAsync(rent);
        await Assert.ThrowsAsync<BusinessRuleException>(() => ledger.SaveAccountAsync(new LedgerAccount { Code = "6100", Name = "Twin", Type = AccountType.Expense }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => ledger.DeleteAccountAsync(rent.Id));
    }

    [SkippableFact]
    public async Task Posting_a_source_again_replaces_it_while_open_and_adds_the_difference_once_locked()
    {
        var ledger = await LedgerAsync();
        var date = new DateOnly(2032, 3, 5);
        var first = await ledger.PostAsync(Entry(date, 100m, source: "test:rent-march"));
        var again = await ledger.PostAsync(Entry(date, 120m, source: "test:rent-march"));
        Assert.Equal((first.Id, first.Number), (again.Id, again.Number));
        Assert.Equal(120m, (await ledger.GetEntryAsync(first.Id))!.Lines.Sum(l => l.Debit));
        Assert.Equal(first.Id, (await ledger.PostAsync(Entry(date, 120m, source: "test:rent-march"))).Id);

        var march = (await ledger.GetYearsAsync()).SelectMany(y => y.Periods).Single(p => p.StartDate == new DateOnly(2032, 3, 1));
        await ledger.SetPeriodStatusAsync(march.Id, FiscalStatus.Locked);
        Assert.False(await ledger.IsOpenAsync(date));
        await Assert.ThrowsAsync<BusinessRuleException>(() => ledger.PostAsync(Entry(date, 5m)));

        var adjustment = await ledger.PostAsync(Entry(date, 150m, source: "test:rent-march"));
        Assert.Equal((JournalKind.Adjustment, new DateOnly(2032, 4, 1), "test:rent-march#adj1"), (adjustment.Kind, adjustment.Date, adjustment.Source));
        Assert.Equal(30m, adjustment.Lines.Sum(l => l.Debit));
        // Nothing more to add: the entry already posted is returned and no second adjustment is made.
        Assert.Equal(first.Id, (await ledger.PostAsync(Entry(date, 150m, source: "test:rent-march"))).Id);
        await using (var ctx = db.CreateContext())
            Assert.Equal(1, await ctx.JournalEntries.CountAsync(e => e.Source!.StartsWith("test:rent-march#adj")));

        var year = await ledger.BalancesAsync(new DateOnly(2031, 7, 1), new DateOnly(2032, 6, 30));
        Assert.Equal(150m, Net(year, "6100"));
        Assert.Equal(-150m, Net(year, "1010"));
        await ledger.SetPeriodStatusAsync(march.Id, FiscalStatus.Open);
        Assert.True(await ledger.IsOpenAsync(date));
    }

    [SkippableFact]
    public async Task A_reversal_cancels_an_entry_and_cannot_be_repeated()
    {
        var ledger = await LedgerAsync();
        var date = new DateOnly(2033, 9, 1);
        var entry = await ledger.PostAsync(Entry(date, 75m));
        var reversal = await ledger.ReverseAsync(entry.Id, date, "Entered twice");
        Assert.Equal((JournalKind.Reversal, entry.Id), (reversal.Kind, reversal.ReversalOfId));
        Assert.Equal(JournalStatus.Reversed, (await ledger.GetEntryAsync(entry.Id))!.Status);
        var balances = await ledger.BalancesAsync(date, date);
        Assert.Equal(0m, Net(balances, "6100"));
        Assert.Equal(150m, balances.Where(b => b.Code == "6100").Sum(b => b.Debit + b.Credit));
        await Assert.ThrowsAsync<BusinessRuleException>(() => ledger.ReverseAsync(entry.Id, date, "Again"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => ledger.ReverseAsync(reversal.Id, date, "Undo the undo"));
    }

    [SkippableFact]
    public async Task An_entry_made_by_hand_waits_for_someone_else_to_approve_it()
    {
        await LedgerAsync();
        var maker = Data;
        maker.CurrentUser = new SessionInfo { UserId = Guid.NewGuid(), UserName = "maker" };
        var checker = Data;
        checker.CurrentUser = new SessionInfo { UserId = Guid.NewGuid(), UserName = "checker" };
        var date = new DateOnly(2034, 1, 15);
        var draft = await new LedgerService(maker).SaveDraftAsync(new JournalDraft(date, JournalKind.Manual, "Owner's capital",
            [JournalDraftLine.Role(LedgerRoles.Bank, 5000, 0), JournalDraftLine.Code("3010", 0, 5000)]));
        Assert.Equal((JournalStatus.Draft, 0L), (draft.Status, draft.Number));
        Assert.DoesNotContain(await new LedgerService(checker).BalancesAsync(date, date), b => b.Code == "3010");

        await Assert.ThrowsAsync<BusinessRuleException>(() => new LedgerService(maker).ApproveAsync(draft.Id));
        var posted = await new LedgerService(checker).ApproveAsync(draft.Id);
        Assert.Equal((JournalStatus.Posted, "checker", "maker"), (posted.Status, posted.PostedBy, posted.CreatedBy));
        Assert.True(posted.Number > 0);
        Assert.Equal(-5000m, Net(await new LedgerService(checker).BalancesAsync(date, date), "3010"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => new LedgerService(checker).DeleteDraftAsync(draft.Id));
    }

    [SkippableFact]
    public async Task The_account_ledger_runs_and_the_trial_balance_balances()
    {
        var ledger = await LedgerAsync();
        var store = db.Station!.StoreId;
        await ledger.PostAsync(Entry(new DateOnly(2035, 7, 31), 40m) with { StoreId = store });
        await ledger.PostAsync(Entry(new DateOnly(2035, 8, 2), 60m) with { StoreId = store });
        await ledger.PostAsync(Entry(new DateOnly(2035, 8, 3), 25m, debit: "1010", credit: "4900") with { StoreId = store });

        var balances = await ledger.BalancesAsync(new DateOnly(2035, 8, 1), new DateOnly(2035, 8, 31), store);
        Assert.Equal(0m, balances.Sum(b => b.Opening));
        Assert.Equal(balances.Sum(b => b.Debit), balances.Sum(b => b.Credit));
        var cash = balances.Single(b => b.Code == "1010");
        // The store's cash before July holds what other tests posted for it (a day's takings), so compare against that.
        decimal before = (await ledger.BalancesAsync(new DateOnly(2035, 7, 1), new DateOnly(2035, 7, 1), store)).Where(b => b.Code == "1010").Sum(b => b.Opening);
        Assert.Equal((before - 40m, 25m, 60m, before - 75m), (cash.Opening, cash.Debit, cash.Credit, cash.Closing));

        var cashId = (await ledger.AccountForRoleAsync(LedgerRoles.Cash))!.Id;
        var rows = await ledger.AccountLedgerAsync(cashId, new DateOnly(2035, 8, 1), new DateOnly(2035, 8, 31), store);
        Assert.Equal([before - 100m, before - 75m], rows.Select(r => r.Balance));
    }

    [SkippableFact]
    public async Task A_stores_business_day_is_posted_from_the_daily_journal_and_only_once()
    {
        var ledger = await LedgerAsync();
        var location = await new SetupService(Data).AddLocationAsync(db.NextLocationCode(), "Ledger test");
        var item = await new InventoryService(Data).SaveAsync(new Item
        {
            LocationId = location.Id, Plu = "LG" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), Name = "Ledger item", Price1 = 20m,
            LastCost = 8m, AverageCost = 8m, OnHand = 10, Taxable1 = false, Taxable2 = false,
        }, "LEDGER", null, null);
        var session = new SessionInfo { StationNumber = Interlocked.Increment(ref _station), LocationId = location.Id, LocationCode = location.Code, UserName = "tester", AccessLevel = 4 };
        var draft = new SaleDraft
        {
            Lines =
            [
                new SaleDraftLine
                {
                    ItemId = item.Id, ItemType = item.ItemType, Plu = item.Plu, Description = item.Name, Quantity = 2, UnitPrice = 20m, UnitCost = 8m, Department = "LEDGER",
                },
            ],
        };
        await new SalesService(Data).PostSaleAsync(draft, [new TenderEntry("Cash", TenderKind.Cash, 40m, 40m)], session);

        var today = DateOnly.FromDateTime(DateTime.Today);
        Assert.True(await ledger.PostDailyAsync(today, today) >= 1);
        var store = db.Station!.StoreId;
        await using var ctx = db.CreateContext();
        var entry = await ctx.JournalEntries.AsNoTracking().Include(e => e.Lines).SingleAsync(e => e.Source == $"daily:{store:N}:{today:yyyy-MM-dd}");
        Assert.Equal((JournalKind.Daily, store), (entry.Kind, entry.StoreId));
        Assert.Equal(entry.Lines.Sum(l => l.Debit), entry.Lines.Sum(l => l.Credit));
        Assert.All(entry.Lines, l => Assert.Equal(store, l.StoreId));

        Assert.Equal(0, await ledger.PostDailyAsync(today, today));
        var again = await ctx.JournalEntries.AsNoTracking().SingleAsync(e => e.Source == entry.Source);
        Assert.Equal((entry.Number, entry.UpdatedAtUtc), (again.Number, again.UpdatedAtUtc));
        var balances = await ledger.BalancesAsync(today, today, store);
        Assert.True(Net(balances, "1010") >= 40m);
        Assert.True(Net(balances, "4010") <= -40m);
    }
}

public sealed class LedgerTestsOnSqlite(SqliteFixture db) : LedgerTests(db), IClassFixture<SqliteFixture>;

public sealed class LedgerTestsOnSqlServer(SqlServerFixture db) : LedgerTests(db), IClassFixture<SqlServerFixture>;
