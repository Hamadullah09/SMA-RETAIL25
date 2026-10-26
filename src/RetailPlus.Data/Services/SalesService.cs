using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>A row of the sales journal.</summary>
public sealed record SaleRow(int Id, long InvoiceNumber, int StationNumber, DateTime SoldAt, string? Staff, string? ClientName,
    decimal Subtotal, decimal Tax1, decimal Tax2, decimal Total, SaleStatus Status, string Tenders);

/// <summary>
/// Saving sales from the POS: invoice numbers, stock, sales statistics, the client's points and account, and the
/// station's drawer totals, all in one transaction.
/// </summary>
public sealed class SalesService(DataContext data)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>Item types that have no stock count.</summary>
    public static bool IsStocked(ItemType type) => type is ItemType.Standard or ItemType.Matrix or ItemType.Serialized or ItemType.Kit;

    /// <summary>
    /// Saves a sale. In training mode (<see cref="SessionInfo.IsTrainee"/>) nothing is written and the returned sale
    /// has Id 0. Payments by "On Account" tenders, and the unpaid rest of a layaway, become an invoice on the client's
    /// account.
    /// </summary>
    public async Task<Sale> PostSaleAsync(SaleDraft draft, IReadOnlyList<TenderEntry> tenders, SessionInfo session)
    {
        var lines = draft.Lines.Where(l => l.Kind == SaleLineKind.Text || l.Quantity != 0 || l.UnitPrice != 0).ToList();
        if (lines.Count == 0) throw new BusinessRuleException("There is nothing to sell.");

        await using var db = data.Create();
        var settings = await db.StoreSettings.AsNoTracking().SingleOrDefaultAsync() ?? new StoreSettings();
        var tax = draft.EffectiveTax(settings);
        var totals = draft.Calculate(tax);

        decimal total = totals.Total;
        bool cashOnly = tenders.Count > 0 && tenders.All(t => t.Kind == TenderKind.Cash);
        decimal rounding = cashOnly ? Money.RoundToMinimumTender(total, settings.MinimumTender) - total : 0;
        total += rounding;

        decimal paid = tenders.Sum(t => t.Amount);
        decimal onAccount = tenders.Where(t => t.Kind == TenderKind.OnAccount).Sum(t => t.Amount);
        decimal layawayOwing = draft.IsLayaway ? Math.Max(0, total - paid) : 0;
        if (!draft.IsLayaway && Math.Abs(paid - total) > 0.005m)
            throw new BusinessRuleException($"The payments ({paid:N2}) do not match the total ({total:N2}).");
        if ((onAccount != 0 || draft.IsLayaway) && draft.ClientId is null)
            throw new BusinessRuleException("Select a client for an on-account sale or a layaway.");
        if (settings.LimitThc && settings.ThcLimit > 0 && draft.TotalThc > settings.ThcLimit)
            throw new BusinessRuleException($"This sale has {draft.TotalThc:0.###} g of THC; the limit is {settings.ThcLimit:0.###} g.");

        var now = DateTime.Now;
        var sale = new Sale
        {
            StationNumber = session.StationNumber,
            LocationId = session.LocationId,
            SoldAt = now,
            Staff = draft.Staff ?? session.UserName,
            ClientId = draft.ClientId,
            ClientName = draft.ClientName,
            CustomerNumber = draft.CustomerNumber,
            Status = SaleStatus.Completed,
            IsOnAccount = onAccount != 0 || draft.IsLayaway,
            Subtotal = totals.Subtotal,
            SubtotalDiscount = totals.SubtotalDiscount,
            Tax1 = totals.Tax1,
            Tax2 = totals.Tax2,
            Rounding = rounding,
            Total = total,
            Tax1Name = tax.Tax1Name, Tax1Rate = tax.Tax1Rate, Tax2Name = tax.Tax2Name, Tax2Rate = tax.Tax2Rate,
            TaxInclusive = tax.Inclusive, Tax2Compound = tax.Compound,
            PointsRedeemed = draft.PointsRedeemed,
            Notes = draft.Notes,
        };
        for (int i = 0, t = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var lineTax = l.Kind == SaleLineKind.Text ? (0m, 0m) : totals.LineTaxes[t++];
            sale.Lines.Add(new SaleLine
            {
                LineNumber = i + 1, Kind = l.Kind, ItemId = l.ItemId, Plu = l.Plu, Description = Trim(l.Description + (l.VariantText is null ? "" : " " + l.VariantText), 60),
                Quantity = l.Quantity, UnitPrice = l.UnitPrice, DiscountAmount = l.DiscountAmount, LineTotal = l.Kind == SaleLineKind.Text ? 0 : l.LineTotal,
                Cogs = Money.Round(l.UnitCost * l.Quantity), Department = l.Department, Taxable1 = l.Taxable1, Taxable2 = l.Taxable2,
                Tax1 = lineTax.Item1, Tax2 = lineTax.Item2, SerialNumber = l.SerialNumber, VariantId = l.VariantId, SupplierName = l.SupplierName,
                Commission = Money.Round(l.LineTotal * l.CommissionPercent / 100m), Restock = l.Restock, Epc = l.Epc,
            });
        }
        sale.Cogs = sale.Lines.Sum(l => l.Cogs);
        sale.PointsEarned = BonusPoints.Earned(settings, totals.NetBeforeTax, draft.SubtotalDiscountPercent > 0);
        foreach (var t in tenders)
            sale.Payments.Add(new SalePayment { TenderType = t.TenderType, Amount = t.Amount, Tendered = t.Tendered, Change = t.Change, AuthCode = t.AuthCode, Reference = t.Reference });

        if (session.IsTrainee) return sale;

        await using var tx = await db.Database.BeginTransactionAsync();
        Client? client = null;
        if (draft.ClientId is int clientId)
        {
            client = await db.Clients.SingleAsync(c => c.Id == clientId);
            decimal owing = onAccount + layawayOwing;
            if (owing > 0 && client.CreditLimit > 0 && client.Balance + owing > client.CreditLimit)
                throw new BusinessRuleException($"This sale would take {client.Company ?? client.LastName} over their credit limit of {client.CreditLimit:N2}.");
        }

        sale.InvoiceNumber = await db.NextNumberAsync(RetailPlusDbContext.Sequences.InvoiceNumbers);
        db.Sales.Add(sale);
        await db.SaveChangesAsync();

        await ApplyStockAndStatisticsAsync(db, sale, +1, now);
        await UpdateTagsAsync(db, sale, +1);

        if (client is not null)
        {
            client.LastPurchase = DateOnly.FromDateTime(now);
            client.Points += sale.PointsEarned - draft.PointsRedeemed;
            decimal owing = onAccount + layawayOwing;
            if (owing != 0)
            {
                client.Balance += owing;
                db.Invoices.Add(InvoiceFromSale(sale, client, draft.IsLayaway ? InvoiceKind.Layaway : InvoiceKind.Invoice, owing, paid - onAccount));
            }
        }

        var drawer = await DrawerService.GetOpenSessionAsync(db, session.StationNumber, session.LocationId);
        AddToDrawer(drawer, sale, tenders.Where(t => t.Kind != TenderKind.OnAccount), +1);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return sale;
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];

    private static Invoice InvoiceFromSale(Sale sale, Client client, InvoiceKind kind, decimal owing, decimal deposit)
    {
        var invoice = new Invoice
        {
            InvoiceNumber = sale.InvoiceNumber, Kind = kind, ClientId = client.Id, AccountNumber = client.AccountNumber, CustomerNumber = client.Number,
            FirstName = client.FirstName, LastName = client.LastName, Company = client.Company, InvoiceDate = DateOnly.FromDateTime(sale.SoldAt),
            Total = sale.Total, Balance = owing, SalesPerson = sale.Staff, Tax1Rate = sale.Tax1Rate, Tax2Rate = sale.Tax2Rate,
            ShipTo = client.ShipTo, Email = client.Email, SaleId = sale.Id,
        };
        foreach (var l in sale.Lines.Where(l => l.Kind != SaleLineKind.Text))
            invoice.Lines.Add(new InvoiceLine { LineNumber = l.LineNumber, Plu = l.Plu, Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice, DiscountAmount = l.DiscountAmount, LineTotal = l.LineTotal });
        if (deposit > 0)
            invoice.Payments.Add(new InvoicePayment { PaidOn = invoice.InvoiceDate, Amount = deposit, TenderType = "Deposit", Note = "Paid at the sale" });
        return invoice;
    }

    /// <summary>Stock counts, serial numbers and sales statistics; <paramref name="sign"/> is -1 when voiding.</summary>
    private static async Task ApplyStockAndStatisticsAsync(RetailPlusDbContext db, Sale sale, int sign, DateTime at)
    {
        int year = at.Year, month = at.Month, week = (at.DayOfYear - 1) / 7 + 1;
        foreach (var line in sale.Lines.Where(l => l.ItemId is not null && l.Kind is SaleLineKind.Sale or SaleLineKind.Return))
        {
            int itemId = line.ItemId!.Value;
            var type = await db.Items.Where(i => i.Id == itemId).Select(i => i.ItemType).SingleAsync();
            bool moves = IsStocked(type) && (line.Kind == SaleLineKind.Sale || line.Restock);
            decimal qty = line.Quantity * sign;
            if (moves)
            {
                await db.Items.Where(i => i.Id == itemId).ExecuteUpdateAsync(s => s.SetProperty(i => i.OnHand, i => i.OnHand - qty));
                if (line.VariantId is int variantId)
                    await db.ItemVariants.Where(v => v.Id == variantId).ExecuteUpdateAsync(s => s.SetProperty(v => v.OnHand, v => v.OnHand - qty));
                if (type == ItemType.Kit)
                {
                    var components = await db.KitComponents.Where(k => k.KitItemId == itemId && k.ComponentItemId != null).ToListAsync();
                    foreach (var c in components)
                    {
                        decimal cq = qty * c.Quantity;
                        await db.Items.Where(i => i.Id == c.ComponentItemId).ExecuteUpdateAsync(s => s.SetProperty(i => i.OnHand, i => i.OnHand - cq));
                    }
                }
                if (type == ItemType.Serialized && !string.IsNullOrWhiteSpace(line.SerialNumber))
                {
                    string status = (line.Kind == SaleLineKind.Sale) == (sign > 0) ? "SOLD" : "IN STOCK";
                    var today = DateOnly.FromDateTime(at);
                    await db.ItemSerials.Where(s => s.ItemId == itemId && s.SerialNumber == line.SerialNumber)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status).SetProperty(x => x.StatusDate, today));
                }
            }
            decimal gross = line.LineTotal * sign;
            await AddPeriodAsync(db, itemId, year, SalesPeriodKind.Month, month, qty, gross);
            await AddPeriodAsync(db, itemId, year, SalesPeriodKind.Week, Math.Min(week, 54), qty, gross);
            var day = DateOnly.FromDateTime(at);
            await db.Items.Where(i => i.Id == itemId).ExecuteUpdateAsync(s => s.SetProperty(i => i.LastUpdated, day));
        }
    }

    /// <summary>RFID tags of sold lines become Sold, of returned lines In stock; <paramref name="sign"/> -1 undoes a sale.</summary>
    private static async Task UpdateTagsAsync(RetailPlusDbContext db, Sale sale, int sign)
    {
        var sold = sale.Lines.Where(l => l.Epc != null && l.Kind == SaleLineKind.Sale).Select(l => l.Epc!).ToList();
        var returned = sale.Lines.Where(l => l.Epc != null && l.Kind == SaleLineKind.Return).Select(l => l.Epc!).ToList();
        if (sign < 0) (sold, returned) = (returned, sold);
        var at = sale.SoldAt;
        long invoice = sale.InvoiceNumber;
        if (sold.Count > 0)
            await db.ItemTags.Where(t => sold.Contains(t.Epc)).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, ItemTagStatus.Sold).SetProperty(t => t.SoldAt, at).SetProperty(t => t.SoldOnInvoice, invoice));
        if (returned.Count > 0)
            await db.ItemTags.Where(t => returned.Contains(t.Epc)).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, ItemTagStatus.InStock).SetProperty(t => t.SoldAt, (DateTime?)null).SetProperty(t => t.SoldOnInvoice, (long?)null));
    }

    private static async Task AddPeriodAsync(RetailPlusDbContext db, int itemId, int year, SalesPeriodKind kind, int period, decimal units, decimal gross)
    {
        int n = await db.ItemPeriodSales.Where(p => p.ItemId == itemId && p.Year == year && p.Kind == kind && p.Period == period)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Units, p => p.Units + units).SetProperty(p => p.Gross, p => p.Gross + gross));
        if (n == 0)
        {
            db.ItemPeriodSales.Add(new ItemPeriodSales { ItemId = itemId, Year = year, Kind = kind, Period = period, Units = units, Gross = gross });
            await db.SaveChangesAsync();
        }
    }

    private static void AddToDrawer(DrawerSession drawer, Sale sale, IEnumerable<TenderEntry> tenders, int sign)
    {
        var real = sale.Lines.Where(l => l.Kind != SaleLineKind.Text).ToList();
        drawer.CustomerCount += sign;
        drawer.NetSales += sign * (sale.Subtotal - sale.SubtotalDiscount - (sale.TaxInclusive ? sale.Tax1 + sale.Tax2 : 0));
        drawer.Tax1Total += sign * sale.Tax1;
        drawer.Tax2Total += sign * sale.Tax2;
        drawer.NoTaxSales += sign * real.Where(l => !l.Taxable1 && !l.Taxable2 && l.Kind == SaleLineKind.Sale).Sum(l => l.LineTotal);
        drawer.Cogs += sign * sale.Cogs;
        drawer.CustomerCredits += sign * real.Where(l => l.Kind is SaleLineKind.Return or SaleLineKind.Credit).Sum(l => -l.LineTotal);
        drawer.BottleReturns += sign * real.Where(l => l.Kind == SaleLineKind.BottleReturn).Sum(l => -l.LineTotal);
        drawer.LastSaleAt = sale.SoldAt;
        foreach (var t in tenders)
        {
            drawer.TotalReceipts += sign * t.Amount;
            var row = drawer.Tenders.FirstOrDefault(x => x.TenderType == t.TenderType);
            if (row is null) drawer.Tenders.Add(row = new DrawerSessionTender { TenderType = t.TenderType });
            row.Amount += sign * t.Amount;
        }
    }

    /// <summary>Voids a saved sale: puts stock back, takes it out of the drawer totals and cancels its invoice.</summary>
    public async Task VoidSaleAsync(int saleId, SessionInfo session, string? reason = null)
    {
        await using var db = data.Create();
        await using var tx = await db.Database.BeginTransactionAsync();
        var sale = await db.Sales.Include(s => s.Lines).Include(s => s.Payments).SingleAsync(s => s.Id == saleId);
        if (sale.Status == SaleStatus.Voided) throw new BusinessRuleException("This sale is already voided.");
        await ApplyStockAndStatisticsAsync(db, sale, -1, sale.SoldAt);
        await UpdateTagsAsync(db, sale, -1);

        var kinds = await db.TenderTypes.AsNoTracking().ToDictionaryAsync(t => t.Name, t => t.Kind);
        var tenders = sale.Payments.Select(p => new TenderEntry(p.TenderType, kinds.GetValueOrDefault(p.TenderType, TenderKind.Other), p.Amount, p.Tendered)).ToList();
        var drawer = await DrawerService.GetOpenSessionAsync(db, sale.StationNumber, sale.LocationId);
        AddToDrawer(drawer, sale, tenders.Where(t => t.Kind != TenderKind.OnAccount), -1);

        if (sale.ClientId is int clientId)
        {
            var client = await db.Clients.SingleAsync(c => c.Id == clientId);
            client.Points -= sale.PointsEarned - sale.PointsRedeemed;
            var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.SaleId == sale.Id && !i.IsVoided);
            if (invoice is not null)
            {
                client.Balance -= invoice.Balance;
                invoice.Balance = 0;
                invoice.IsVoided = true;
            }
        }
        sale.Status = SaleStatus.Voided;
        sale.VoidedAt = DateTime.Now;
        sale.VoidedBy = session.UserName;
        if (!string.IsNullOrWhiteSpace(reason)) sale.Notes = (sale.Notes + " Void: " + reason).Trim();
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task<Sale?> GetSaleAsync(int id)
    {
        await using var db = data.Create();
        return await db.Sales.AsNoTracking().Include(s => s.Lines.OrderBy(l => l.LineNumber)).Include(s => s.Payments).SingleOrDefaultAsync(s => s.Id == id);
    }

    public async Task<Sale?> FindByInvoiceNumberAsync(long invoiceNumber)
    {
        await using var db = data.Create();
        return await db.Sales.AsNoTracking().Include(s => s.Lines.OrderBy(l => l.LineNumber)).Include(s => s.Payments)
            .Where(s => s.InvoiceNumber == invoiceNumber).OrderByDescending(s => s.Id).FirstOrDefaultAsync();
    }

    /// <summary>The sales journal between two dates (inclusive), newest first.</summary>
    public async Task<List<SaleRow>> JournalAsync(DateOnly from, DateOnly to, int? station = null, int take = 5000)
    {
        await using var db = data.Create();
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var q = db.Sales.AsNoTracking().Where(s => s.SoldAt >= start && s.SoldAt < end);
        if (station is int st) q = q.Where(s => s.StationNumber == st);
        var rows = await q.OrderByDescending(s => s.SoldAt).Take(take)
            .Select(s => new { s.Id, s.InvoiceNumber, s.StationNumber, s.SoldAt, s.Staff, s.ClientName, s.Subtotal, s.Tax1, s.Tax2, s.Total, s.Status, Tenders = s.Payments.Select(p => p.TenderType).ToList() })
            .ToListAsync();
        return rows.Select(r => new SaleRow(r.Id, r.InvoiceNumber, r.StationNumber, r.SoldAt, r.Staff, r.ClientName, r.Subtotal, r.Tax1, r.Tax2, r.Total, r.Status, string.Join(", ", r.Tenders.Distinct()))).ToList();
    }

    // Suspend / recall ----------------------------------------------------------------------------------------

    public async Task<int> SuspendAsync(SaleDraft draft, string? label, int stationNumber)
    {
        await using var db = data.Create();
        var row = new SuspendedSale { StationNumber = stationNumber, SuspendedAt = DateTime.Now, Label = label ?? draft.ClientName ?? $"{draft.Lines.Count} lines", PayloadJson = JsonSerializer.Serialize(draft, Json) };
        db.SuspendedSales.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    public async Task<List<SuspendedSale>> ListSuspendedAsync()
    {
        await using var db = data.Create();
        return await db.SuspendedSales.AsNoTracking().OrderBy(s => s.SuspendedAt).ToListAsync();
    }

    /// <summary>Takes a suspended sale back (any station may recall it) and removes it from the list.</summary>
    public async Task<SaleDraft?> RecallAsync(int id)
    {
        await using var db = data.Create();
        var row = await db.SuspendedSales.SingleOrDefaultAsync(s => s.Id == id);
        if (row is null) return null;
        db.SuspendedSales.Remove(row);
        await db.SaveChangesAsync();
        return JsonSerializer.Deserialize<SaleDraft>(row.PayloadJson, Json);
    }

    // Customer orders and RMAs --------------------------------------------------------------------------------

    public async Task<List<CustomerOrder>> GetCustomerOrdersAsync(bool openOnly)
    {
        await using var db = data.Create();
        return await db.CustomerOrders.AsNoTracking().Where(o => !openOnly || !o.Paid).OrderByDescending(o => o.OrderDate).ToListAsync();
    }

    public async Task SaveCustomerOrderAsync(CustomerOrder order)
    {
        await using var db = data.Create();
        var isNew = order.Id == 0;
        if (isNew) db.CustomerOrders.Add(order); else db.CustomerOrders.Update(order);
        if (isNew && order.ItemId is int itemId)
            await db.Items.Where(i => i.Id == itemId).ExecuteUpdateAsync(s => s.SetProperty(i => i.CustomerOrdered, i => i.CustomerOrdered + order.Quantity));
        await db.SaveChangesAsync();
    }

    public async Task DeleteCustomerOrderAsync(int id)
    {
        await using var db = data.Create();
        var order = await db.CustomerOrders.SingleAsync(o => o.Id == id);
        if (order.ItemId is int itemId)
            await db.Items.Where(i => i.Id == itemId).ExecuteUpdateAsync(s => s.SetProperty(i => i.CustomerOrdered, i => i.CustomerOrdered - order.Quantity));
        db.CustomerOrders.Remove(order);
        await db.SaveChangesAsync();
    }

    public async Task<List<Rma>> GetRmasAsync(bool activeOnly)
    {
        await using var db = data.Create();
        return await db.Rmas.AsNoTracking().Where(r => !activeOnly || r.IsActive).OrderByDescending(r => r.Id).ToListAsync();
    }

    public async Task SaveRmaAsync(Rma rma)
    {
        await using var db = data.Create();
        if (rma.Id == 0)
        {
            if (string.IsNullOrWhiteSpace(rma.CustomerRmaNumber) && rma.IsCustomerRma)
                rma.CustomerRmaNumber = (await db.NextNumberAsync(RetailPlusDbContext.Sequences.RmaNumbers)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            db.Rmas.Add(rma);
        }
        else db.Rmas.Update(rma);
        await db.SaveChangesAsync();
    }
}
