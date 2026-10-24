using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

public sealed record InvoiceRow(int Id, long InvoiceNumber, InvoiceKind Kind, DateOnly InvoiceDate, string Name, string? AccountNumber,
    decimal Total, decimal Balance, decimal Penalty, DateOnly? LastPaidDate, bool IsVoided, int? ClientId);

/// <summary>One client's line of the aged receivables report.</summary>
public sealed record AgingRow(int? ClientId, string Name, decimal Current, decimal Days30, decimal Days60, decimal Days90Plus)
{
    public decimal Total => Current + Days30 + Days60 + Days90Plus;
}

/// <summary>Accounts receivable: on-account invoices, quotes and layaways, payments, late charges and statements.</summary>
public sealed class InvoiceService(DataContext data)
{
    public async Task<List<InvoiceRow>> ListAsync(InvoiceKind? kind, bool openOnly, string? text = null, int? clientId = null, int take = 3000)
    {
        await using var db = data.Create();
        var q = db.Invoices.AsNoTracking().AsQueryable();
        if (kind is { } k) q = q.Where(i => i.Kind == k);
        if (openOnly) q = q.Where(i => i.Balance != 0 && !i.IsVoided);
        if (clientId is int cid) q = q.Where(i => i.ClientId == cid);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var t = text.Trim();
            q = long.TryParse(t, out var n)
                ? q.Where(i => i.InvoiceNumber == n || (i.AccountNumber ?? "").StartsWith(t))
                : q.Where(i => (i.Company ?? "").StartsWith(t) || (i.LastName ?? "").StartsWith(t) || (i.FirstName ?? "").StartsWith(t) || (i.AccountNumber ?? "").StartsWith(t));
        }
        var rows = await q.OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.InvoiceNumber).Take(take).ToListAsync();
        return rows.Select(i => new InvoiceRow(i.Id, i.InvoiceNumber, i.Kind, i.InvoiceDate, i.DisplayName, i.AccountNumber, i.Total, i.Balance, i.Penalty, i.LastPaidDate, i.IsVoided, i.ClientId)).ToList();
    }

    public async Task<Invoice?> GetAsync(int id)
    {
        await using var db = data.Create();
        return await db.Invoices.AsNoTracking().Include(i => i.Lines.OrderBy(l => l.LineNumber)).Include(i => i.Payments.OrderBy(p => p.PaidOn))
            .SingleOrDefaultAsync(i => i.Id == id);
    }

    /// <summary>Creates an invoice (or quote) for a client without a POS sale, e.g. recurring billing.</summary>
    public async Task<Invoice> CreateAsync(int clientId, InvoiceKind kind, DateOnly date, IReadOnlyList<InvoiceLine> lines, string? notes)
    {
        await using var db = data.Create();
        await using var tx = await db.Database.BeginTransactionAsync();
        var client = await db.Clients.SingleAsync(c => c.Id == clientId);
        var settings = await db.StoreSettings.AsNoTracking().SingleOrDefaultAsync() ?? new StoreSettings();
        var invoice = NewInvoice(client, kind, date, settings);
        invoice.InvoiceNumber = await db.NextNumberAsync(RetailPlusDbContext.Sequences.InvoiceNumbers);
        invoice.Notes = notes;
        int n = 0;
        foreach (var l in lines)
        {
            l.LineNumber = ++n;
            if (l.LineTotal == 0) l.LineTotal = Money.Round(l.Quantity * l.UnitPrice - l.DiscountAmount);
            invoice.Lines.Add(l);
        }
        invoice.Total = invoice.Lines.Sum(l => l.LineTotal);
        invoice.Balance = kind == InvoiceKind.Quote ? 0 : invoice.Total;
        if (kind != InvoiceKind.Quote) client.Balance += invoice.Balance;
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return invoice;
    }

    private static Invoice NewInvoice(Client client, InvoiceKind kind, DateOnly date, StoreSettings settings) => new()
    {
        Kind = kind, ClientId = client.Id, AccountNumber = client.AccountNumber, CustomerNumber = client.Number, FirstName = client.FirstName,
        LastName = client.LastName, Company = client.Company, InvoiceDate = date, ShipTo = client.ShipTo, Email = client.Email,
        Tax1Rate = settings.Tax1Rate, Tax2Rate = settings.Tax2Rate, DiscountPercent = client.UsualDiscountPercent,
    };

    /// <summary>Saves the POS screen as a price quote. Nothing is taken out of stock and nothing is owed.</summary>
    public async Task<Invoice> SaveQuoteAsync(SaleDraft draft, SessionInfo session)
    {
        if (draft.ClientId is null) throw new BusinessRuleException("Select a client for the quote.");
        await using var db = data.Create();
        var settings = await db.StoreSettings.AsNoTracking().SingleOrDefaultAsync() ?? new StoreSettings();
        var client = await db.Clients.AsNoTracking().SingleAsync(c => c.Id == draft.ClientId);
        var totals = draft.Calculate(draft.EffectiveTax(settings));
        var quote = NewInvoice(client, InvoiceKind.Quote, DateOnly.FromDateTime(DateTime.Today), settings);
        quote.InvoiceNumber = await db.NextNumberAsync(RetailPlusDbContext.Sequences.InvoiceNumbers);
        quote.SalesPerson = session.UserName;
        quote.Total = totals.Total;
        quote.DiscountPercent = draft.SubtotalDiscountPercent;
        quote.Notes = draft.Notes;
        int n = 0;
        foreach (var l in draft.Lines.Where(l => l.Kind != SaleLineKind.Text))
            quote.Lines.Add(new InvoiceLine { LineNumber = ++n, Plu = l.Plu, Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice, DiscountAmount = l.DiscountAmount, LineTotal = l.LineTotal });
        db.Invoices.Add(quote);
        await db.SaveChangesAsync();
        return quote;
    }

    /// <summary>Loads a quote back into a POS draft so it can be sold.</summary>
    public async Task<SaleDraft> QuoteToDraftAsync(int invoiceId, int locationId)
    {
        await using var db = data.Create();
        var quote = await db.Invoices.AsNoTracking().Include(i => i.Lines).SingleAsync(i => i.Id == invoiceId);
        var client = quote.ClientId is int cid ? await db.Clients.AsNoTracking().SingleOrDefaultAsync(c => c.Id == cid) : null;
        var draft = new SaleDraft
        {
            ClientId = quote.ClientId, ClientName = quote.DisplayName, CustomerNumber = quote.CustomerNumber,
            SubtotalDiscountPercent = quote.DiscountPercent, ExemptTax1 = client?.ExemptTax1 ?? false, ExemptTax2 = client?.ExemptTax2 ?? false,
            ClientPriceLevel = client?.PriceLevel ?? 0, Notes = $"From quote {quote.InvoiceNumber}",
        };
        foreach (var l in quote.Lines.OrderBy(l => l.LineNumber))
        {
            var item = l.Plu is null ? null : await db.Items.AsNoTracking().Include(i => i.Department)
                .FirstOrDefaultAsync(i => i.LocationId == locationId && i.Plu == l.Plu && !i.IsDeleted);
            draft.Lines.Add(new SaleDraftLine
            {
                ItemId = item?.Id, ItemType = item?.ItemType ?? ItemType.NonStock, Plu = l.Plu, Description = l.Description, Quantity = l.Quantity,
                UnitPrice = l.UnitPrice, DiscountAmount = l.DiscountAmount, Taxable1 = item?.Taxable1 ?? true, Taxable2 = item?.Taxable2 ?? true,
                Department = item?.Department?.Name, UnitCost = item is null ? 0 : (item.AverageCost > 0 ? item.AverageCost : item.LastCost), AutoPriced = false,
            });
        }
        return draft;
    }

    /// <summary>
    /// Records a payment on an invoice: the late charge is paid first, then the balance. The money goes into the
    /// station's drawer totals.
    /// </summary>
    public async Task ApplyPaymentAsync(int invoiceId, decimal amount, string tenderType, DateOnly paidOn, SessionInfo session, string? note = null)
    {
        if (amount <= 0) throw new BusinessRuleException("Enter a payment above zero.");
        await using var db = data.Create();
        await using var tx = await db.Database.BeginTransactionAsync();
        var invoice = await db.Invoices.SingleAsync(i => i.Id == invoiceId);
        await ApplyAsync(db, invoice, amount, tenderType, paidOn, note);
        await AddReceiptToDrawerAsync(db, session, tenderType, amount);
        await SaveAsync(db);
        await tx.CommitAsync();
    }

    private static async Task ApplyAsync(RetailPlusDbContext db, Invoice invoice, decimal amount, string tenderType, DateOnly paidOn, string? note)
    {
        if (invoice.IsVoided) throw new BusinessRuleException("This invoice is voided.");
        if (invoice.Kind == InvoiceKind.Quote) throw new BusinessRuleException("A quote has nothing to pay.");
        var (toPenalty, _) = LateCharges.SplitPayment(amount, invoice.Penalty);
        invoice.Penalty -= toPenalty;
        invoice.Balance -= amount;
        invoice.LastPaidDate = paidOn;
        invoice.LastPaidAmount = amount;
        db.InvoicePayments.Add(new InvoicePayment { InvoiceId = invoice.Id, PaidOn = paidOn, Amount = amount, TenderType = tenderType, AppliedToPenalty = toPenalty, Note = note });
        if (invoice.ClientId is int cid)
            await db.Clients.Where(c => c.Id == cid).ExecuteUpdateAsync(s => s.SetProperty(c => c.Balance, c => c.Balance - amount));
    }

    private static async Task AddReceiptToDrawerAsync(RetailPlusDbContext db, SessionInfo session, string tenderType, decimal amount)
    {
        if (session.IsTrainee) return;
        var drawer = await DrawerService.GetOpenSessionAsync(db, session.StationNumber, session.LocationId);
        drawer.TotalReceipts += amount;
        var row = drawer.Tenders.FirstOrDefault(t => t.TenderType == tenderType);
        if (row is null) drawer.Tenders.Add(row = new DrawerSessionTender { TenderType = tenderType });
        row.Amount += amount;
    }

    private static async Task SaveAsync(RetailPlusDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("This invoice was changed at another station. Reload it and try again.");
        }
    }

    /// <summary>
    /// Distribute Payment: spreads one payment over the client's open invoices, oldest first. Returns what is left
    /// over (a credit), which is applied to the newest invoice so the client's balance stays right.
    /// </summary>
    public async Task<decimal> DistributePaymentAsync(int clientId, decimal amount, string tenderType, DateOnly paidOn, SessionInfo session)
    {
        if (amount <= 0) throw new BusinessRuleException("Enter a payment above zero.");
        await using var db = data.Create();
        await using var tx = await db.Database.BeginTransactionAsync();
        var open = await db.Invoices.Where(i => i.ClientId == clientId && i.Balance > 0 && !i.IsVoided && i.Kind != InvoiceKind.Quote)
            .OrderBy(i => i.InvoiceDate).ThenBy(i => i.InvoiceNumber).ToListAsync();
        if (open.Count == 0) throw new BusinessRuleException("This client has no open invoices.");
        decimal left = amount;
        foreach (var invoice in open)
        {
            if (left <= 0) break;
            decimal pay = Math.Min(left, invoice.Balance);
            await ApplyAsync(db, invoice, pay, tenderType, paidOn, "Distributed payment");
            left -= pay;
        }
        if (left > 0) await ApplyAsync(db, open[^1], left, tenderType, paidOn, "Overpayment (credit)");
        await AddReceiptToDrawerAsync(db, session, tenderType, amount);
        await SaveAsync(db);
        await tx.CommitAsync();
        return left;
    }

    /// <summary>
    /// Adds late charges to overdue invoices (Setup / Options / Overdue Interest Charges). The charge is recomputed from
    /// the last payment date and only the increase is added, so running it twice on a day adds nothing.
    /// </summary>
    public async Task<(int Invoices, decimal Total)> ApplyLateChargesAsync(DateOnly today)
    {
        await using var db = data.Create();
        var settings = await db.StoreSettings.AsNoTracking().SingleOrDefaultAsync() ?? new StoreSettings();
        if (settings.InterestRatePerMonth <= 0) return (0, 0);
        var open = await db.Invoices.Where(i => i.Kind == InvoiceKind.Invoice && i.Balance > 0 && !i.IsVoided).ToListAsync();
        int count = 0; decimal sum = 0;
        foreach (var invoice in open)
        {
            var (_, penalty) = LateCharges.Calculate(invoice.Balance - invoice.Penalty, invoice.InvoiceDate, invoice.LastPaidDate, today, settings.InterestRatePerMonth, settings.GracePeriodDays);
            decimal delta = penalty - invoice.Penalty;
            if (delta <= 0) continue;
            invoice.Penalty += delta;
            invoice.Balance += delta;
            if (invoice.ClientId is int cid)
                await db.Clients.Where(c => c.Id == cid).ExecuteUpdateAsync(s => s.SetProperty(c => c.Balance, c => c.Balance + delta));
            count++; sum += delta;
        }
        await db.SaveChangesAsync();
        return (count, sum);
    }

    public async Task VoidAsync(int invoiceId)
    {
        await using var db = data.Create();
        var invoice = await db.Invoices.SingleAsync(i => i.Id == invoiceId);
        if (invoice.IsVoided) return;
        if (invoice.ClientId is int cid && invoice.Kind != InvoiceKind.Quote)
            await db.Clients.Where(c => c.Id == cid).ExecuteUpdateAsync(s => s.SetProperty(c => c.Balance, c => c.Balance - invoice.Balance));
        invoice.Balance = 0;
        invoice.IsVoided = true;
        await SaveAsync(db);
    }

    /// <summary>Aged receivables by client: invoice age counted from the invoice date.</summary>
    public async Task<List<AgingRow>> AgingAsync(DateOnly today)
    {
        await using var db = data.Create();
        var open = await db.Invoices.AsNoTracking().Where(i => i.Balance != 0 && !i.IsVoided && i.Kind != InvoiceKind.Quote).ToListAsync();
        return open.GroupBy(i => (i.ClientId, Name: i.DisplayName))
            .Select(g =>
            {
                decimal c = 0, d30 = 0, d60 = 0, d90 = 0;
                foreach (var i in g)
                {
                    int age = today.DayNumber - i.InvoiceDate.DayNumber;
                    if (age < 30) c += i.Balance; else if (age < 60) d30 += i.Balance; else if (age < 90) d60 += i.Balance; else d90 += i.Balance;
                }
                return new AgingRow(g.Key.ClientId, g.Key.Name, c, d30, d60, d90);
            })
            .OrderBy(r => r.Name).ToList();
    }

    /// <summary>A client statement: open invoices and the payments of the last <paramref name="days"/> days.</summary>
    public async Task<(Client Client, List<Invoice> Open, List<InvoicePayment> Payments)> StatementAsync(int clientId, int days = 90)
    {
        await using var db = data.Create();
        var client = await db.Clients.AsNoTracking().SingleAsync(c => c.Id == clientId);
        var open = await db.Invoices.AsNoTracking().Where(i => i.ClientId == clientId && i.Balance != 0 && !i.IsVoided && i.Kind != InvoiceKind.Quote)
            .OrderBy(i => i.InvoiceDate).ToListAsync();
        var since = DateOnly.FromDateTime(DateTime.Today.AddDays(-days));
        var payments = await db.InvoicePayments.AsNoTracking()
            .Where(p => p.PaidOn >= since && db.Invoices.Any(i => i.Id == p.InvoiceId && i.ClientId == clientId))
            .OrderBy(p => p.PaidOn).ToListAsync();
        return (client, open, payments);
    }
}
