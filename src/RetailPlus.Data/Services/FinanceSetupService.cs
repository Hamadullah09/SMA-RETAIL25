using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>What a supplier payment withholds: the rule set's rate for the supplier, and the amount on a taxable base.</summary>
public sealed record WithholdingQuote(WithholdingKind Kind, decimal Rate, decimal Threshold, string? Section, string? RuleSet);

/// <summary>
/// The finance module's setup: its settings, the country tax rule sets (Pakistan seeded), each store's rule set and deposit
/// account, the suppliers' tax details, and the daily journal's account mapping picked from the chart of accounts.
/// </summary>
public sealed class FinanceSetupService(DataContext data)
{
    /// <summary>The name of the seeded Pakistan rule set.</summary>
    public const string PakistanRuleSet = "Pakistan";

    /// <summary>
    /// Pakistan's rates as published for tax year 2026 (Finance Act 2025), as far as known when this was written. They
    /// change with every federal budget (June): check and update them each budget year on the web (Finance › Settings ›
    /// Tax rules). Withholding: section 153(1)(a) supplies of goods (company rate), 153(1)(b) services (company rate), 155
    /// rent (company rate); suppliers not on the active taxpayers list pay the non-filer rate (double).
    /// </summary>
    public static readonly string PakistanNotes =
        "Seeded with the rates published for tax year 2026 (Finance Act 2025) as far as known when the program was written. " +
        "Rates change with every federal budget: check them each budget year. Withholding rates are those for companies; " +
        "individuals and associations of persons may have other rates (153(1)(a) goods 5.5%, services 11%, rent in slabs). " +
        "Thresholds are applied to each payment here, while the law applies them to a supplier's payments over the year.";

    // Settings --------------------------------------------------------------------------------------------------------

    public async Task<FinanceSettings> GetSettingsAsync()
    {
        await using var db = data.Create();
        return await db.FinanceSettings.AsNoTracking().FirstOrDefaultAsync() ?? new FinanceSettings();
    }

    public async Task<FinanceSettings> SaveSettingsAsync(FinanceSettings settings)
    {
        if (settings.NightlyDays is < 1 or > 92) throw new BusinessRuleException("The nightly posting goes back 1 to 92 days.");
        if (settings.NightlyHour is < 0 or > 23) throw new BusinessRuleException("The nightly posting runs at an hour from 0 to 23.");
        if (settings.AttachmentMaxKilobytes is < 64 or > 51200) throw new BusinessRuleException("Attachments may be 64 KB to 50 MB.");
        await using var db = data.Create();
        var row = await db.FinanceSettings.FirstOrDefaultAsync();
        if (row is null) db.FinanceSettings.Add(row = new FinanceSettings());
        row.AutoPostOnSync = settings.AutoPostOnSync;
        row.NightlyDays = settings.NightlyDays;
        row.NightlyHour = settings.NightlyHour;
        row.AttachmentMaxKilobytes = settings.AttachmentMaxKilobytes;
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>Gives a new database its finance settings and the Pakistan rule set; running it again changes nothing.</summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = data.Create();
        if (!await db.FinanceSettings.AnyAsync(cancellationToken)) db.FinanceSettings.Add(new FinanceSettings());
        if (!await db.TaxRuleSets.AnyAsync(cancellationToken))
        {
            var pk = new TaxRuleSet { Country = "PK", Name = PakistanRuleSet, InputTaxClaimable = true, Notes = PakistanNotes };
            pk.Id = Guid.CreateVersion7();
            pk.SalesTaxRates.Add(new SalesTaxRate { Name = "Standard", Rate = 18m, IsDefault = true });
            pk.SalesTaxRates.Add(new SalesTaxRate { Name = "Zero-rated", Rate = 0m });
            pk.SalesTaxRates.Add(new SalesTaxRate { Name = "Exempt", Rate = 0m });
            pk.WithholdingRules.Add(new WithholdingRule { Kind = WithholdingKind.Goods, Section = "153(1)(a)", FilerRate = 5m, NonFilerRate = 10m, Threshold = 75000m });
            pk.WithholdingRules.Add(new WithholdingRule { Kind = WithholdingKind.Services, Section = "153(1)(b)", FilerRate = 9m, NonFilerRate = 18m, Threshold = 30000m });
            pk.WithholdingRules.Add(new WithholdingRule { Kind = WithholdingKind.Rent, Section = "155", FilerRate = 15m, NonFilerRate = 30m });
            pk.WithholdingRules.Add(new WithholdingRule { Kind = WithholdingKind.Other, Section = null, FilerRate = 0m, NonFilerRate = 0m });
            db.TaxRuleSets.Add(pk);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    // Tax rule sets ---------------------------------------------------------------------------------------------------

    public async Task<List<TaxRuleSet>> GetRuleSetsAsync()
    {
        await using var db = data.Create();
        var sets = await db.TaxRuleSets.AsNoTracking().Include(r => r.SalesTaxRates).Include(r => r.WithholdingRules).AsSplitQuery()
            .OrderBy(r => r.Name).ToListAsync();
        foreach (var s in sets)
        {
            s.SalesTaxRates = [.. s.SalesTaxRates.OrderByDescending(r => r.IsDefault).ThenByDescending(r => r.Rate).ThenBy(r => r.Name)];
            s.WithholdingRules = [.. s.WithholdingRules.OrderBy(r => r.Kind)];
        }
        return sets;
    }

    /// <summary>Adds or replaces a rule set with its rates (every rate and rule is saved as given).</summary>
    public async Task<TaxRuleSet> SaveRuleSetAsync(TaxRuleSet set)
    {
        var name = set.Name.Trim();
        if (name.Length is 0 or > 60) throw new BusinessRuleException("A rule set needs a name of up to 60 characters.");
        if (set.Country.Trim().Length != 2) throw new BusinessRuleException("The country is a two-letter code, such as PK.");
        if (set.SalesTaxRates.Any(r => r.Rate is < 0 or > 100 || string.IsNullOrWhiteSpace(r.Name)))
            throw new BusinessRuleException("Each sales tax rate has a name and a rate from 0 to 100%.");
        if (set.WithholdingRules.Any(r => r.FilerRate is < 0 or > 100 || r.NonFilerRate is < 0 or > 100 || r.Threshold < 0))
            throw new BusinessRuleException("Withholding rates are from 0 to 100%, and thresholds are not negative.");
        if (set.WithholdingRules.GroupBy(r => r.Kind).Any(g => g.Count() > 1))
            throw new BusinessRuleException("A rule set has one withholding rule per kind of payment.");
        await using var db = data.Create();
        if (await db.TaxRuleSets.AnyAsync(r => r.Name == name && r.Id != set.Id)) throw new BusinessRuleException($"There is already a rule set {name}.");
        var row = set.Id == Guid.Empty ? null
            : await db.TaxRuleSets.Include(r => r.SalesTaxRates).Include(r => r.WithholdingRules).SingleOrDefaultAsync(r => r.Id == set.Id);
        if (row is null)
        {
            row = new TaxRuleSet();
            db.TaxRuleSets.Add(row);
        }
        else
        {
            db.SalesTaxRates.RemoveRange(row.SalesTaxRates);
            db.WithholdingRules.RemoveRange(row.WithholdingRules);
            row.SalesTaxRates.Clear();
            row.WithholdingRules.Clear();
        }
        row.Name = name;
        row.Country = set.Country.Trim().ToUpperInvariant();
        row.InputTaxClaimable = set.InputTaxClaimable;
        row.Notes = string.IsNullOrWhiteSpace(set.Notes) ? null : set.Notes.Trim();
        row.IsActive = set.IsActive;
        bool defaulted = false;
        foreach (var r in set.SalesTaxRates)
        {
            row.SalesTaxRates.Add(new SalesTaxRate { Name = r.Name.Trim(), Rate = r.Rate, IsDefault = r.IsDefault && !defaulted });
            defaulted |= r.IsDefault;
        }
        foreach (var w in set.WithholdingRules)
            row.WithholdingRules.Add(new WithholdingRule
            {
                Kind = w.Kind, Section = string.IsNullOrWhiteSpace(w.Section) ? null : w.Section.Trim(), FilerRate = w.FilerRate, NonFilerRate = w.NonFilerRate,
                Threshold = w.Threshold,
            });
        await db.SaveChangesAsync();
        return row;
    }

    public async Task DeleteRuleSetAsync(Guid id)
    {
        await using var db = data.Create();
        var row = await db.TaxRuleSets.SingleOrDefaultAsync(r => r.Id == id) ?? throw new BusinessRuleException("That rule set no longer exists.");
        if (await db.StoreFinances.AnyAsync(s => s.TaxRuleSetId == id)) throw new BusinessRuleException($"A store uses {row.Name}; give it another rule set first.");
        db.TaxRuleSets.Remove(row);
        await db.SaveChangesAsync();
    }

    /// <summary>The rule set a store works with: its own choice, else the first active one of the ledger's country, else none.</summary>
    public async Task<TaxRuleSet?> RuleSetForStoreAsync(Guid storeId)
    {
        await using var db = data.Create();
        return await RuleSetForStoreAsync(db, storeId);
    }

    internal static async Task<TaxRuleSet?> RuleSetForStoreAsync(RetailPlusDbContext db, Guid storeId)
    {
        var chosen = await db.StoreFinances.AsNoTracking().Where(s => s.StoreId == storeId).Select(s => s.TaxRuleSetId).FirstOrDefaultAsync();
        var sets = db.TaxRuleSets.AsNoTracking().Include(r => r.SalesTaxRates).Include(r => r.WithholdingRules).AsSplitQuery();
        if (chosen is Guid id && await sets.SingleOrDefaultAsync(r => r.Id == id) is { } own) return own;
        var country = await db.LedgerSettings.AsNoTracking().Select(s => s.Country).FirstOrDefaultAsync() ?? "PK";
        return await sets.Where(r => r.Country == country && r.IsActive).OrderBy(r => r.Name).FirstOrDefaultAsync();
    }

    // Stores and suppliers --------------------------------------------------------------------------------------------

    public async Task<StoreFinance> GetStoreAsync(Guid storeId)
    {
        await using var db = data.Create();
        return await db.StoreFinances.AsNoTracking().FirstOrDefaultAsync(s => s.StoreId == storeId) ?? new StoreFinance { StoreId = storeId };
    }

    public async Task<StoreFinance> SaveStoreAsync(StoreFinance store)
    {
        await using var db = data.Create();
        if (!await db.Stores.AnyAsync(s => s.Id == store.StoreId)) throw new BusinessRuleException("That store no longer exists.");
        if (store.TaxRuleSetId is Guid set && !await db.TaxRuleSets.AnyAsync(r => r.Id == set)) throw new BusinessRuleException("That rule set no longer exists.");
        if (store.DepositBankAccountId is Guid bank && !await db.BankAccounts.AnyAsync(b => b.Id == bank && b.IsActive))
            throw new BusinessRuleException("That bank account no longer exists or is inactive.");
        var row = await db.StoreFinances.FirstOrDefaultAsync(s => s.StoreId == store.StoreId);
        if (row is null) db.StoreFinances.Add(row = new StoreFinance { StoreId = store.StoreId });
        row.TaxRuleSetId = store.TaxRuleSetId;
        row.DepositBankAccountId = store.DepositBankAccountId;
        await db.SaveChangesAsync();
        return row;
    }

    public async Task<SupplierFinance> GetSupplierAsync(Guid supplierId)
    {
        await using var db = data.Create();
        return await db.SupplierFinances.AsNoTracking().FirstOrDefaultAsync(s => s.SupplierId == supplierId) ?? new SupplierFinance { SupplierId = supplierId };
    }

    public async Task<SupplierFinance> SaveSupplierAsync(SupplierFinance supplier)
    {
        if (supplier.PaymentTermsDays is < 0 or > 365) throw new BusinessRuleException("Payment terms are 0 to 365 days.");
        await using var db = data.Create();
        if (!await db.Suppliers.AnyAsync(s => s.Id == supplier.SupplierId)) throw new BusinessRuleException("That supplier no longer exists.");
        if (supplier.DefaultAccountId is Guid account && !await db.LedgerAccounts.AnyAsync(a => a.Id == account && a.IsPosting))
            throw new BusinessRuleException("The default account must be an account that is posted to.");
        var row = await db.SupplierFinances.FirstOrDefaultAsync(s => s.SupplierId == supplier.SupplierId);
        if (row is null) db.SupplierFinances.Add(row = new SupplierFinance { SupplierId = supplier.SupplierId });
        row.TaxNumber = Trim(supplier.TaxNumber, 20);
        row.SalesTaxNumber = Trim(supplier.SalesTaxNumber, 20);
        row.IsFiler = supplier.IsFiler;
        row.WithholdingKind = supplier.WithholdingKind;
        row.WithholdingExempt = supplier.WithholdingExempt;
        row.PaymentTermsDays = supplier.PaymentTermsDays;
        row.DefaultAccountId = supplier.DefaultAccountId;
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>The withholding a payment to the supplier for the store's bills carries (rate 0 when exempt or no rule).</summary>
    public async Task<WithholdingQuote> WithholdingForAsync(Guid supplierId, Guid storeId)
    {
        await using var db = data.Create();
        return await WithholdingForAsync(db, supplierId, storeId);
    }

    internal static async Task<WithholdingQuote> WithholdingForAsync(RetailPlusDbContext db, Guid supplierId, Guid storeId)
    {
        var profile = await db.SupplierFinances.AsNoTracking().FirstOrDefaultAsync(s => s.SupplierId == supplierId) ?? new SupplierFinance();
        var set = await RuleSetForStoreAsync(db, storeId);
        var rule = set?.WithholdingRules.FirstOrDefault(r => r.Kind == profile.WithholdingKind);
        if (rule is null || profile.WithholdingExempt) return new WithholdingQuote(profile.WithholdingKind, 0, 0, rule?.Section, set?.Name);
        return new WithholdingQuote(profile.WithholdingKind, profile.IsFiler ? rule.FilerRate : rule.NonFilerRate, rule.Threshold, rule.Section, set?.Name);
    }

    // The daily journal's account mapping -----------------------------------------------------------------------------

    /// <summary>
    /// Saves a store's daily-journal mapping with accounts from the chart: each code must be an active account that is
    /// posted to. The mapping stays the one the CSV export uses (code and name), so the export goes on working.
    /// </summary>
    public async Task SaveMappingAsync(Guid storeId, IEnumerable<AccountMapping> rows)
    {
        List<LedgerAccount> chart;
        await using (var db = data.Create())
            chart = await db.LedgerAccounts.AsNoTracking().ToListAsync();
        var byCode = chart.ToDictionary(a => a.Code, StringComparer.Ordinal);
        var list = rows.ToList();
        foreach (var row in list)
        {
            var code = (row.AccountCode ?? "").Trim();
            if (code.Length == 0) continue;
            if (!byCode.TryGetValue(code, out var account)) throw new BusinessRuleException($"There is no account {code} in the chart of accounts.");
            if (!account.IsPosting || !account.IsActive) throw new BusinessRuleException($"{account.Code} {account.Name} is a header or inactive account; choose an account that is posted to.");
            row.AccountCode = account.Code;
            row.AccountName = account.Name.Length > 60 ? account.Name[..60] : account.Name;
        }
        await new AccountingService(data.For(new StationIdentity(storeId, data.Station?.StationNumber ?? 0), data.CurrentUser)).SaveMappingsAsync(list);
    }

    private static string? Trim(string? text, int length)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        return t.Length <= length ? t : t[..length];
    }
}

/// <summary>Numbers of the finance documents, taken from the server's own range like the ledger's entries.</summary>
internal static class FinanceNumbers
{
    public static async Task<long> NextAsync(DataContext data, string sequence, CancellationToken cancellationToken = default)
    {
        var store = data.Station?.StoreId ?? Guid.Empty;
        await using var db = data.For(new StationIdentity(store, LedgerService.LedgerStation), data.CurrentUser).Create();
        return await db.NextNumberAsync(sequence, cancellationToken);
    }

    /// <summary>The base currency when <paramref name="currency"/> is empty, else the code in capitals.</summary>
    public static async Task<string> CurrencyAsync(RetailPlusDbContext db, string? currency)
    {
        var base_ = await db.LedgerSettings.AsNoTracking().Select(s => s.BaseCurrency).FirstOrDefaultAsync() ?? "PKR";
        var c = (currency ?? "").Trim().ToUpperInvariant();
        return c.Length == 0 ? base_ : c;
    }

    public static async Task<string> BaseCurrencyAsync(RetailPlusDbContext db) =>
        await db.LedgerSettings.AsNoTracking().Select(s => s.BaseCurrency).FirstOrDefaultAsync() ?? "PKR";

    public static string? Cut(string? text, int length) => text is null ? null : text.Length <= length ? text : text[..length];

    public static decimal Base(decimal amount, decimal rate) => Money.Round(amount * rate);
}
