using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>A row of the landed cost vouchers list.</summary>
public sealed record LandedCostRow(Guid Id, long Number, Guid StoreId, DateOnly Date, LandedCostStatus Status, LandedCostBasis Basis, string? Reference,
    decimal Total, int Receipts, string? Note, DateTime? PostedAtUtc, string? PostedBy);

/// <summary>A received purchase order a voucher can be spread over.</summary>
public sealed record ReceiptChoice(Guid PurchaseOrderId, long Number, Guid StoreId, string LocationCode, DateTime? ReceivedAt, string? Supplier, string? Description,
    int Lines, decimal Value);

/// <summary>A charge of a voucher being saved.</summary>
public sealed record LandedChargeInput(LandedCostChargeKind Kind, string? Description, decimal Amount);

/// <summary>
/// Landed cost vouchers: freight, duty, insurance and other charges for goods received on one or more purchase orders,
/// spread over the received lines by value, quantity or weight. Posting raises the average cost of the pieces still on hand
/// and puts the share of pieces already gone into cost of goods: Dr Inventory and Cost of goods, Cr Landed costs to
/// allocate (source <c>landed:{voucherId}</c>). The carrier's or customs' bill, in payables, debits that clearing account.
/// Vouchers are kept on the server.
/// </summary>
public sealed class LandedCostService(DataContext data)
{
    public async Task<List<LandedCostRow>> ListAsync(Guid? store = null, LandedCostStatus? status = null)
    {
        await using var db = data.Create();
        var q = db.LandedCostVouchers.AsNoTracking();
        if (store is Guid s) q = q.Where(v => v.StoreId == s);
        if (status is { } st) q = q.Where(v => v.Status == st);
        var rows = await q.Include(v => v.Charges).Include(v => v.Receipts).ToListAsync();
        return [.. rows.OrderByDescending(v => v.Date).ThenByDescending(v => v.Number)
            .Select(v => new LandedCostRow(v.Id, v.Number, v.StoreId, v.Date, v.Status, v.Basis, v.Reference, v.Charges.Sum(c => c.Amount), v.Receipts.Count, v.Note,
                v.PostedAtUtc, v.PostedBy))];
    }

    public async Task<LandedCostVoucher?> GetAsync(Guid id)
    {
        await using var db = data.Create();
        var voucher = await db.LandedCostVouchers.AsNoTracking().AsSplitQuery().Include(v => v.Charges).Include(v => v.Receipts).Include(v => v.Allocations)
            .SingleOrDefaultAsync(v => v.Id == id);
        if (voucher is not null) voucher.Allocations = [.. voucher.Allocations.OrderBy(a => a.Plu, StringComparer.OrdinalIgnoreCase)];
        return voucher;
    }

    /// <summary>The received purchase orders of a store, newest first, to spread a voucher over.</summary>
    public async Task<List<ReceiptChoice>> ReceiptsAsync(Guid? store = null, int take = 200)
    {
        await using var db = data.Create();
        var q = db.PurchaseOrders.AsNoTracking().Where(p => p.Status == PurchaseOrderStatus.Received);
        if (store is Guid s) q = q.Where(p => p.StoreId == s);
        var orders = await q.OrderByDescending(p => p.ReceivedAt).Take(take).Include(p => p.Lines).Include(p => p.Location).ToListAsync();
        var suppliers = await db.Suppliers.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Company);
        return [.. orders.Select(p => new ReceiptChoice(p.Id, p.Number, p.StoreId, p.Location?.Code ?? "", p.ReceivedAt,
            p.SupplierId is Guid sid ? suppliers.GetValueOrDefault(sid) : p.Lines.Select(l => l.SupplierName).FirstOrDefault(n => n != null), p.Description,
            p.Lines.Count(l => l.QuantityReceived != 0), Money.Round(p.Lines.Sum(l => l.QuantityReceived * l.CostEach))))];
    }

    /// <summary>
    /// Adds a draft voucher (<paramref name="id"/> null) or changes one: date, basis, reference, note, its charges and the
    /// purchase orders it is spread over, which must all be received and of the voucher's store.
    /// </summary>
    public async Task<LandedCostVoucher> SaveAsync(Guid? id, Guid? version, Guid storeId, DateOnly date, LandedCostBasis basis, string? reference, string? note,
        IReadOnlyList<LandedChargeInput> charges, IReadOnlyList<Guid> purchaseOrderIds)
    {
        if (charges.Any(c => c.Amount < 0)) throw new BusinessRuleException("Charges are entered as positive amounts.");
        await using var db = data.Create();
        await using var tx = await db.Database.BeginTransactionAsync();
        var orders = await db.PurchaseOrders.AsNoTracking().Where(p => purchaseOrderIds.Contains(p.Id)).ToListAsync();
        if (orders.Count != purchaseOrderIds.Distinct().Count()) throw new BusinessRuleException("A purchase order chosen no longer exists.");
        if (orders.FirstOrDefault(p => p.Status != PurchaseOrderStatus.Received) is { } open) throw new BusinessRuleException($"PO {open.Number} has not been received yet.");
        if (orders.FirstOrDefault(p => p.StoreId != storeId) is { } other) throw new BusinessRuleException($"PO {other.Number} is another store's.");
        LandedCostVoucher voucher;
        if (id is Guid existing)
        {
            voucher = await db.LandedCostVouchers.Include(v => v.Charges).Include(v => v.Receipts).SingleOrDefaultAsync(v => v.Id == existing)
                      ?? throw new BusinessRuleException("That voucher no longer exists.");
            if (version is Guid v && v != voucher.Version) throw new ConcurrencyConflictException($"Voucher {voucher.Number} was changed by someone else. Load it again.");
            if (voucher.Status != LandedCostStatus.Draft) throw new BusinessRuleException($"Voucher {voucher.Number} is posted; it cannot be changed.");
            if (voucher.StoreId != storeId) throw new BusinessRuleException("A voucher stays with its store.");
            db.LandedCostCharges.RemoveRange(voucher.Charges);
            db.LandedCostReceipts.RemoveRange(voucher.Receipts);
            voucher.Charges.Clear();
            voucher.Receipts.Clear();
            await db.SaveChangesAsync();
        }
        else
        {
            if (!await db.Stores.AnyAsync(s => s.Id == storeId)) throw new BusinessRuleException("That store does not exist.");
            voucher = new LandedCostVoucher { StoreId = storeId, Status = LandedCostStatus.Draft, CreatedBy = Cut(data.CurrentUser?.UserName, 50) };
            voucher.Number = await DocumentNumbers.NextAsync(data.For(new StationIdentity(storeId, 0), data.CurrentUser), db, RetailPlusDbContext.Sequences.LandedCostNumbers);
            db.LandedCostVouchers.Add(voucher);
        }
        voucher.Date = date;
        voucher.Basis = basis;
        voucher.Reference = Clean(reference, 40);
        voucher.Note = Clean(note, 200);
        foreach (var c in charges.Where(c => c.Amount != 0))
            voucher.Charges.Add(new LandedCostCharge { Kind = c.Kind, Description = Clean(c.Description, 100), Amount = Money.Round(c.Amount) });
        foreach (var po in purchaseOrderIds.Distinct()) voucher.Receipts.Add(new LandedCostReceipt { PurchaseOrderId = po });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException($"Voucher {voucher.Number} was changed by someone else. Load it again.");
        }
        await tx.CommitAsync();
        return voucher;
    }

    /// <summary>How the voucher would be spread now (nothing is saved): each received line's share, and how much of it goes to stock.</summary>
    public async Task<List<LandedCostAllocation>> PreviewAsync(Guid id)
    {
        await using var db = data.Create();
        var voucher = await db.LandedCostVouchers.AsNoTracking().Include(v => v.Charges).Include(v => v.Receipts).SingleOrDefaultAsync(v => v.Id == id)
                      ?? throw new BusinessRuleException("That voucher no longer exists.");
        return await AllocateAsync(db, voucher);
    }

    /// <summary>
    /// Posts a draft voucher: spreads it, raises the average cost of the items for the pieces still on hand, and posts
    /// Dr Inventory (and Cost of goods) Cr Landed costs to allocate. Needs a ledger with those accounts.
    /// </summary>
    public async Task<LandedCostVoucher> PostAsync(Guid id, Guid? version = null)
    {
        await using var db = data.Create();
        db.SuppressAudit = true;
        var voucher = await db.LandedCostVouchers.Include(v => v.Charges).Include(v => v.Receipts).Include(v => v.Allocations).SingleOrDefaultAsync(v => v.Id == id)
                      ?? throw new BusinessRuleException("That voucher no longer exists.");
        if (version is Guid v && v != voucher.Version) throw new ConcurrencyConflictException($"Voucher {voucher.Number} was changed by someone else. Load it again.");
        if (voucher.Status != LandedCostStatus.Draft) throw new BusinessRuleException($"Voucher {voucher.Number} is already posted.");
        decimal total = voucher.Charges.Sum(c => c.Amount);
        if (total <= 0) throw new BusinessRuleException("Enter the charges first.");
        if (voucher.Receipts.Count == 0) throw new BusinessRuleException("Choose the received orders the charges are for.");
        var allocations = await AllocateAsync(db, voucher);
        if (allocations.Count == 0) throw new BusinessRuleException("Nothing was received on the orders chosen.");

        foreach (var a in allocations.Where(a => a.ItemId != null && a.ToInventory != 0))
        {
            var item = await db.Items.SingleAsync(i => i.Id == a.ItemId);
            if (item.OnHand > 0) item.AverageCost = Math.Round(item.AverageCost + a.ToInventory / item.OnHand, 3, MidpointRounding.AwayFromZero);
            if (item.Price1 > 0) item.MarginPercent = Math.Clamp(Money.MarginPercent(item.Price1, item.AverageCost), -9999m, 9999m);
        }
        decimal toStock = allocations.Sum(a => a.ToInventory), toCogs = allocations.Sum(a => a.ToCostOfGoods);
        var lines = new List<JournalDraftLine>();
        if (toStock > 0) lines.Add(JournalDraftLine.Role(LedgerRoles.Inventory, toStock, 0, voucher.StoreId, "Landed costs on stock"));
        if (toCogs > 0) lines.Add(JournalDraftLine.Role(LedgerRoles.CostOfGoods, toCogs, 0, voucher.StoreId, "Landed costs on goods already sold"));
        lines.Add(JournalDraftLine.Role(LedgerRoles.LandedCostsClearing, 0, total, voucher.StoreId, "Landed costs allocated"));
        var ledger = new LedgerService(data.For(new StationIdentity(voucher.StoreId, 0), data.CurrentUser));
        var entry = await ledger.PostAsync(new JournalDraft(voucher.Date, JournalKind.Inventory, $"Landed costs, voucher {voucher.Number}", lines)
        {
            Source = $"landed:{voucher.Id}", StoreId = voucher.StoreId, Reference = voucher.Reference ?? $"LC {voucher.Number}",
        });

        foreach (var a in allocations) voucher.Allocations.Add(a);
        voucher.Status = LandedCostStatus.Posted;
        voucher.PostedAtUtc = DateTime.UtcNow;
        voucher.PostedBy = Cut(data.CurrentUser?.UserName, 50);
        voucher.JournalEntryId = entry.Id;
        db.Audit(AuditActions.LandedCostPosted, $"Voucher {voucher.Number}: {total:N2} over {allocations.Count} lines", nameof(LandedCostVoucher), voucher.Id);
        await db.SaveChangesAsync();
        return voucher;
    }

    /// <summary>Deletes a draft voucher.</summary>
    public async Task DeleteAsync(Guid id)
    {
        await using var db = data.Create();
        var voucher = await db.LandedCostVouchers.SingleOrDefaultAsync(v => v.Id == id) ?? throw new BusinessRuleException("That voucher no longer exists.");
        if (voucher.Status != LandedCostStatus.Draft) throw new BusinessRuleException($"Voucher {voucher.Number} is posted, so it stays.");
        db.LandedCostVouchers.Remove(voucher);
        await db.SaveChangesAsync();
    }

    /// <summary>Spreads the voucher's charges over the received lines of its orders, with each item's stock now.</summary>
    private static async Task<List<LandedCostAllocation>> AllocateAsync(RetailPlusDbContext db, LandedCostVoucher voucher)
    {
        var ids = voucher.Receipts.Select(r => r.PurchaseOrderId).ToList();
        var lines = await db.PurchaseOrderLines.AsNoTracking().Where(l => ids.Contains(l.PurchaseOrderId) && l.QuantityReceived > 0).ToListAsync();
        var itemIds = lines.Where(l => l.ItemId != null).Select(l => l.ItemId!.Value).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        var basis = lines.Select(l =>
        {
            var item = l.ItemId is Guid id ? items.GetValueOrDefault(id) : null;
            decimal weight = item is null ? 0 : LandedCosts.WeightInKilograms(l.QuantityReceived, item.ShipWeight, item.Weight, item.WeightUnit);
            return new LandedLine(l.Id, l.QuantityReceived, l.QuantityReceived * l.CostEach, weight);
        }).ToList();
        var shares = LandedCosts.Spread(basis, voucher.Charges.Sum(c => c.Amount), voucher.Basis);
        // Each item's stock is shared out once, over its lines in turn, so two orders of one item never count the same pieces twice.
        var stockLeft = items.ToDictionary(i => i.Key, i => Math.Max(0, i.Value.OnHand));
        var result = new List<LandedCostAllocation>();
        foreach (var l in lines.OrderBy(l => l.Plu, StringComparer.OrdinalIgnoreCase))
        {
            var b = basis.Single(x => x.Key == l.Id);
            decimal share = shares[l.Id];
            decimal onHand = l.ItemId is Guid id ? stockLeft.GetValueOrDefault(id) : 0;
            decimal toStock = LandedCosts.ToInventory(share, l.QuantityReceived, onHand);
            if (l.ItemId is Guid used) stockLeft[used] = Math.Max(0, onHand - l.QuantityReceived);
            result.Add(new LandedCostAllocation
            {
                LandedCostVoucherId = voucher.Id, PurchaseOrderLineId = l.Id, ItemId = l.ItemId, Plu = l.Plu, Description = Cut(l.Description, 60) ?? "",
                Quantity = l.QuantityReceived, Value = Money.Round(b.Value), Weight = Math.Round(b.Weight, 3), Share = share, OnHand = Math.Min(onHand, l.QuantityReceived),
                ToInventory = toStock, ToCostOfGoods = share - toStock,
            });
        }
        return result;
    }

    private static string? Clean(string? text, int length) => string.IsNullOrWhiteSpace(text) ? null : Cut(text.Trim(), length);

    private static string? Cut(string? text, int length) => text is null || text.Length <= length ? text : text[..length];
}
