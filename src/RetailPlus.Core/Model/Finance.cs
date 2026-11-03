namespace RetailPlus.Core.Model;

// Finance and accounting (phase E1): payables, expenses, bank and cash, budgets, country tax rules, the FBR queue. Every
// table here lives on the server only (ILocalOnly) and is never synced to the tills; whatever moves money is posted to
// the general ledger through LedgerService.PostAsync.

/// <summary>Where an account's movement shows in the cash flow statement (indirect method).</summary>
public enum CashFlowSection
{
    Operating,
    Investing,
    Financing,
}

/// <summary>The finance module's own settings next to <see cref="LedgerSettings"/>: one row.</summary>
public class FinanceSettings : Entity, ILocalOnly
{
    /// <summary>Post a store's business days as soon as a package from one of its tills is applied.</summary>
    public bool AutoPostOnSync { get; set; } = true;
    /// <summary>Every night the last this many days of every store are posted again (late packages, corrections).</summary>
    public int NightlyDays { get; set; } = 7;
    /// <summary>The hour (server time, 0–23) the nightly posting runs.</summary>
    public int NightlyHour { get; set; } = 2;
    /// <summary>The largest receipt or attachment accepted, in kilobytes.</summary>
    public int AttachmentMaxKilobytes { get; set; } = 5120;
    /// <summary>
    /// How far the server's changes were looked at for days to post: its change number on SQL Server (rows from the tills
    /// keep the tills' times, so times cannot tell what arrived), the time in ticks elsewhere. 0 before the first look.
    /// </summary>
    public long SyncCursor { get; set; }
    public DateOnly? LastNightlyRunOn { get; set; }
    /// <summary>How far sales were looked at for the FBR queue (as <see cref="SyncCursor"/>).</summary>
    public long FiscalCursor { get; set; }
}

// Country rules -------------------------------------------------------------------------------------------------------

/// <summary>What a supplier payment is for, which decides the withholding tax rate.</summary>
public enum WithholdingKind
{
    Goods,
    Services,
    Rent,
    Other,
}

/// <summary>
/// A country's tax rules as data: sales tax rates, whether input tax on purchases may be claimed, and withholding tax on
/// supplier payments. Pakistan is seeded; another country is another rule set. Each store uses one (the ledger's country's
/// by default).
/// </summary>
public class TaxRuleSet : Entity, ILocalOnly
{
    /// <summary>ISO country code, e.g. PK.</summary>
    public string Country { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Input tax on bills and expenses is recovered (posted to Input tax); otherwise it is part of the cost.</summary>
    public bool InputTaxClaimable { get; set; } = true;
    /// <summary>Where the rates come from and when they were last checked.</summary>
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public List<SalesTaxRate> SalesTaxRates { get; set; } = [];
    public List<WithholdingRule> WithholdingRules { get; set; } = [];
}

/// <summary>One sales tax rate of a rule set (standard, reduced, zero).</summary>
public class SalesTaxRate : Entity, ILocalOnly
{
    public Guid TaxRuleSetId { get; set; }
    public string Name { get; set; } = "";
    public decimal Rate { get; set; }
    /// <summary>The rate offered first on bills and expenses.</summary>
    public bool IsDefault { get; set; }
}

/// <summary>Withholding tax on a supplier payment of one kind, for suppliers on the tax roll (filers) and those not.</summary>
public class WithholdingRule : Entity, ILocalOnly
{
    public Guid TaxRuleSetId { get; set; }
    public WithholdingKind Kind { get; set; }
    /// <summary>The law's reference, e.g. "153(1)(a)".</summary>
    public string? Section { get; set; }
    public decimal FilerRate { get; set; }
    public decimal NonFilerRate { get; set; }
    /// <summary>Payments below this amount are not taxed (0: every payment).</summary>
    public decimal Threshold { get; set; }
}

/// <summary>A store's finance choices: its tax rule set and the bank account its till cash is deposited to.</summary>
public class StoreFinance : Entity, ILocalOnly
{
    public Guid StoreId { get; set; }
    public Guid? TaxRuleSetId { get; set; }
    public Guid? DepositBankAccountId { get; set; }
}

/// <summary>A supplier's tax and payment details, kept on the server next to the supplier the tills know.</summary>
public class SupplierFinance : Entity, ILocalOnly
{
    public Guid SupplierId { get; set; }
    /// <summary>National tax number (NTN in Pakistan).</summary>
    public string? TaxNumber { get; set; }
    /// <summary>Sales tax registration number (STRN).</summary>
    public string? SalesTaxNumber { get; set; }
    /// <summary>On the active taxpayers list: the filer rate of withholding applies.</summary>
    public bool IsFiler { get; set; } = true;
    public WithholdingKind WithholdingKind { get; set; } = WithholdingKind.Goods;
    /// <summary>Holds an exemption certificate: no tax is withheld.</summary>
    public bool WithholdingExempt { get; set; }
    /// <summary>Days from the bill date to its due date.</summary>
    public int PaymentTermsDays { get; set; } = 30;
    /// <summary>The account a new bill line of this supplier starts with.</summary>
    public Guid? DefaultAccountId { get; set; }
}

// Accounts payable ----------------------------------------------------------------------------------------------------

public enum SupplierBillKind
{
    Bill,
    /// <summary>Goods returned or an overcharge credited by the supplier: a bill the other way round.</summary>
    DebitNote,
}

public enum SupplierBillStatus
{
    Draft,
    Posted,
    PartPaid,
    Paid,
    /// <summary>Cancelled: its ledger entry was reversed.</summary>
    Void,
}

/// <summary>
/// A supplier's bill (or debit note). Its lines are expenses (an account), stock (an item) or goods received on a purchase
/// order (posted to goods received, not billed). Posting it puts it in the ledger: Dr the lines and the input tax, Cr
/// accounts payable for the supplier. Amounts are in the bill's currency; <see cref="ExchangeRate"/> turns them into the
/// ledger's.
/// </summary>
public class SupplierBill : Entity, ILocalOnly, IStoreScoped, IVersioned
{
    public long Number { get; set; }
    public SupplierBillKind Kind { get; set; }
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public Guid StoreId { get; set; }
    public DateOnly BillDate { get; set; }
    public DateOnly DueDate { get; set; }
    /// <summary>The number on the supplier's own invoice.</summary>
    public string? SupplierReference { get; set; }
    /// <summary>ISO code; the ledger's base currency when empty.</summary>
    public string Currency { get; set; } = "";
    /// <summary>Base currency per unit of <see cref="Currency"/> (1 for the base currency).</summary>
    public decimal ExchangeRate { get; set; } = 1;
    public string? Notes { get; set; }
    public SupplierBillStatus Status { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }
    /// <summary>Paid or settled so far (payments, withholding, debit notes applied).</summary>
    public decimal Paid { get; set; }
    /// <summary>For a debit note: the bill it corrects, if any.</summary>
    public Guid? OriginalBillId { get; set; }
    /// <summary>Made by an expense paid on account.</summary>
    public Guid? ExpenseId { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? PostedAtUtc { get; set; }
    public string? PostedBy { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    public string? VoidReason { get; set; }
    public Guid Version { get; set; }
    public List<SupplierBillLine> Lines { get; set; } = [];

    public decimal Open => Total - Paid;
}

/// <summary>
/// A bill line: either an account (an expense) or an item (stock), with quantity, price and tax. A line received on a
/// purchase order carries its links, so the three-way match (phase E2) can find it; such a line is posted to goods
/// received, not billed, which the receipt credited.
/// </summary>
public class SupplierBillLine : Entity, ILocalOnly
{
    public Guid SupplierBillId { get; set; }
    public int LineNumber { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? ItemId { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    /// <summary>Quantity × price, before tax.</summary>
    public decimal Amount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    public Guid? PurchaseOrderLineId { get; set; }
}

public enum SupplierPaymentStatus
{
    Posted,
    Void,
}

/// <summary>
/// Money paid to a supplier from a bank or cash account, allocated to bills; the withholding tax kept back is owed to the
/// tax office. Dr accounts payable (what the bills are settled by), Cr the bank (what was paid), Cr withholding tax
/// payable.
/// </summary>
public class SupplierPayment : Entity, ILocalOnly, IStoreScoped
{
    public long Number { get; set; }
    public Guid SupplierId { get; set; }
    public Guid StoreId { get; set; }
    public DateOnly Date { get; set; }
    public Guid BankAccountId { get; set; }
    /// <summary>The bank account's currency; bills paid together are all in it.</summary>
    public string Currency { get; set; } = "";
    public decimal ExchangeRate { get; set; } = 1;
    /// <summary>What left the bank.</summary>
    public decimal Amount { get; set; }
    public decimal WithholdingAmount { get; set; }
    public WithholdingKind WithholdingKind { get; set; }
    public decimal WithholdingRate { get; set; }
    /// <summary>Cheque or transfer number.</summary>
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public SupplierPaymentStatus Status { get; set; }
    public Guid? PaymentRunId { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    public List<SupplierPaymentAllocation> Allocations { get; set; } = [];
}

/// <summary>How much of a payment settles one bill (in the bill's currency, withholding included).</summary>
public class SupplierPaymentAllocation : Entity, ILocalOnly
{
    public Guid SupplierPaymentId { get; set; }
    public Guid SupplierBillId { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>Bills chosen and paid together, one payment per supplier.</summary>
public class PaymentRun : Entity, ILocalOnly
{
    public long Number { get; set; }
    public DateOnly Date { get; set; }
    public Guid BankAccountId { get; set; }
    public decimal Total { get; set; }
    public int Payments { get; set; }
    public string? Notes { get; set; }
    public string? PaidBy { get; set; }
    public DateTime PaidAtUtc { get; set; }
}

// Expenses ------------------------------------------------------------------------------------------------------------

public enum ExpensePaidBy
{
    Cash,
    Bank,
    /// <summary>Owed to the payee: a supplier bill is made for it.</summary>
    OnAccount,
}

public enum ExpenseStatus
{
    Draft,
    Posted,
    Void,
}

/// <summary>A cost paid or owed (rent, electricity), with its receipt attached.</summary>
public class Expense : Entity, ILocalOnly, IStoreScoped, IVersioned
{
    public long Number { get; set; }
    public DateOnly Date { get; set; }
    public Guid StoreId { get; set; }
    public string Payee { get; set; } = "";
    /// <summary>Needed when it is paid on account (the bill is the supplier's).</summary>
    public Guid? SupplierId { get; set; }
    public Guid AccountId { get; set; }
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public decimal TaxAmount { get; set; }
    public ExpensePaidBy PaidBy { get; set; }
    /// <summary>The bank or cash account it was paid from; none for cash from the store's till money (Cash in hand).</summary>
    public Guid? BankAccountId { get; set; }
    public string? Reference { get; set; }
    public ExpenseStatus Status { get; set; }
    public Guid? SupplierBillId { get; set; }
    public Guid? RecurringExpenseId { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? CreatedBy { get; set; }
    public Guid Version { get; set; }

    public decimal Total => Amount + TaxAmount;
}

public enum RecurrenceFrequency
{
    Weekly,
    Monthly,
    Quarterly,
    Yearly,
}

/// <summary>An expense made again on a schedule (monthly rent), as a draft or posted.</summary>
public class RecurringExpense : Entity, ILocalOnly, IStoreScoped
{
    public string Name { get; set; } = "";
    public Guid StoreId { get; set; }
    public string Payee { get; set; } = "";
    public Guid? SupplierId { get; set; }
    public Guid AccountId { get; set; }
    public decimal Amount { get; set; }
    public decimal TaxAmount { get; set; }
    public ExpensePaidBy PaidBy { get; set; }
    public Guid? BankAccountId { get; set; }
    public RecurrenceFrequency Frequency { get; set; } = RecurrenceFrequency.Monthly;
    public DateOnly NextDate { get; set; }
    public DateOnly? EndDate { get; set; }
    /// <summary>Post each one at once; otherwise it waits as a draft.</summary>
    public bool PostAutomatically { get; set; }
    public bool IsActive { get; set; } = true;
    public DateOnly? LastMadeOn { get; set; }
}

/// <summary>What an attachment belongs to.</summary>
public enum AttachmentOwner
{
    Expense,
    JournalEntry,
    SupplierBill,
}

/// <summary>A receipt picture or PDF kept in the server's database, with a size limit (<see cref="FinanceSettings"/>).</summary>
public class FinanceAttachment : Entity, ILocalOnly
{
    public AttachmentOwner OwnerKind { get; set; }
    public Guid OwnerId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public int Size { get; set; }
    public byte[] Data { get; set; } = [];
    public string? UploadedBy { get; set; }
}

// Bank and cash -------------------------------------------------------------------------------------------------------

public enum BankAccountKind
{
    Bank,
    /// <summary>A cash box or petty cash, kept like a bank account.</summary>
    Cash,
    /// <summary>A mobile wallet or card settlement account.</summary>
    Wallet,
}

/// <summary>A bank or cash account of the company, kept in its own ledger account (an asset).</summary>
public class BankAccount : Entity, ILocalOnly, IVersioned
{
    public string Name { get; set; } = "";
    public BankAccountKind Kind { get; set; }
    public string? BankName { get; set; }
    public string? AccountNumber { get; set; }
    public string? Iban { get; set; }
    /// <summary>ISO code; the ledger's base currency when empty.</summary>
    public string Currency { get; set; } = "";
    public Guid LedgerAccountId { get; set; }
    public LedgerAccount? LedgerAccount { get; set; }
    /// <summary>Null: the company's; else the store whose account it is.</summary>
    public Guid? StoreId { get; set; }
    public decimal OpeningBalance { get; set; }
    public DateOnly? OpeningDate { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid Version { get; set; }
}

/// <summary>Money moved from one bank or cash account to another.</summary>
public class BankTransfer : Entity, ILocalOnly
{
    public long Number { get; set; }
    public DateOnly Date { get; set; }
    public Guid FromAccountId { get; set; }
    public Guid ToAccountId { get; set; }
    /// <summary>In the paying account's currency.</summary>
    public decimal Amount { get; set; }
    /// <summary>What arrived, in the receiving account's currency (the same as <see cref="Amount"/> in one currency).</summary>
    public decimal ToAmount { get; set; }
    public string? Reference { get; set; }
    public string? Memo { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? CreatedBy { get; set; }
}

/// <summary>Till cash taken to the bank: Cash in hand to a bank account, usually one End of Day's takings.</summary>
public class BankDeposit : Entity, ILocalOnly, IStoreScoped
{
    public long Number { get; set; }
    public DateOnly Date { get; set; }
    public Guid StoreId { get; set; }
    public Guid BankAccountId { get; set; }
    public decimal Amount { get; set; }
    /// <summary>The End of Day (drawer session) whose cash this is; a session is deposited once.</summary>
    public Guid? DrawerSessionId { get; set; }
    public string? Reference { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? CreatedBy { get; set; }
}

/// <summary>
/// How one bank's statement CSV is read, saved per bank so the next statement is read the same way. Columns are header
/// names, or 1-based column numbers when the file has no header.
/// </summary>
public class BankStatementFormat : Entity, ILocalOnly
{
    public string BankName { get; set; } = "";
    public bool HasHeader { get; set; } = true;
    public string Delimiter { get; set; } = ",";
    public string DateColumn { get; set; } = "";
    /// <summary>.NET date format, e.g. dd/MM/yyyy.</summary>
    public string DateFormat { get; set; } = "yyyy-MM-dd";
    public string? DescriptionColumn { get; set; }
    public string? ReferenceColumn { get; set; }
    /// <summary>One signed amount column (money in positive)…</summary>
    public string? AmountColumn { get; set; }
    /// <summary>…or a money-out and a money-in column.</summary>
    public string? DebitColumn { get; set; }
    public string? CreditColumn { get; set; }
    public string? BalanceColumn { get; set; }
}

/// <summary>A bank statement brought in from a CSV file.</summary>
public class BankStatement : Entity, ILocalOnly
{
    public Guid BankAccountId { get; set; }
    public string? FileName { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    /// <summary>The balance after the last line, when the file gives balances.</summary>
    public decimal? ClosingBalance { get; set; }
    public int LineCount { get; set; }
    public string? ImportedBy { get; set; }
    public List<BankStatementLine> Lines { get; set; } = [];
}

public enum StatementLineStatus
{
    Unmatched,
    /// <summary>Matched to a ledger line of the bank's account.</summary>
    Matched,
    /// <summary>An entry was made for it (bank charges, profit) and matched.</summary>
    Entered,
    /// <summary>Set aside by hand (not in the ledger, not to be).</summary>
    Ignored,
}

/// <summary>A line of a bank statement and what it was matched to.</summary>
public class BankStatementLine : Entity, ILocalOnly
{
    public Guid BankStatementId { get; set; }
    public Guid BankAccountId { get; set; }
    public int LineNumber { get; set; }
    public DateOnly Date { get; set; }
    public string? Description { get; set; }
    public string? Reference { get; set; }
    /// <summary>Money in positive, money out negative.</summary>
    public decimal Amount { get; set; }
    public decimal? Balance { get; set; }
    public StatementLineStatus Status { get; set; }
    /// <summary>The journal line (on the bank's ledger account) it was matched to; each ledger line matches once.</summary>
    public Guid? JournalLineId { get; set; }
    public string? MatchedBy { get; set; }
    public DateTime? MatchedAtUtc { get; set; }
}

// Budgets -------------------------------------------------------------------------------------------------------------

/// <summary>A budget amount of one account for one month, for a store or (no store) the company.</summary>
public class BudgetLine : Entity, ILocalOnly
{
    public Guid FiscalYearId { get; set; }
    public Guid AccountId { get; set; }
    public Guid? StoreId { get; set; }
    /// <summary>The first day of the month.</summary>
    public DateOnly Month { get; set; }
    /// <summary>Debit positive (an expense, an asset), credit negative (income), as the ledger's balances.</summary>
    public decimal Amount { get; set; }
}

// FBR POS integration -------------------------------------------------------------------------------------------------

public enum FiscalInvoiceStatus
{
    Pending,
    Sent,
    /// <summary>Tried and refused or unreachable; tried again later.</summary>
    Failed,
    /// <summary>Not to be sent (a sale made before the integration was switched on, a store left out).</summary>
    Skipped,
}

/// <summary>
/// A sale waiting to be reported to the tax office (FBR's POS integration in Pakistan), and what came back. When it is
/// sent, the fiscal invoice number is also written on the sale, which takes it to the tills for reprints.
/// </summary>
public class FiscalInvoice : Entity, ILocalOnly, IStoreScoped
{
    public Guid SaleId { get; set; }
    public Guid StoreId { get; set; }
    public long InvoiceNumber { get; set; }
    public FiscalInvoiceStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTime? NextAttemptUtc { get; set; }
    public string? LastError { get; set; }
    public string? FiscalNumber { get; set; }
    public DateTime? SentAtUtc { get; set; }
}
