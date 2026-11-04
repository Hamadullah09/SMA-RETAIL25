namespace RetailPlus.Core.Model;

/// <summary>What an account holds; it decides which side increases it and where it shows in the statements.</summary>
public enum AccountType
{
    Asset,
    Liability,
    Equity,
    Income,
    Expense,
}

/// <summary>
/// An account of the company's chart of accounts: one chart for every store, while each posting carries its store. The
/// accounts form a tree; a header account (<see cref="IsPosting"/> false) only adds up the accounts under it. The ledger
/// lives on the server and is never synced to the tills.
/// </summary>
public class LedgerAccount : Entity, ILocalOnly
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public AccountType Type { get; set; }
    public Guid? ParentId { get; set; }
    public LedgerAccount? Parent { get; set; }
    /// <summary>False for a header account, which cannot be posted to.</summary>
    public bool IsPosting { get; set; } = true;
    public bool IsActive { get; set; } = true;
    /// <summary>One of <see cref="LedgerRoles"/> when the program posts to this account by itself; at most one account each.</summary>
    public string? Role { get; set; }
    public string? Description { get; set; }
    /// <summary>Where its movement shows in the cash flow statement; null: equity under financing, everything else operating.</summary>
    public CashFlowSection? CashFlow { get; set; }
}

/// <summary>
/// The accounts the program finds by what they are for rather than by code, so each company may number its chart as
/// it likes. Names are stored, so they never change once released; new ones are added at the end.
/// </summary>
public static class LedgerRoles
{
    public const string Suspense = "Suspense";
    public const string RetainedEarnings = "RetainedEarnings";
    public const string Cash = "Cash";
    public const string Bank = "Bank";
    public const string AccountsReceivable = "AccountsReceivable";
    public const string Inventory = "Inventory";
    public const string InventoryInTransit = "InventoryInTransit";
    public const string InputTax = "InputTax";
    public const string EmployeeAdvances = "EmployeeAdvances";
    public const string WorkInProgress = "WorkInProgress";
    public const string AccountsPayable = "AccountsPayable";
    public const string PurchasesClearing = "PurchasesClearing";
    public const string SalesTaxPayable = "SalesTaxPayable";
    public const string GiftCardLiability = "GiftCardLiability";
    public const string SalariesPayable = "SalariesPayable";
    public const string PayrollTaxPayable = "PayrollTaxPayable";
    public const string StatutoryContributionsPayable = "StatutoryContributionsPayable";
    public const string Sales = "Sales";
    public const string Discounts = "Discounts";
    public const string OtherIncome = "OtherIncome";
    public const string CostOfGoods = "CostOfGoods";
    public const string StockAdjustments = "StockAdjustments";
    public const string GiftCardAdjustments = "GiftCardAdjustments";
    public const string Rounding = "Rounding";
    public const string SalariesExpense = "SalariesExpense";
    public const string EmployerContributionsExpense = "EmployerContributionsExpense";
    public const string ManufacturingOverhead = "ManufacturingOverhead";
    public const string ProductionVariance = "ProductionVariance";
    public const string LandedCostsClearing = "LandedCostsClearing";
    public const string ExchangeDifferences = "ExchangeDifferences";
    public const string GeneralExpenses = "GeneralExpenses";
    /// <summary>Income tax withheld from supplier payments, owed to the tax office.</summary>
    public const string WithholdingTaxPayable = "WithholdingTaxPayable";
    /// <summary>Bank charges found on bank statements.</summary>
    public const string BankCharges = "BankCharges";

    public static IReadOnlyList<string> All { get; } =
    [
        Suspense, RetainedEarnings, Cash, Bank, AccountsReceivable, Inventory, InventoryInTransit, InputTax, EmployeeAdvances,
        WorkInProgress, AccountsPayable, PurchasesClearing, SalesTaxPayable, GiftCardLiability, SalariesPayable, PayrollTaxPayable,
        StatutoryContributionsPayable, Sales, Discounts, OtherIncome, CostOfGoods, StockAdjustments, GiftCardAdjustments, Rounding,
        SalariesExpense, EmployerContributionsExpense, ManufacturingOverhead, ProductionVariance, LandedCostsClearing,
        ExchangeDifferences, GeneralExpenses, WithholdingTaxPayable, BankCharges,
    ];
}

/// <summary>What made a journal entry.</summary>
public enum JournalKind
{
    /// <summary>Entered by hand (with approval).</summary>
    Manual,
    /// <summary>A store's business day from the tills: sales, payments on account, stock movements, gift cards.</summary>
    Daily,
    SupplierBill,
    SupplierPayment,
    Expense,
    Bank,
    Payroll,
    Production,
    Inventory,
    Opening,
    Closing,
    /// <summary>The difference posted when a day changed after its period was locked.</summary>
    Adjustment,
    Reversal,
}

public enum JournalStatus
{
    /// <summary>Entered and waiting for approval; not in any balance.</summary>
    Draft,
    Posted,
    /// <summary>Posted, then cancelled by a reversing entry.</summary>
    Reversed,
}

/// <summary>Who a journal line is about, for the sub-ledgers (a client's, a supplier's or an employee's balance).</summary>
public enum LedgerPartyKind
{
    Client,
    Supplier,
    Employee,
}

/// <summary>
/// One entry of the general ledger: lines whose debits equal their credits. Entries the program posts carry a
/// <see cref="Source"/> (a store's day, a supplier bill, a payroll run), so posting the same thing again replaces the
/// entry instead of adding a second one.
/// </summary>
public class JournalEntry : Entity, ILocalOnly
{
    /// <summary>The entry's number, given when it is posted (0 while a draft).</summary>
    public long Number { get; set; }
    public DateOnly Date { get; set; }
    public JournalKind Kind { get; set; }
    public JournalStatus Status { get; set; }
    /// <summary>What it was posted for ("daily:{store}:{date}", "bill:{id}"); unique; null for entries made by hand.</summary>
    public string? Source { get; set; }
    /// <summary>The store the entry is for, or null for the company as a whole. Each line has its own store too.</summary>
    public Guid? StoreId { get; set; }
    public string Memo { get; set; } = "";
    /// <summary>The document it comes from, as people know it (a bill or invoice number).</summary>
    public string? Reference { get; set; }
    public string? CreatedBy { get; set; }
    public Guid? CreatedById { get; set; }
    public DateTime? PostedAtUtc { get; set; }
    public string? PostedBy { get; set; }
    public Guid? PostedById { get; set; }
    /// <summary>The entry this one reverses.</summary>
    public Guid? ReversalOfId { get; set; }
    /// <summary>The entry that reversed this one.</summary>
    public Guid? ReversedById { get; set; }
    public List<JournalLine> Lines { get; set; } = [];
}

/// <summary>One line of a journal entry: a debit or a credit to one account, for one store.</summary>
public class JournalLine : Entity, ILocalOnly
{
    public Guid JournalEntryId { get; set; }
    public int LineNumber { get; set; }
    public Guid AccountId { get; set; }
    public LedgerAccount? Account { get; set; }
    public Guid? StoreId { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Memo { get; set; }
    public LedgerPartyKind? PartyKind { get; set; }
    public Guid? PartyId { get; set; }
}

public enum FiscalStatus
{
    Open,
    /// <summary>No postings for now (month-end review); can be opened again.</summary>
    Locked,
    /// <summary>Year closed; its profit moved to retained earnings.</summary>
    Closed,
}

/// <summary>A financial year of the company, with one period per month.</summary>
public class FiscalYear : Entity, ILocalOnly
{
    public string Name { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public FiscalStatus Status { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public string? ClosedBy { get; set; }
    public List<FiscalPeriod> Periods { get; set; } = [];
}

/// <summary>A month of a <see cref="FiscalYear"/>: postings dated in it are allowed only while it is open.</summary>
public class FiscalPeriod : Entity, ILocalOnly
{
    public Guid FiscalYearId { get; set; }
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public FiscalStatus Status { get; set; }
}

/// <summary>The company's ledger settings: one row.</summary>
public class LedgerSettings : Entity, ILocalOnly
{
    /// <summary>The currency the books are kept in (ISO code, e.g. PKR).</summary>
    public string BaseCurrency { get; set; } = "PKR";
    /// <summary>The month a financial year starts in (Pakistan: 7, July).</summary>
    public int FiscalYearStartMonth { get; set; } = 7;
    /// <summary>The country whose rule sets (taxes, payroll) are used unless a store names another (ISO code, e.g. PK).</summary>
    public string Country { get; set; } = "PK";
    /// <summary>Entries made by hand wait for a second person to approve them.</summary>
    public bool ManualEntriesNeedApproval { get; set; } = true;
}
