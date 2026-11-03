using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>One bill to pay and how much of it (in the bill's currency, withholding included).</summary>
public sealed record BillToPay(Guid BillId, decimal Amount);

/// <summary>
/// A payment to make: the bank or cash account, the bills (and debit notes) it settles, and, when given, the withholding
/// tax kept back (otherwise worked out from the store's rule set and the supplier's details).
/// </summary>
public sealed record PaymentDraft(Guid BankAccountId, DateOnly Date, IReadOnlyList<BillToPay> Bills)
{
    public decimal? WithholdingAmount { get; init; }
    /// <summary>Base currency per unit of the bank account's currency (1 in the base currency).</summary>
    public decimal ExchangeRate { get; init; } = 1;
    public string? Reference { get; init; }
    public string? Notes { get; init; }
}

/// <summary>A supplier's open amounts by how long they are overdue (in the base currency).</summary>
public sealed record AgeingRow(Guid PartyId, string Name, decimal NotDue, decimal Days1To30, decimal Days31To60, decimal Days61To90, decimal Over90)
{
    public decimal Total => NotDue + Days1To30 + Days31To60 + Days61To90 + Over90;
}

/// <summary>A line of a supplier's statement: what was billed (credit), paid or returned (debit), and the balance owed after it.</summary>
public sealed record StatementRow(DateOnly Date, long EntryNumber, JournalKind Kind, string Memo, string? Reference, decimal Billed, decimal Paid, decimal Balance);

/// <summary>A supplier's statement: what was owed before, the lines, and what is owed at the end.</summary>
public sealed record SupplierStatement(Guid SupplierId, string Supplier, DateOnly From, DateOnly To, decimal Opening, IReadOnlyList<StatementRow> Lines)
{
    public decimal Closing => Lines.Count == 0 ? Opening : Lines[^1].Balance;
}

/// <summary>
/// Accounts payable: supplier bills and debit notes (drafts, posting, voiding), payments allocated to bills with
/// withholding tax, payment runs, and the ageing, statements and bills due. Everything posted goes through
/// <see cref="LedgerService.PostAsync"/>, one entry per document (source <c>bill:{id}</c>, <c>supplier-payment:{id}</c>).
/// </summary>
public sealed class PayablesService(DataContext data)
{
    private LedgerService Ledger => new(data);

    // Bills -----------------------------------------------------------------------------------------------------------

    public async Task<SupplierBill?> GetBillAsync(Guid id)
    {
        await using var db = data.Create();
        var bill = await db.SupplierBills.AsNoTracking().Include(b => b.Lines).Include(b => b.Supplier).SingleOrDefaultAsync(b => b.Id == id);
        if (bill is not null) bill.Lines = [.. bill.Lines.OrderBy(l => l.LineNumber)];
        return bill;
    }

    /// <summary>
    /// Saves a draft bill or debit note (new when its id is empty): amounts are worked out from the lines (quantity × price,
    /// tax from the line's rate unless a tax amount is given without one). The due date follows the supplier's terms when
    /// it is not set. Only drafts change.
    /// </summary>
    public async Task<SupplierBill> SaveBillAsync(SupplierBill bill)
    {
        if (bill.Lines.Count == 0) throw new BusinessRuleException("A bill needs at least one line.");
        await using var db = data.Create();
        var supplier = await db.Suppliers.AsNoTracking().SingleOrDefaultAsync(s => s.Id == bill.SupplierId) ?? throw new BusinessRuleException("Choose the supplier.");
        if (!await db.Stores.AnyAsync(s => s.Id == bill.StoreId)) throw new BusinessRuleException("Choose the store the bill is for.");
        var currency = await FinanceNumbers.CurrencyAsync(db, bill.Currency);
        bool foreign = currency != await FinanceNumbers.BaseCurrencyAsync(db);
        if (foreign && bill.ExchangeRate <= 0) throw new BusinessRuleException($"Give the exchange rate of {currency}.");
        var accounts = await db.LedgerAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id);
        foreach (var line in bill.Lines)
        {
            if (line.PurchaseOrderId is null && line.ItemId is null)
            {
                if (line.AccountId is not Guid a || !accounts.TryGetValue(a, out var account)) throw new BusinessRuleException("Each line needs an account or an item.");
                if (!account.IsPosting || !account.IsActive) throw new BusinessRuleException($"{account.Code} {account.Name} is a header or inactive account.");
            }
            if (line.Quantity == 0) throw new BusinessRuleException("A line's quantity cannot be zero.");
            if (line.UnitPrice < 0) throw new BusinessRuleException("Prices are entered as positive amounts; use a debit note for a credit.");
            if (line.TaxRate is < 0 or > 100) throw new BusinessRuleException("A tax rate is from 0 to 100%.");
        }

        SupplierBill row;
        if (bill.Id == Guid.Empty)
        {
            row = new SupplierBill { Kind = bill.Kind, Status = SupplierBillStatus.Draft, CreatedBy = FinanceNumbers.Cut(data.CurrentUser?.UserName, 50) };
            row.Id = Guid.CreateVersion7();
            db.SupplierBills.Add(row);
        }
        else
        {
            row = await db.SupplierBills.Include(b => b.Lines).SingleOrDefaultAsync(b => b.Id == bill.Id) ?? throw new BusinessRuleException("That bill no longer exists.");
            if (row.Status != SupplierBillStatus.Draft) throw new BusinessRuleException($"{Describe(row)} is posted; void it to change it.");
            if (bill.Version != Guid.Empty && bill.Version != row.Version) throw new ConcurrencyConflictException("Someone else changed this bill. Load it again.");
            db.SupplierBillLines.RemoveRange(row.Lines);
            row.Lines.Clear();
        }
        var terms = await db.SupplierFinances.AsNoTracking().Where(s => s.SupplierId == bill.SupplierId).Select(s => (int?)s.PaymentTermsDays).FirstOrDefaultAsync() ?? 30;
        row.SupplierId = supplier.Id;
        row.StoreId = bill.StoreId;
        row.BillDate = bill.BillDate;
        row.DueDate = bill.DueDate == default || bill.DueDate < bill.BillDate ? bill.BillDate.AddDays(terms) : bill.DueDate;
        row.SupplierReference = FinanceNumbers.Cut(bill.SupplierReference?.Trim(), 40);
        row.Currency = currency;
        row.ExchangeRate = foreign ? bill.ExchangeRate : 1;
        row.Notes = FinanceNumbers.Cut(bill.Notes?.Trim(), 400);
        row.OriginalBillId = bill.Kind == SupplierBillKind.DebitNote ? bill.OriginalBillId : null;
        row.ExpenseId = bill.ExpenseId;
        int n = 0;
        foreach (var line in bill.Lines)
        {
            decimal amount = Money.Round(line.Quantity * line.UnitPrice);
            decimal tax = line.TaxRate == 0 ? Money.Round(line.TaxAmount) : Money.Round(amount * line.TaxRate / 100m);
            row.Lines.Add(new SupplierBillLine
            {
                SupplierBillId = row.Id, LineNumber = ++n, AccountId = line.AccountId, ItemId = line.ItemId,
                Description = FinanceNumbers.Cut((line.Description ?? "").Trim(), 200)!, Quantity = line.Quantity, UnitPrice = line.UnitPrice,
                Amount = amount, TaxRate = line.TaxRate, TaxAmount = tax, PurchaseOrderId = line.PurchaseOrderId, PurchaseOrderLineId = line.PurchaseOrderLineId,
            });
        }
        row.Subtotal = row.Lines.Sum(l => l.Amount);
        row.TaxTotal = row.Lines.Sum(l => l.TaxAmount);
        row.Total = row.Subtotal + row.TaxTotal;
        if (row.Total <= 0) throw new BusinessRuleException("A bill's total must be more than zero.");
        await db.SaveChangesAsync();
        return row;
    }

    public async Task DeleteDraftAsync(Guid id)
    {
        await using var db = data.Create();
        var row = await db.SupplierBills.SingleOrDefaultAsync(b => b.Id == id) ?? throw new BusinessRuleException("That bill no longer exists.");
        if (row.Status != SupplierBillStatus.Draft) throw new BusinessRuleException($"{Describe(row)} is posted; void it instead.");
        db.SupplierBills.Remove(row);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Posts a draft bill: Dr each line (its account; stock to Inventory; a purchase order's goods to goods received, not
    /// billed) and the input tax (to Input tax when the store's rule set lets it be claimed, else onto the line), Cr
    /// accounts payable for the supplier. A debit note posts the same the other way round. Numbered when posted.
    /// </summary>
    public async Task<SupplierBill> PostBillAsync(Guid id)
    {
        SupplierBill bill;
        string supplierName;
        bool claimable;
        Dictionary<Guid, string> codes;
        await using (var db = data.Create())
        {
            bill = await db.SupplierBills.Include(b => b.Lines).SingleOrDefaultAsync(b => b.Id == id) ?? throw new BusinessRuleException("That bill no longer exists.");
            if (bill.Status != SupplierBillStatus.Draft) throw new BusinessRuleException($"{Describe(bill)} is already posted.");
            supplierName = await db.Suppliers.AsNoTracking().Where(s => s.Id == bill.SupplierId).Select(s => s.Company ?? s.LastName ?? "").FirstAsync();
            claimable = (await FinanceSetupService.RuleSetForStoreAsync(db, bill.StoreId))?.InputTaxClaimable ?? true;
            codes = await db.LedgerAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Code);
        }
        if (bill.Number == 0) bill.Number = await FinanceNumbers.NextAsync(data, RetailPlusDbContext.Sequences.SupplierBillNumbers);
        int sign = bill.Kind == SupplierBillKind.Bill ? 1 : -1;
        var lines = new List<JournalDraftLine>();
        void Line(JournalDraftLine debit, decimal amount)
        {
            if (amount == 0) return;
            lines.Add(sign > 0 ? debit with { Debit = amount, Credit = 0 } : debit with { Debit = 0, Credit = amount });
        }
        decimal ap = 0;
        foreach (var l in bill.Lines.OrderBy(l => l.LineNumber))
        {
            decimal net = FinanceNumbers.Base(l.Amount, bill.ExchangeRate), tax = FinanceNumbers.Base(l.TaxAmount, bill.ExchangeRate);
            var memo = FinanceNumbers.Cut(l.Description.Length > 0 ? l.Description : null, 200);
            JournalDraftLine target = l.PurchaseOrderId is not null ? JournalDraftLine.Role(LedgerRoles.PurchasesClearing, 0, 0, bill.StoreId, memo)
                : l.ItemId is not null ? JournalDraftLine.Role(LedgerRoles.Inventory, 0, 0, bill.StoreId, memo)
                : JournalDraftLine.Code(codes[l.AccountId!.Value], 0, 0, bill.StoreId, memo);
            if (claimable)
            {
                Line(target, net);
                Line(JournalDraftLine.Role(LedgerRoles.InputTax, 0, 0, bill.StoreId, memo), tax);
            }
            else Line(target, net + tax);
            ap += net + tax;
        }
        var payable = JournalDraftLine.Role(LedgerRoles.AccountsPayable, 0, 0, bill.StoreId, supplierName, LedgerPartyKind.Supplier, bill.SupplierId);
        lines.Add(sign > 0 ? payable with { Credit = ap } : payable with { Debit = ap });
        var kindName = bill.Kind == SupplierBillKind.Bill ? "Bill" : "Debit note";
        var entry = await Ledger.PostAsync(new JournalDraft(bill.BillDate, JournalKind.SupplierBill, $"{kindName} {bill.Number} from {supplierName}", lines)
        {
            Source = $"bill:{bill.Id:N}", StoreId = bill.StoreId, Reference = FinanceNumbers.Cut(bill.SupplierReference ?? bill.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), 40),
        });

        await using (var db = data.Create())
        {
            var row = await db.SupplierBills.SingleAsync(b => b.Id == id);
            row.Number = bill.Number;
            row.Status = SupplierBillStatus.Posted;
            row.JournalEntryId = entry.Id;
            row.PostedAtUtc = SyncColumns.Now();
            row.PostedBy = FinanceNumbers.Cut(data.CurrentUser?.UserName, 50);
            db.Audit(AuditActions.BillPosted, $"{kindName} {row.Number} from {supplierName}: {row.Total:N2} {row.Currency}", nameof(SupplierBill), row.Id);
            await db.SaveChangesAsync();
            return row;
        }
    }

    /// <summary>Cancels a posted bill nothing was paid on: its entry is reversed on <paramref name="date"/>.</summary>
    public async Task<SupplierBill> VoidBillAsync(Guid id, DateOnly date, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Say why the bill is voided.");
        Guid? entryId;
        await using (var db = data.Create())
        {
            var bill = await db.SupplierBills.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id) ?? throw new BusinessRuleException("That bill no longer exists.");
            if (bill.Status == SupplierBillStatus.Draft) throw new BusinessRuleException("A draft is deleted, not voided.");
            if (bill.Status == SupplierBillStatus.Void) throw new BusinessRuleException($"{Describe(bill)} is already void.");
            if (bill.Paid != 0) throw new BusinessRuleException($"{Describe(bill)} has payments; void them first.");
            entryId = bill.JournalEntryId;
        }
        if (entryId is Guid entry) await Ledger.ReverseAsync(entry, date, reason);
        await using (var db = data.Create())
        {
            var row = await db.SupplierBills.SingleAsync(b => b.Id == id);
            row.Status = SupplierBillStatus.Void;
            row.VoidedAtUtc = SyncColumns.Now();
            row.VoidReason = FinanceNumbers.Cut(reason.Trim(), 200);
            db.Audit(AuditActions.BillVoided, $"{Describe(row)}: {reason.Trim()}", nameof(SupplierBill), row.Id);
            await db.SaveChangesAsync();
            return row;
        }
    }

    // Payments --------------------------------------------------------------------------------------------------------

    public async Task<SupplierPayment?> GetPaymentAsync(Guid id)
    {
        await using var db = data.Create();
        return await db.SupplierPayments.AsNoTracking().Include(p => p.Allocations).SingleOrDefaultAsync(p => p.Id == id);
    }

    /// <summary>
    /// Pays bills of one supplier and one store from a bank or cash account. Each bill gets at most what is open on it;
    /// debit notes chosen with them reduce what is paid. Withholding is worked out on the bills' amounts before sales tax,
    /// at the supplier's rate (filer or not) of the store's rule set, unless the draft gives it. Dr accounts payable, Cr
    /// the bank, Cr withholding tax payable; a foreign-currency bill paid at another rate leaves an exchange difference.
    /// </summary>
    public async Task<SupplierPayment> PayAsync(PaymentDraft draft, Guid? paymentRunId = null)
    {
        if (draft.Bills.Count == 0) throw new BusinessRuleException("Choose the bills to pay.");
        if (draft.Bills.Any(b => b.Amount <= 0)) throw new BusinessRuleException("Each bill is paid a positive amount.");
        if (draft.Bills.Select(b => b.BillId).Distinct().Count() != draft.Bills.Count) throw new BusinessRuleException("A bill is chosen twice.");
        List<SupplierBill> bills;
        BankAccount bank;
        string supplierName;
        WithholdingQuote quote;
        string baseCurrency;
        await using (var db = data.Create())
        {
            bank = await db.BankAccounts.AsNoTracking().SingleOrDefaultAsync(b => b.Id == draft.BankAccountId) ?? throw new BusinessRuleException("Choose the bank or cash account it is paid from.");
            if (!bank.IsActive) throw new BusinessRuleException($"{bank.Name} is inactive.");
            var ids = draft.Bills.Select(b => b.BillId).ToList();
            bills = await db.SupplierBills.AsNoTracking().Where(b => ids.Contains(b.Id)).ToListAsync();
            if (bills.Count != ids.Count) throw new BusinessRuleException("A bill chosen no longer exists.");
            if (bills.Select(b => b.SupplierId).Distinct().Count() > 1) throw new BusinessRuleException("A payment is for one supplier; use a payment run for several.");
            if (bills.Select(b => b.StoreId).Distinct().Count() > 1) throw new BusinessRuleException("A payment covers the bills of one store.");
            if (bills.FirstOrDefault(b => b.Status is not (SupplierBillStatus.Posted or SupplierBillStatus.PartPaid)) is { } closed)
                throw new BusinessRuleException($"{Describe(closed)} is {closed.Status.ToString().ToLowerInvariant()}; only posted bills are paid.");
            baseCurrency = await FinanceNumbers.BaseCurrencyAsync(db);
            var currency = await FinanceNumbers.CurrencyAsync(db, bank.Currency);
            if (bills.FirstOrDefault(b => b.Currency != currency) is { } other)
                throw new BusinessRuleException($"{Describe(other)} is in {other.Currency}; pay it from an account in {other.Currency}.");
            foreach (var b in draft.Bills)
            {
                var bill = bills.Single(x => x.Id == b.BillId);
                if (b.Amount > bill.Open) throw new BusinessRuleException($"{Describe(bill)} has {bill.Open:N2} open; it cannot be paid {b.Amount:N2}.");
            }
            supplierName = await db.Suppliers.AsNoTracking().Where(s => s.Id == bills[0].SupplierId).Select(s => s.Company ?? s.LastName ?? "").FirstAsync();
            quote = await FinanceSetupService.WithholdingForAsync(db, bills[0].SupplierId, bills[0].StoreId);
        }
        string payCurrency = bills[0].Currency;
        decimal rate = payCurrency == baseCurrency ? 1 : draft.ExchangeRate;
        if (rate <= 0) throw new BusinessRuleException($"Give the exchange rate of {payCurrency}.");

        decimal Sign(SupplierBill b) => b.Kind == SupplierBillKind.Bill ? 1 : -1;
        decimal settled = draft.Bills.Sum(b => Sign(bills.Single(x => x.Id == b.BillId)) * b.Amount);
        if (settled < 0) throw new BusinessRuleException("The debit notes chosen are more than the bills; choose less of them.");
        // Withholding is on the goods or services, not on the sales tax of the bills.
        decimal taxable = Money.Round(draft.Bills.Sum(b =>
        {
            var bill = bills.Single(x => x.Id == b.BillId);
            return Sign(bill) * (bill.Total == 0 ? 0 : b.Amount * bill.Subtotal / bill.Total);
        }));
        decimal withholding = draft.WithholdingAmount is decimal given ? Money.Round(given)
            : quote.Rate == 0 || (quote.Threshold > 0 && taxable * rate < quote.Threshold) ? 0 : Money.Round(taxable * quote.Rate / 100m);
        if (withholding < 0 || withholding > settled) throw new BusinessRuleException("The withholding tax must be between zero and what is paid.");
        decimal paid = settled - withholding;

        var payment = new SupplierPayment
        {
            SupplierId = bills[0].SupplierId, StoreId = bills[0].StoreId, Date = draft.Date, BankAccountId = bank.Id, Currency = payCurrency, ExchangeRate = rate,
            Amount = paid, WithholdingAmount = withholding, WithholdingKind = quote.Kind, WithholdingRate = draft.WithholdingAmount is null ? quote.Rate : taxable == 0 ? 0 : Math.Round(withholding * 100m / taxable, 3),
            Reference = FinanceNumbers.Cut(draft.Reference?.Trim(), 40), Notes = FinanceNumbers.Cut(draft.Notes?.Trim(), 400), Status = SupplierPaymentStatus.Posted,
            PaymentRunId = paymentRunId, CreatedBy = FinanceNumbers.Cut(data.CurrentUser?.UserName, 50),
        };
        payment.Id = Guid.CreateVersion7();
        payment.Number = await FinanceNumbers.NextAsync(data, RetailPlusDbContext.Sequences.SupplierPaymentNumbers);
        foreach (var b in draft.Bills) payment.Allocations.Add(new SupplierPaymentAllocation { SupplierPaymentId = payment.Id, SupplierBillId = b.BillId, Amount = b.Amount });

        // The ledger: accounts payable at the bills' own rates, the bank and the tax at the payment's.
        decimal apBase = Money.Round(draft.Bills.Sum(b => { var bill = bills.Single(x => x.Id == b.BillId); return Sign(bill) * FinanceNumbers.Base(b.Amount, bill.ExchangeRate); }));
        decimal bankBase = FinanceNumbers.Base(paid, rate), taxBase = FinanceNumbers.Base(withholding, rate);
        if (apBase != 0 || bankBase != 0)
        {
            var lines = new List<JournalDraftLine>();
            var memo = $"Payment {payment.Number} to {supplierName}";
            lines.Add(JournalDraftLine.Role(LedgerRoles.AccountsPayable, apBase > 0 ? apBase : 0, apBase < 0 ? -apBase : 0, payment.StoreId, supplierName, LedgerPartyKind.Supplier, payment.SupplierId));
            var bankCode = await LedgerCodeAsync(bank.LedgerAccountId);
            if (bankBase != 0) lines.Add(JournalDraftLine.Code(bankCode, 0, bankBase, payment.StoreId, bank.Name));
            if (taxBase != 0) lines.Add(JournalDraftLine.Role(LedgerRoles.WithholdingTaxPayable, 0, taxBase, payment.StoreId, $"Withheld from {supplierName}", LedgerPartyKind.Supplier, payment.SupplierId));
            decimal difference = apBase - bankBase - taxBase;
            if (difference != 0)
                lines.Add(JournalDraftLine.Role(LedgerRoles.ExchangeDifferences, difference < 0 ? -difference : 0, difference > 0 ? difference : 0, payment.StoreId, "Exchange difference"));
            lines.RemoveAll(l => l.Debit == 0 && l.Credit == 0);
            if (lines.Count >= 2)
            {
                var entry = await Ledger.PostAsync(new JournalDraft(draft.Date, JournalKind.SupplierPayment, memo, lines)
                {
                    Source = $"supplier-payment:{payment.Id:N}", StoreId = payment.StoreId, Reference = payment.Reference ?? payment.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                });
                payment.JournalEntryId = entry.Id;
            }
        }

        await using (var db = data.Create())
        {
            db.SupplierPayments.Add(payment);
            foreach (var b in draft.Bills)
            {
                var row = await db.SupplierBills.SingleAsync(x => x.Id == b.BillId);
                row.Paid += b.Amount;
                row.Status = row.Paid >= row.Total ? SupplierBillStatus.Paid : SupplierBillStatus.PartPaid;
            }
            db.Audit(AuditActions.SupplierPaid, $"Payment {payment.Number} to {supplierName}: {paid:N2} {payCurrency}" + (withholding != 0 ? $", {withholding:N2} withheld" : ""),
                nameof(SupplierPayment), payment.Id);
            await db.SaveChangesAsync();
        }
        return payment;
    }

    /// <summary>Cancels a payment: its entry is reversed on <paramref name="date"/> and its bills are open again.</summary>
    public async Task<SupplierPayment> VoidPaymentAsync(Guid id, DateOnly date, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Say why the payment is voided.");
        SupplierPayment payment;
        await using (var db = data.Create())
        {
            payment = await db.SupplierPayments.AsNoTracking().Include(p => p.Allocations).SingleOrDefaultAsync(p => p.Id == id) ?? throw new BusinessRuleException("That payment no longer exists.");
            if (payment.Status == SupplierPaymentStatus.Void) throw new BusinessRuleException($"Payment {payment.Number} is already void.");
        }
        if (payment.JournalEntryId is Guid entry) await Ledger.ReverseAsync(entry, date, reason);
        await using (var db = data.Create())
        {
            var row = await db.SupplierPayments.SingleAsync(p => p.Id == id);
            row.Status = SupplierPaymentStatus.Void;
            row.VoidedAtUtc = SyncColumns.Now();
            foreach (var a in payment.Allocations)
            {
                var bill = await db.SupplierBills.SingleAsync(b => b.Id == a.SupplierBillId);
                bill.Paid -= a.Amount;
                bill.Status = bill.Paid <= 0 ? SupplierBillStatus.Posted : SupplierBillStatus.PartPaid;
            }
            db.Audit(AuditActions.PaymentVoided, $"Payment {row.Number}: {reason.Trim()}", nameof(SupplierPayment), row.Id);
            await db.SaveChangesAsync();
            return row;
        }
    }

    /// <summary>
    /// A payment run: the chosen bills are paid together from one account, one payment per supplier and store (debit notes
    /// of the same supplier chosen with them are set against its bills). Returns the run with its payments.
    /// </summary>
    public async Task<(PaymentRun Run, List<SupplierPayment> Payments)> PayRunAsync(Guid bankAccountId, DateOnly date, IReadOnlyList<BillToPay> bills, string? reference,
        decimal exchangeRate = 1, string? notes = null)
    {
        if (bills.Count == 0) throw new BusinessRuleException("Choose the bills to pay.");
        List<(Guid Id, Guid Supplier, Guid Store)> owners;
        await using (var db = data.Create())
        {
            var ids = bills.Select(b => b.BillId).ToList();
            owners = (await db.SupplierBills.AsNoTracking().Where(b => ids.Contains(b.Id)).Select(b => new { b.Id, b.SupplierId, b.StoreId }).ToListAsync())
                .Select(b => (b.Id, b.SupplierId, b.StoreId)).ToList();
            if (owners.Count != ids.Count) throw new BusinessRuleException("A bill chosen no longer exists.");
        }
        var run = new PaymentRun
        {
            Date = date, BankAccountId = bankAccountId, Notes = FinanceNumbers.Cut(notes?.Trim(), 200), PaidBy = FinanceNumbers.Cut(data.CurrentUser?.UserName, 50),
            PaidAtUtc = SyncColumns.Now(),
        };
        run.Id = Guid.CreateVersion7();
        run.Number = await FinanceNumbers.NextAsync(data, RetailPlusDbContext.Sequences.PaymentRunNumbers);
        await using (var db = data.Create())
        {
            db.PaymentRuns.Add(run);
            await db.SaveChangesAsync();
        }
        var payments = new List<SupplierPayment>();
        foreach (var group in bills.GroupBy(b =>
                 {
                     var owner = owners.Single(o => o.Id == b.BillId);
                     return (owner.Supplier, owner.Store);
                 }))
            payments.Add(await PayAsync(new PaymentDraft(bankAccountId, date, [.. group]) { Reference = reference, ExchangeRate = exchangeRate, Notes = notes }, run.Id));
        await using (var db = data.Create())
        {
            var row = await db.PaymentRuns.SingleAsync(r => r.Id == run.Id);
            row.Payments = payments.Count;
            row.Total = payments.Sum(p => p.Amount);
            await db.SaveChangesAsync();
            return (row, payments);
        }
    }

    // Reading ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Open bills by how long they are overdue on <paramref name="asOf"/> (by due date), per supplier, in the base currency;
    /// open debit notes count against the supplier as not due.
    /// </summary>
    public async Task<List<AgeingRow>> AgeingAsync(DateOnly asOf, Guid? store = null)
    {
        await using var db = data.Create();
        var q = db.SupplierBills.AsNoTracking().Where(b => (b.Status == SupplierBillStatus.Posted || b.Status == SupplierBillStatus.PartPaid) && b.BillDate <= asOf);
        if (store is Guid s) q = q.Where(b => b.StoreId == s);
        var bills = await q.Select(b => new { b.SupplierId, b.Kind, b.DueDate, b.Total, b.Paid, b.ExchangeRate }).ToListAsync();
        var names = await SupplierNamesAsync(db, bills.Select(b => b.SupplierId));
        return bills.GroupBy(b => b.SupplierId).Select(g =>
        {
            decimal[] buckets = new decimal[5];
            foreach (var b in g)
            {
                decimal open = FinanceNumbers.Base(b.Total - b.Paid, b.ExchangeRate) * (b.Kind == SupplierBillKind.Bill ? 1 : -1);
                int days = asOf.DayNumber - b.DueDate.DayNumber;
                buckets[b.Kind == SupplierBillKind.DebitNote || days <= 0 ? 0 : days <= 30 ? 1 : days <= 60 ? 2 : days <= 90 ? 3 : 4] += open;
            }
            return new AgeingRow(g.Key, names.GetValueOrDefault(g.Key, ""), buckets[0], buckets[1], buckets[2], buckets[3], buckets[4]);
        }).Where(r => r.Total != 0).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// A supplier's statement from the ledger: what was owed before <paramref name="from"/>, then every posting to accounts
    /// payable for the supplier (bills, debit notes, payments, entries made by hand) with the balance owed after each.
    /// </summary>
    public async Task<SupplierStatement> StatementAsync(Guid supplierId, DateOnly from, DateOnly to, Guid? store = null)
    {
        var ap = await Ledger.AccountForRoleAsync(LedgerRoles.AccountsPayable) ?? throw new BusinessRuleException("No account has the role AccountsPayable.");
        await using var db = data.Create();
        var name = await db.Suppliers.AsNoTracking().Where(s => s.Id == supplierId).Select(s => s.Company ?? s.LastName ?? "").FirstOrDefaultAsync()
            ?? throw new BusinessRuleException("That supplier no longer exists.");
        var q = db.JournalLines.AsNoTracking().Where(l => l.AccountId == ap.Id && l.PartyKind == LedgerPartyKind.Supplier && l.PartyId == supplierId)
            .Join(db.JournalEntries.AsNoTracking().Where(e => e.Status != JournalStatus.Draft && e.Date <= to), l => l.JournalEntryId, e => e.Id,
                (l, e) => new { e.Date, e.Number, e.Kind, e.Memo, e.Reference, l.StoreId, l.Debit, l.Credit, l.LineNumber });
        if (store is Guid s) q = q.Where(l => l.StoreId == s);
        var all = await q.ToListAsync();
        decimal balance = all.Where(l => l.Date < from).Sum(l => l.Credit - l.Debit);
        decimal opening = balance;
        var rows = new List<StatementRow>();
        foreach (var l in all.Where(l => l.Date >= from).OrderBy(l => l.Date).ThenBy(l => l.Number).ThenBy(l => l.LineNumber))
        {
            balance += l.Credit - l.Debit;
            rows.Add(new StatementRow(l.Date, l.Number, l.Kind, l.Memo, l.Reference, l.Credit, l.Debit, balance));
        }
        return new SupplierStatement(supplierId, name, from, to, opening, rows);
    }

    /// <summary>Open bills due on or before <paramref name="until"/>, oldest due first.</summary>
    public async Task<List<SupplierBill>> BillsDueAsync(DateOnly until, Guid? store = null)
    {
        await using var db = data.Create();
        var q = db.SupplierBills.AsNoTracking().Include(b => b.Supplier)
            .Where(b => b.Kind == SupplierBillKind.Bill && (b.Status == SupplierBillStatus.Posted || b.Status == SupplierBillStatus.PartPaid) && b.DueDate <= until);
        if (store is Guid s) q = q.Where(b => b.StoreId == s);
        return await q.OrderBy(b => b.DueDate).ThenBy(b => b.Number).ToListAsync();
    }

    private async Task<string> LedgerCodeAsync(Guid accountId)
    {
        await using var db = data.Create();
        return await db.LedgerAccounts.AsNoTracking().Where(a => a.Id == accountId).Select(a => a.Code).SingleOrDefaultAsync()
            ?? throw new BusinessRuleException("The bank account's ledger account no longer exists.");
    }

    internal static async Task<Dictionary<Guid, string>> SupplierNamesAsync(RetailPlusDbContext db, IEnumerable<Guid> ids)
    {
        var list = ids.Distinct().ToList();
        return await db.Suppliers.AsNoTracking().Where(s => list.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Company ?? ((s.FirstName + " " + s.LastName).Trim()));
    }

    private static string Describe(SupplierBill bill) =>
        (bill.Kind == SupplierBillKind.Bill ? "Bill " : "Debit note ") + (bill.Number == 0 ? "(draft)" : bill.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
