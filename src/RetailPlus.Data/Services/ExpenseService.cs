using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>
/// Expenses (rent, electricity, repairs): paid in cash, from a bank or cash account, or owed (on account, which makes a
/// supplier bill). Recurring expenses are made on their schedule, as drafts or posted. Receipts and other attachments of
/// expenses, journal entries and bills are kept in the database with a size limit.
/// </summary>
public sealed class ExpenseService(DataContext data)
{
    /// <summary>The kinds of file an attachment may be.</summary>
    public static readonly IReadOnlyDictionary<string, string> AllowedTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".gif"] = "image/gif", [".webp"] = "image/webp", [".pdf"] = "application/pdf",
    };

    public async Task<Expense?> GetAsync(Guid id)
    {
        await using var db = data.Create();
        return await db.Expenses.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id);
    }

    /// <summary>Saves a draft expense (new when its id is empty). Only drafts change.</summary>
    public async Task<Expense> SaveAsync(Expense expense)
    {
        await using var db = data.Create();
        await CheckAsync(db, expense.StoreId, expense.AccountId, expense.Amount, expense.TaxAmount, expense.PaidBy, expense.BankAccountId, expense.SupplierId, expense.Payee);
        Expense row;
        if (expense.Id == Guid.Empty)
        {
            row = new Expense { Status = ExpenseStatus.Draft, CreatedBy = FinanceNumbers.Cut(data.CurrentUser?.UserName, 50) };
            db.Expenses.Add(row);
        }
        else
        {
            row = await db.Expenses.SingleOrDefaultAsync(e => e.Id == expense.Id) ?? throw new BusinessRuleException("That expense no longer exists.");
            if (row.Status != ExpenseStatus.Draft) throw new BusinessRuleException($"Expense {row.Number} is posted; void it to change it.");
            if (expense.Version != Guid.Empty && expense.Version != row.Version) throw new ConcurrencyConflictException("Someone else changed this expense. Load it again.");
        }
        row.Date = expense.Date;
        row.StoreId = expense.StoreId;
        row.Payee = FinanceNumbers.Cut(expense.Payee.Trim(), 100)!;
        row.SupplierId = expense.SupplierId;
        row.AccountId = expense.AccountId;
        row.Description = FinanceNumbers.Cut(expense.Description?.Trim(), 200);
        row.Amount = Money.Round(expense.Amount);
        row.TaxAmount = Money.Round(expense.TaxAmount);
        row.PaidBy = expense.PaidBy;
        row.BankAccountId = expense.PaidBy == ExpensePaidBy.OnAccount ? null : expense.BankAccountId;
        row.Reference = FinanceNumbers.Cut(expense.Reference?.Trim(), 40);
        row.RecurringExpenseId = expense.RecurringExpenseId;
        await db.SaveChangesAsync();
        return row;
    }

    public async Task DeleteDraftAsync(Guid id)
    {
        await using var db = data.Create();
        var row = await db.Expenses.SingleOrDefaultAsync(e => e.Id == id) ?? throw new BusinessRuleException("That expense no longer exists.");
        if (row.Status != ExpenseStatus.Draft) throw new BusinessRuleException($"Expense {row.Number} is posted; void it instead.");
        db.FinanceAttachments.RemoveRange(db.FinanceAttachments.Where(a => a.OwnerKind == AttachmentOwner.Expense && a.OwnerId == id));
        db.Expenses.Remove(row);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Posts a draft expense. Paid in cash: Dr the account and the input tax, Cr Cash in hand (or the cash account chosen);
    /// from a bank: Cr that account. On account: a supplier bill is made and posted for it (Cr accounts payable).
    /// </summary>
    public async Task<Expense> PostAsync(Guid id)
    {
        Expense expense;
        string code;
        string? bankCode = null;
        bool claimable;
        await using (var db = data.Create())
        {
            expense = await db.Expenses.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id) ?? throw new BusinessRuleException("That expense no longer exists.");
            if (expense.Status != ExpenseStatus.Draft) throw new BusinessRuleException($"Expense {expense.Number} is already posted.");
            code = await db.LedgerAccounts.AsNoTracking().Where(a => a.Id == expense.AccountId).Select(a => a.Code).SingleAsync();
            if (expense.BankAccountId is Guid bank)
                bankCode = await db.BankAccounts.AsNoTracking().Where(b => b.Id == bank).Select(b => b.LedgerAccount!.Code).SingleAsync();
            claimable = (await FinanceSetupService.RuleSetForStoreAsync(db, expense.StoreId))?.InputTaxClaimable ?? true;
        }
        long number = expense.Number != 0 ? expense.Number : await FinanceNumbers.NextAsync(data, RetailPlusDbContext.Sequences.ExpenseNumbers);
        Guid? entryId = null, billId = null;
        if (expense.PaidBy == ExpensePaidBy.OnAccount)
        {
            var payables = new PayablesService(data);
            var bill = await payables.SaveBillAsync(new SupplierBill
            {
                Kind = SupplierBillKind.Bill, SupplierId = expense.SupplierId!.Value, StoreId = expense.StoreId, BillDate = expense.Date,
                SupplierReference = expense.Reference, Notes = $"Expense {number}: {expense.Description ?? expense.Payee}", ExpenseId = expense.Id,
                Lines =
                [
                    new SupplierBillLine
                    {
                        AccountId = expense.AccountId, Description = expense.Description ?? expense.Payee, Quantity = 1, UnitPrice = expense.Amount, TaxAmount = expense.TaxAmount,
                    },
                ],
            });
            bill = await payables.PostBillAsync(bill.Id);
            billId = bill.Id;
            entryId = bill.JournalEntryId;
        }
        else
        {
            var memo = FinanceNumbers.Cut($"{expense.Payee}{(expense.Description is null ? "" : ": " + expense.Description)}", 200);
            var lines = new List<JournalDraftLine>();
            if (claimable)
            {
                lines.Add(JournalDraftLine.Code(code, expense.Amount, 0, expense.StoreId, memo));
                if (expense.TaxAmount != 0) lines.Add(JournalDraftLine.Role(LedgerRoles.InputTax, expense.TaxAmount, 0, expense.StoreId, memo));
            }
            else lines.Add(JournalDraftLine.Code(code, expense.Total, 0, expense.StoreId, memo));
            lines.Add(bankCode is not null ? JournalDraftLine.Code(bankCode, 0, expense.Total, expense.StoreId, memo)
                : JournalDraftLine.Role(LedgerRoles.Cash, 0, expense.Total, expense.StoreId, memo));
            var entry = await new LedgerService(data).PostAsync(new JournalDraft(expense.Date, JournalKind.Expense, $"Expense {number}: {expense.Payee}", lines)
            {
                Source = $"expense:{expense.Id:N}", StoreId = expense.StoreId, Reference = FinanceNumbers.Cut(expense.Reference, 40),
            });
            entryId = entry.Id;
        }
        await using (var db = data.Create())
        {
            var row = await db.Expenses.SingleAsync(e => e.Id == id);
            row.Number = number;
            row.Status = ExpenseStatus.Posted;
            row.JournalEntryId = entryId;
            row.SupplierBillId = billId;
            db.Audit(AuditActions.ExpensePosted, $"Expense {number} {row.Payee}: {row.Total:N2} ({row.PaidBy})", nameof(Expense), row.Id);
            await db.SaveChangesAsync();
            return row;
        }
    }

    /// <summary>Cancels a posted expense: its entry is reversed (or its bill voided, when it was on account).</summary>
    public async Task<Expense> VoidAsync(Guid id, DateOnly date, string reason)
    {
        Expense expense;
        await using (var db = data.Create())
            expense = await db.Expenses.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id) ?? throw new BusinessRuleException("That expense no longer exists.");
        if (expense.Status != ExpenseStatus.Posted) throw new BusinessRuleException(expense.Status == ExpenseStatus.Draft ? "A draft is deleted, not voided." : $"Expense {expense.Number} is already void.");
        if (expense.SupplierBillId is Guid bill) await new PayablesService(data).VoidBillAsync(bill, date, reason);
        else if (expense.JournalEntryId is Guid entry) await new LedgerService(data).ReverseAsync(entry, date, reason);
        await using (var db = data.Create())
        {
            var row = await db.Expenses.SingleAsync(e => e.Id == id);
            row.Status = ExpenseStatus.Void;
            await db.SaveChangesAsync();
            return row;
        }
    }

    // Recurring expenses ----------------------------------------------------------------------------------------------

    public async Task<List<RecurringExpense>> GetRecurringAsync(Guid? store = null)
    {
        await using var db = data.Create();
        var q = db.RecurringExpenses.AsNoTracking();
        if (store is Guid s) q = q.Where(r => r.StoreId == s);
        return await q.OrderBy(r => r.Name).ToListAsync();
    }

    public async Task<RecurringExpense> SaveRecurringAsync(RecurringExpense recurring)
    {
        var name = recurring.Name.Trim();
        if (name.Length is 0 or > 60) throw new BusinessRuleException("A recurring expense needs a name of up to 60 characters.");
        await using var db = data.Create();
        await CheckAsync(db, recurring.StoreId, recurring.AccountId, recurring.Amount, recurring.TaxAmount, recurring.PaidBy, recurring.BankAccountId, recurring.SupplierId, recurring.Payee);
        if (recurring.EndDate is DateOnly end && end < recurring.NextDate) throw new BusinessRuleException("It ends before it is next made.");
        var row = recurring.Id == Guid.Empty ? null : await db.RecurringExpenses.SingleOrDefaultAsync(r => r.Id == recurring.Id);
        if (row is null) db.RecurringExpenses.Add(row = new RecurringExpense());
        row.Name = name;
        row.StoreId = recurring.StoreId;
        row.Payee = FinanceNumbers.Cut(recurring.Payee.Trim(), 100)!;
        row.SupplierId = recurring.SupplierId;
        row.AccountId = recurring.AccountId;
        row.Amount = Money.Round(recurring.Amount);
        row.TaxAmount = Money.Round(recurring.TaxAmount);
        row.PaidBy = recurring.PaidBy;
        row.BankAccountId = recurring.PaidBy == ExpensePaidBy.OnAccount ? null : recurring.BankAccountId;
        row.Frequency = recurring.Frequency;
        row.NextDate = recurring.NextDate;
        row.EndDate = recurring.EndDate;
        row.PostAutomatically = recurring.PostAutomatically;
        row.IsActive = recurring.IsActive;
        await db.SaveChangesAsync();
        return row;
    }

    public async Task DeleteRecurringAsync(Guid id)
    {
        await using var db = data.Create();
        var row = await db.RecurringExpenses.SingleOrDefaultAsync(r => r.Id == id) ?? throw new BusinessRuleException("That recurring expense no longer exists.");
        db.RecurringExpenses.Remove(row);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Makes every recurring expense that is due on or before <paramref name="today"/> (several when it was missed for a
    /// while), posted or left as drafts as each one says, and moves its next date on. Returns the expenses made.
    /// </summary>
    public async Task<List<Expense>> RunDueAsync(DateOnly today)
    {
        List<RecurringExpense> due;
        await using (var db = data.Create())
            due = await db.RecurringExpenses.AsNoTracking().Where(r => r.IsActive && r.NextDate <= today).ToListAsync();
        var made = new List<Expense>();
        foreach (var r in due)
        {
            var date = r.NextDate;
            while (date <= today && (r.EndDate is null || date <= r.EndDate))
            {
                var expense = await SaveAsync(new Expense
                {
                    Date = date, StoreId = r.StoreId, Payee = r.Payee, SupplierId = r.SupplierId, AccountId = r.AccountId, Description = r.Name, Amount = r.Amount,
                    TaxAmount = r.TaxAmount, PaidBy = r.PaidBy, BankAccountId = r.BankAccountId, RecurringExpenseId = r.Id,
                });
                if (r.PostAutomatically)
                {
                    try
                    {
                        expense = await PostAsync(expense.Id);
                    }
                    catch (BusinessRuleException)
                    {
                        // Left as a draft (a locked period, an inactive account): someone posts it by hand.
                    }
                }
                made.Add(expense);
                date = Next(date, r.Frequency);
            }
            await using var db = data.Create();
            var row = await db.RecurringExpenses.SingleAsync(x => x.Id == r.Id);
            row.LastMadeOn = made.LastOrDefault(e => e.RecurringExpenseId == r.Id)?.Date ?? row.LastMadeOn;
            row.NextDate = date;
            if (r.EndDate is DateOnly end && date > end) row.IsActive = false;
            await db.SaveChangesAsync();
        }
        return made;
    }

    /// <summary>
    /// The date after <paramref name="date"/> in the schedule: the same day of the next month (quarter, year), and the last
    /// day of the month for one made on a month's last day (31 August, 30 September, 31 October).
    /// </summary>
    public static DateOnly Next(DateOnly date, RecurrenceFrequency frequency)
    {
        if (frequency == RecurrenceFrequency.Weekly) return date.AddDays(7);
        int months = frequency switch { RecurrenceFrequency.Quarterly => 3, RecurrenceFrequency.Yearly => 12, _ => 1 };
        var next = date.AddMonths(months);
        bool lastDay = date.Day == DateTime.DaysInMonth(date.Year, date.Month);
        return lastDay ? new DateOnly(next.Year, next.Month, DateTime.DaysInMonth(next.Year, next.Month)) : next;
    }

    // Attachments -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Keeps a receipt picture or PDF with an expense, a journal entry or a bill. Refused when it is not a picture or a PDF,
    /// or larger than the settings allow.
    /// </summary>
    public async Task<FinanceAttachment> AttachAsync(AttachmentOwner owner, Guid ownerId, string fileName, byte[] content)
    {
        var name = Path.GetFileName(fileName ?? "").Trim();
        if (name.Length == 0) name = "receipt";
        if (!AllowedTypes.TryGetValue(Path.GetExtension(name), out var type)) throw new BusinessRuleException("Attach a picture (JPEG, PNG, GIF, WebP) or a PDF.");
        if (!LooksLike(type, content)) throw new BusinessRuleException($"{name} is not the kind of file its name says.");
        await using var db = data.Create();
        var limit = (await db.FinanceSettings.AsNoTracking().Select(s => (int?)s.AttachmentMaxKilobytes).FirstOrDefaultAsync() ?? 5120) * 1024L;
        if (content.Length == 0) throw new BusinessRuleException("The file is empty.");
        if (content.Length > limit) throw new BusinessRuleException($"The file is larger than the {limit / 1024} KB allowed.");
        bool exists = owner switch
        {
            AttachmentOwner.Expense => await db.Expenses.AnyAsync(e => e.Id == ownerId),
            AttachmentOwner.JournalEntry => await db.JournalEntries.AnyAsync(e => e.Id == ownerId),
            _ => await db.SupplierBills.AnyAsync(b => b.Id == ownerId),
        };
        if (!exists) throw new BusinessRuleException("What the file is for no longer exists.");
        var row = new FinanceAttachment
        {
            OwnerKind = owner, OwnerId = ownerId, FileName = FinanceNumbers.Cut(name, 200)!, ContentType = type, Size = content.Length, Data = content,
            UploadedBy = FinanceNumbers.Cut(data.CurrentUser?.UserName, 50),
        };
        db.FinanceAttachments.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>The attachments of a record, without their contents.</summary>
    public async Task<List<FinanceAttachment>> AttachmentsAsync(AttachmentOwner owner, Guid ownerId)
    {
        await using var db = data.Create();
        return await db.FinanceAttachments.AsNoTracking().Where(a => a.OwnerKind == owner && a.OwnerId == ownerId).OrderBy(a => a.CreatedAtUtc)
            .Select(a => new FinanceAttachment
            {
                Id = a.Id, OwnerKind = a.OwnerKind, OwnerId = a.OwnerId, FileName = a.FileName, ContentType = a.ContentType, Size = a.Size, UploadedBy = a.UploadedBy,
                CreatedAtUtc = a.CreatedAtUtc, UpdatedAtUtc = a.UpdatedAtUtc,
            }).ToListAsync();
    }

    public async Task<FinanceAttachment?> AttachmentAsync(Guid id)
    {
        await using var db = data.Create();
        return await db.FinanceAttachments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id);
    }

    public async Task DeleteAttachmentAsync(Guid id)
    {
        await using var db = data.Create();
        var row = await db.FinanceAttachments.SingleOrDefaultAsync(a => a.Id == id) ?? throw new BusinessRuleException("That file no longer exists.");
        db.FinanceAttachments.Remove(row);
        await db.SaveChangesAsync();
    }

    /// <summary>The first bytes of the file match its kind (so a renamed program is not stored as a receipt).</summary>
    private static bool LooksLike(string type, byte[] content)
    {
        static bool Starts(byte[] c, params byte[] magic) => c.Length >= magic.Length && c.AsSpan(0, magic.Length).SequenceEqual(magic);
        return type switch
        {
            "image/jpeg" => Starts(content, 0xFF, 0xD8, 0xFF),
            "image/png" => Starts(content, 0x89, 0x50, 0x4E, 0x47),
            "image/gif" => Starts(content, 0x47, 0x49, 0x46, 0x38),
            "image/webp" => content.Length >= 12 && Starts(content, 0x52, 0x49, 0x46, 0x46) && content[8] == 0x57 && content[9] == 0x45 && content[10] == 0x42 && content[11] == 0x50,
            "application/pdf" => Starts(content, 0x25, 0x50, 0x44, 0x46),
            _ => false,
        };
    }

    private static async Task CheckAsync(RetailPlusDbContext db, Guid store, Guid account, decimal amount, decimal tax, ExpensePaidBy paidBy, Guid? bank, Guid? supplier, string payee)
    {
        if (string.IsNullOrWhiteSpace(payee)) throw new BusinessRuleException("Say who was paid.");
        if (amount <= 0) throw new BusinessRuleException("An expense's amount must be more than zero.");
        if (tax < 0) throw new BusinessRuleException("The tax cannot be negative.");
        if (!await db.Stores.AnyAsync(s => s.Id == store)) throw new BusinessRuleException("Choose the store the expense is for.");
        var a = await db.LedgerAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == account) ?? throw new BusinessRuleException("Choose the expense account.");
        if (!a.IsPosting || !a.IsActive) throw new BusinessRuleException($"{a.Code} {a.Name} is a header or inactive account.");
        if (paidBy == ExpensePaidBy.Bank && bank is null) throw new BusinessRuleException("Choose the bank account it was paid from.");
        if (bank is Guid b && !await db.BankAccounts.AnyAsync(x => x.Id == b && x.IsActive)) throw new BusinessRuleException("That bank or cash account no longer exists or is inactive.");
        if (paidBy == ExpensePaidBy.OnAccount && (supplier is null || !await db.Suppliers.AnyAsync(s => s.Id == supplier)))
            throw new BusinessRuleException("An expense on account needs the supplier it is owed to.");
    }
}
