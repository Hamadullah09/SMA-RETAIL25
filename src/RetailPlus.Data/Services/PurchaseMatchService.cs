using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>
/// One supplier bill line that names a purchase order line: what the supplier billed (pieces, and cost per piece before
/// tax). <see cref="Quantity"/> is in pieces, like <see cref="PurchaseOrderLine.QuantityReceived"/>.
/// </summary>
public sealed record BillLineInfo(Guid BillId, Guid BillLineId, string? BillNumber, DateOnly BillDate, Guid? SupplierId, Guid PurchaseOrderId, Guid PurchaseOrderLineId,
    decimal Quantity, decimal UnitCost);

/// <summary>
/// Where the three-way match reads supplier bills from. The payables module (supplier bills, phase E1) implements it over
/// its bill lines that carry a purchase order and purchase order line; until then the server uses
/// <see cref="InMemoryBillLineSource"/>, which holds no bills unless a test adds them.
/// </summary>
public interface IBillLineSource
{
    /// <summary>Every bill line (of any bill not cancelled) that names one of these purchase orders.</summary>
    Task<IReadOnlyList<BillLineInfo>> LinesForOrdersAsync(IReadOnlyCollection<Guid> purchaseOrderIds, CancellationToken cancellationToken = default);

    /// <summary>The lines of one bill that name a purchase order line.</summary>
    Task<IReadOnlyList<BillLineInfo>> LinesOfBillAsync(Guid billId, CancellationToken cancellationToken = default);

    /// <summary>The purchase orders that bill lines dated from <paramref name="fromDate"/> to <paramref name="toDate"/> name (all dates when null).</summary>
    Task<IReadOnlyList<Guid>> OrdersBilledAsync(DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Bill lines kept in memory: the stand-in until supplier bills exist (phase E1), and what the tests add bills to. The
/// server registers one as a singleton.
/// </summary>
public sealed class InMemoryBillLineSource : IBillLineSource
{
    private readonly ConcurrentDictionary<Guid, BillLineInfo> _lines = new();

    public void Add(BillLineInfo line) => _lines[line.BillLineId] = line;

    public void Remove(Guid billLineId) => _lines.TryRemove(billLineId, out _);

    public Task<IReadOnlyList<BillLineInfo>> LinesForOrdersAsync(IReadOnlyCollection<Guid> purchaseOrderIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BillLineInfo>>([.. _lines.Values.Where(l => purchaseOrderIds.Contains(l.PurchaseOrderId))]);

    public Task<IReadOnlyList<BillLineInfo>> LinesOfBillAsync(Guid billId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BillLineInfo>>([.. _lines.Values.Where(l => l.BillId == billId)]);

    public Task<IReadOnlyList<Guid>> OrdersBilledAsync(DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>([.. _lines.Values.Where(l => (fromDate is null || l.BillDate >= fromDate) && (toDate is null || l.BillDate <= toDate))
            .Select(l => l.PurchaseOrderId).Distinct()]);
}

/// <summary>A purchase order line with what was ordered, received and billed, for the matching reports.</summary>
public sealed record MatchReportRow(Guid PurchaseOrderId, long PurchaseOrderNumber, Guid PurchaseOrderLineId, Guid StoreId, string Plu, string Description,
    string? Supplier, decimal Ordered, decimal Received, decimal Billed, decimal Difference, decimal CostEach, decimal Value, DateTime? ReceivedAt);

/// <summary>What a match run did.</summary>
public sealed record MatchRun(int Lines, int Matched, int Held, int Posted);

/// <summary>
/// The three-way match: each supplier bill line that names a purchase order line is compared with what was ordered
/// (quantity and agreed cost) and received (quantity and the cost the stock was valued at). Lines within the store's
/// tolerances (<see cref="SupplySettings"/>) are matched by themselves; others are held until someone with
/// <see cref="Permissions.PurchasingApprove"/> accepts or rejects them. For accepted lines, the difference between the
/// billed and the received cost of the pieces received goes to the items' average cost while they are still in stock, and
/// to cost of goods for the rest, as one ledger entry per order line (source <c>match:{purchaseOrderLineId}</c>):
/// Dr Inventory and Cost of goods, Cr Goods received not billed (the other way round when the bill is cheaper).
/// </summary>
public sealed class PurchaseMatchService(DataContext data, IBillLineSource bills)
{
    /// <summary>Checks every bill line of the purchase order again (new bills, changed bills, a receipt since).</summary>
    public async Task<MatchRun> MatchOrderAsync(Guid purchaseOrderId, CancellationToken cancellationToken = default) =>
        await MatchAsync([purchaseOrderId], cancellationToken);

    /// <summary>Checks the lines of one bill (and the other bills of the same order lines), as the payables module does when a bill is saved.</summary>
    public async Task<MatchRun> MatchBillAsync(Guid billId, CancellationToken cancellationToken = default)
    {
        var lines = await bills.LinesOfBillAsync(billId, cancellationToken);
        return await MatchAsync([.. lines.Select(l => l.PurchaseOrderId).Distinct()], cancellationToken);
    }

    /// <summary>Checks every purchase order a bill names.</summary>
    public async Task<MatchRun> MatchAllAsync(CancellationToken cancellationToken = default) =>
        await MatchAsync([.. await bills.OrdersBilledAsync(null, null, cancellationToken)], cancellationToken);

    /// <summary>The match results of a store (every store when null), held ones first, newest first.</summary>
    public async Task<List<BillMatch>> ListAsync(Guid? store = null, BillMatchStatus? status = null, Guid? purchaseOrderId = null)
    {
        await using var db = data.Create();
        var q = db.BillMatches.AsNoTracking();
        if (store is Guid s) q = q.Where(m => m.StoreId == s);
        if (status is { } st) q = q.Where(m => m.Status == st);
        if (purchaseOrderId is Guid po) q = q.Where(m => m.PurchaseOrderId == po);
        var rows = await q.ToListAsync();
        return [.. rows.OrderBy(m => m.Status != BillMatchStatus.Held).ThenByDescending(m => m.BillDate).ThenBy(m => m.Plu, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<BillMatch?> GetAsync(Guid id)
    {
        await using var db = data.Create();
        return await db.BillMatches.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id);
    }

    /// <summary>The bill lines of a bill that are held or rejected: the payables module should not pay them.</summary>
    public async Task<List<BillMatch>> BlockedLinesAsync(Guid billId)
    {
        await using var db = data.Create();
        return await db.BillMatches.AsNoTracking().Where(m => m.BillId == billId && (m.Status == BillMatchStatus.Held || m.Status == BillMatchStatus.Rejected)).ToListAsync();
    }

    /// <summary>True when no line of the bill is held or rejected by the match.</summary>
    public async Task<bool> IsPayableAsync(Guid billId) => (await BlockedLinesAsync(billId)).Count == 0;

    /// <summary>Accepts a held line; its price difference is then posted. Needs <see cref="Permissions.PurchasingApprove"/>.</summary>
    public async Task<BillMatch> AcceptAsync(Guid id, Guid? version = null, string? note = null) => await DecideAsync(id, version, BillMatchStatus.Accepted, note);

    /// <summary>Rejects a held line (the supplier should correct the bill). Needs <see cref="Permissions.PurchasingApprove"/>.</summary>
    public async Task<BillMatch> RejectAsync(Guid id, string reason, Guid? version = null)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Say why the bill line is rejected.");
        return await DecideAsync(id, version, BillMatchStatus.Rejected, reason);
    }

    /// <summary>Order lines received but not (fully) billed: received pieces above the billed ones, oldest receipt first.</summary>
    public async Task<List<MatchReportRow>> ReceivedNotBilledAsync(Guid? store = null, CancellationToken cancellationToken = default)
    {
        await using var db = data.Create();
        var q = db.PurchaseOrders.AsNoTracking().Where(p => p.Status == PurchaseOrderStatus.Received);
        if (store is Guid s) q = q.Where(p => p.StoreId == s);
        var orders = await q.Include(p => p.Lines).ToListAsync(cancellationToken);
        var billed = await BilledAsync(orders.Select(o => o.Id).ToList(), cancellationToken);
        var suppliers = await SupplierNamesAsync(db, cancellationToken);
        return [.. Rows(orders, billed, suppliers).Where(r => r.Received > r.Billed).OrderBy(r => r.ReceivedAt).ThenBy(r => r.PurchaseOrderNumber)];
    }

    /// <summary>Order lines billed for more pieces than were received (or billed before anything arrived).</summary>
    public async Task<List<MatchReportRow>> BilledNotReceivedAsync(Guid? store = null, CancellationToken cancellationToken = default)
    {
        var ids = await bills.OrdersBilledAsync(null, null, cancellationToken);
        await using var db = data.Create();
        var q = db.PurchaseOrders.AsNoTracking().Where(p => ids.Contains(p.Id));
        if (store is Guid s) q = q.Where(p => p.StoreId == s);
        var orders = await q.Include(p => p.Lines).ToListAsync(cancellationToken);
        var billed = await BilledAsync(orders.Select(o => o.Id).ToList(), cancellationToken);
        var suppliers = await SupplierNamesAsync(db, cancellationToken);
        return [.. Rows(orders, billed, suppliers).Where(r => r.Billed > r.Received).OrderBy(r => r.PurchaseOrderNumber)];
    }

    // The match itself --------------------------------------------------------------------------------------------------

    private async Task<MatchRun> MatchAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0) return new MatchRun(0, 0, 0, 0);
        var all = await bills.LinesForOrdersAsync(orderIds, cancellationToken);
        int lines = 0, matched = 0, held = 0, posted = 0;
        foreach (var orderId in orderIds)
        {
            var (l, m, h, p) = await MatchOneAsync(orderId, [.. all.Where(b => b.PurchaseOrderId == orderId)], cancellationToken);
            lines += l; matched += m; held += h; posted += p;
        }
        return new MatchRun(lines, matched, held, posted);
    }

    private async Task<(int Lines, int Matched, int Held, int Posted)> MatchOneAsync(Guid orderId, List<BillLineInfo> billLines, CancellationToken cancellationToken)
    {
        int matched = 0, held = 0;
        var touched = new HashSet<Guid>();
        await using (var db = data.Create())
        {
            var po = await db.PurchaseOrders.AsNoTracking().Include(p => p.Lines).SingleOrDefaultAsync(p => p.Id == orderId, cancellationToken);
            if (po is null) return (0, 0, 0, 0);
            var settings = await SupplySettingsService.ForStoreAsync(db, po.StoreId);
            var existing = await db.BillMatches.Where(m => m.PurchaseOrderId == orderId).ToListAsync(cancellationToken);
            var now = DateTime.UtcNow;
            foreach (var line in po.Lines)
            {
                var mine = billLines.Where(b => b.PurchaseOrderLineId == line.Id).OrderBy(b => b.BillDate).ThenBy(b => b.BillLineId).ToList();
                if (mine.Count == 0) continue;
                decimal ordered = line.OrderQuantity * line.CaseQuantity;
                decimal received = line.QuantityReceived;
                decimal orderedCost = line.OrderedCost > 0 ? line.OrderedCost : line.CostEach;
                decimal receivedCost = line.CostEach;
                decimal qtyAllowed = MatchTolerance.Allowed(received, settings.QuantityTolerancePercent, settings.QuantityToleranceUnits);
                decimal billedSoFar = 0;
                foreach (var bill in mine)
                {
                    decimal before = billedSoFar;
                    billedSoFar += bill.Quantity;
                    var reasons = new List<string>();
                    if (received <= 0) reasons.Add("nothing has been received on this order line yet");
                    else if (billedSoFar - received > qtyAllowed) reasons.Add($"{billedSoFar:0.###} billed in all for {received:0.###} received");
                    if (!MatchTolerance.Within(orderedCost, bill.UnitCost, settings.PriceTolerancePercent, settings.PriceToleranceAmount))
                        reasons.Add($"billed at {bill.UnitCost:N2} each for {orderedCost:N2} ordered");
                    decimal covered = Math.Clamp(received - before, 0, bill.Quantity);
                    var row = existing.FirstOrDefault(m => m.BillLineId == bill.BillLineId);
                    bool changed = row is null || row.QuantityBilled != bill.Quantity || row.BilledCost != bill.UnitCost || row.QuantityReceived != received
                                   || row.ReceivedCost != receivedCost || row.OrderedCost != orderedCost;
                    if (row is null)
                    {
                        db.BillMatches.Add(row = new BillMatch { BillLineId = bill.BillLineId });
                        existing.Add(row);
                    }
                    row.StoreId = po.StoreId;
                    row.PurchaseOrderId = po.Id;
                    row.PurchaseOrderLineId = line.Id;
                    row.BillId = bill.BillId;
                    row.BillNumber = Cut(bill.BillNumber, 40);
                    row.BillDate = bill.BillDate;
                    row.SupplierId = bill.SupplierId ?? line.SupplierId ?? po.SupplierId;
                    row.ItemId = line.ItemId;
                    row.Plu = line.Plu;
                    row.Description = Cut(line.Description, 60) ?? "";
                    row.QuantityOrdered = ordered;
                    row.QuantityReceived = received;
                    row.QuantityBilled = bill.Quantity;
                    row.QuantityMatched = covered;
                    row.OrderedCost = orderedCost;
                    row.ReceivedCost = receivedCost;
                    row.BilledCost = bill.UnitCost;
                    row.CheckedAtUtc = now;
                    // A decision stands while the line is as it was decided; a changed bill or receipt is checked again.
                    if (changed || row.Status is BillMatchStatus.Matched or BillMatchStatus.Held)
                    {
                        if (changed && row.Status is BillMatchStatus.Accepted or BillMatchStatus.Rejected)
                        {
                            row.DecidedAtUtc = null;
                            row.DecidedBy = null;
                            row.DecidedById = null;
                            row.DecisionNote = null;
                        }
                        row.Status = reasons.Count == 0 ? BillMatchStatus.Matched : BillMatchStatus.Held;
                        row.Reason = reasons.Count == 0 ? null : Cut(Capital(string.Join("; ", reasons)) + ".", 400);
                    }
                    if (row.Status == BillMatchStatus.Held) held++; else if (row.Status == BillMatchStatus.Matched) matched++;
                    touched.Add(line.Id);
                }
            }
            // Bill lines that no longer exist (the bill was changed) are no longer matched.
            var current = billLines.Select(b => b.BillLineId).ToHashSet();
            foreach (var gone in existing.Where(m => !current.Contains(m.BillLineId) && db.Entry(m).State != EntityState.Added).ToList())
            {
                touched.Add(gone.PurchaseOrderLineId);
                db.BillMatches.Remove(gone);
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        int posted = 0;
        foreach (var lineId in touched)
            if (await PostVarianceAsync(lineId, cancellationToken)) posted++;
        return (billLines.Count, matched, held, posted);
    }

    private async Task<BillMatch> DecideAsync(Guid id, Guid? version, BillMatchStatus status, string? note)
    {
        if (data.CurrentUser is { } user && !user.Can(Permissions.PurchasingApprove))
            throw new BusinessRuleException("Deciding on a held bill line needs the right to approve purchases.");
        BillMatch row;
        await using (var db = data.Create())
        {
            row = await db.BillMatches.SingleOrDefaultAsync(m => m.Id == id) ?? throw new BusinessRuleException("That bill line is no longer matched.");
            if (version is Guid v && v != row.Version) throw new ConcurrencyConflictException("Someone else decided on this bill line. Load it again.");
            if (row.Status != BillMatchStatus.Held) throw new BusinessRuleException($"This bill line is {row.Status.ToString().ToLowerInvariant()}, not held.");
            row.Status = status;
            row.DecidedAtUtc = DateTime.UtcNow;
            row.DecidedBy = Cut(data.CurrentUser?.UserName, 50);
            row.DecidedById = data.CurrentUser?.UserId;
            row.DecisionNote = string.IsNullOrWhiteSpace(note) ? null : Cut(note.Trim(), 200);
            db.Audit(status == BillMatchStatus.Accepted ? AuditActions.MatchAccepted : AuditActions.MatchRejected,
                $"Bill {row.BillNumber}, {row.Plu}: {row.QuantityBilled:0.###} at {row.BilledCost:N2}" + (row.DecisionNote is null ? "" : " " + row.DecisionNote),
                nameof(BillMatch), row.Id);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ConcurrencyConflictException("Someone else decided on this bill line. Load it again.");
            }
        }
        await PostVarianceAsync(row.PurchaseOrderLineId, CancellationToken.None);
        return (await GetAsync(id))!;
    }

    /// <summary>
    /// Posts (or posts again) the price difference of one order line from its accepted bill lines. What was already put
    /// into the item's cost stays there; a change is split between the stock on hand now and cost of goods. Returns true
    /// when something was posted.
    /// </summary>
    private async Task<bool> PostVarianceAsync(Guid purchaseOrderLineId, CancellationToken cancellationToken)
    {
        await using var db = data.Create();
        db.SuppressAudit = true;
        var accepted = await db.BillMatches.AsNoTracking()
            .Where(m => m.PurchaseOrderLineId == purchaseOrderLineId && (m.Status == BillMatchStatus.Matched || m.Status == BillMatchStatus.Accepted))
            .ToListAsync(cancellationToken);
        decimal quantity = accepted.Sum(m => m.QuantityMatched);
        decimal difference = Money.Round(accepted.Sum(m => m.QuantityMatched * (m.BilledCost - m.ReceivedCost)));
        var variance = await db.PriceVariances.SingleOrDefaultAsync(v => v.PurchaseOrderLineId == purchaseOrderLineId, cancellationToken);
        decimal before = variance?.Difference ?? 0;
        if (difference == before) return false;
        // Nothing to post where nothing was ever posted, nor (for want of a zero entry) once a difference went back to none.
        if (difference == 0 && variance is null) return false;

        var line = await db.PurchaseOrderLines.AsNoTracking().SingleAsync(l => l.Id == purchaseOrderLineId, cancellationToken);
        var po = await db.PurchaseOrders.AsNoTracking().SingleAsync(p => p.Id == line.PurchaseOrderId, cancellationToken);
        decimal change = difference - before;
        decimal changeToStock = 0;
        Item? item = line.ItemId is Guid itemId ? await db.Items.SingleOrDefaultAsync(i => i.Id == itemId, cancellationToken) : null;
        if (item is not null && quantity > 0)
        {
            changeToStock = LandedCosts.ToInventory(change, quantity, item.OnHand);
            if (changeToStock != 0 && item.OnHand > 0)
                item.AverageCost = Math.Max(0, Math.Round(item.AverageCost + changeToStock / item.OnHand, 3, MidpointRounding.AwayFromZero));
        }
        if (variance is null) db.PriceVariances.Add(variance = new PriceVariance { PurchaseOrderLineId = purchaseOrderLineId, StoreId = po.StoreId, ItemId = line.ItemId });
        variance.Quantity = quantity;
        variance.Difference = difference;
        variance.ToInventory += changeToStock;
        variance.ToCostOfGoods = difference - variance.ToInventory;
        variance.PostedAtUtc = DateTime.UtcNow;

        if (difference != 0)
        {
            var lines = new List<JournalDraftLine>();
            void Side(string role, decimal amount, string memo)
            {
                if (amount > 0) lines.Add(JournalDraftLine.Role(role, amount, 0, po.StoreId, memo));
                else if (amount < 0) lines.Add(JournalDraftLine.Role(role, 0, -amount, po.StoreId, memo));
            }
            string what = $"PO {po.Number} {line.Plu}";
            Side(LedgerRoles.Inventory, variance.ToInventory, $"{what}: price difference on stock");
            Side(LedgerRoles.CostOfGoods, variance.ToCostOfGoods, $"{what}: price difference on goods sold");
            Side(LedgerRoles.PurchasesClearing, -difference, $"{what}: billed {quantity:0.###} at another price than received");
            var ledger = new LedgerService(data.For(new StationIdentity(po.StoreId, 0), data.CurrentUser));
            var entry = await ledger.PostAsync(new JournalDraft(DateOnly.FromDateTime(DateTime.Now), JournalKind.Inventory, $"Purchase price difference, {what}", lines)
            {
                Source = $"match:{purchaseOrderLineId}", StoreId = po.StoreId, Reference = $"PO {po.Number}",
            }, cancellationToken);
            variance.JournalEntryId = entry.Id;
        }
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // Report helpers ---------------------------------------------------------------------------------------------------

    private async Task<Dictionary<Guid, decimal>> BilledAsync(List<Guid> orderIds, CancellationToken cancellationToken) =>
        orderIds.Count == 0 ? []
            : (await bills.LinesForOrdersAsync(orderIds, cancellationToken)).GroupBy(b => b.PurchaseOrderLineId).ToDictionary(g => g.Key, g => g.Sum(b => b.Quantity));

    private static async Task<Dictionary<Guid, string?>> SupplierNamesAsync(RetailPlusDbContext db, CancellationToken cancellationToken) =>
        await db.Suppliers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Company, cancellationToken);

    private static IEnumerable<MatchReportRow> Rows(List<PurchaseOrder> orders, Dictionary<Guid, decimal> billed, Dictionary<Guid, string?> suppliers) =>
        orders.SelectMany(po => po.Lines.Select(l =>
        {
            decimal b = billed.GetValueOrDefault(l.Id);
            decimal diff = l.QuantityReceived - b;
            var supplier = l.SupplierId is Guid sid ? suppliers.GetValueOrDefault(sid) ?? l.SupplierName : l.SupplierName;
            return new MatchReportRow(po.Id, po.Number, l.Id, po.StoreId, l.Plu, l.Description, supplier, l.OrderQuantity * l.CaseQuantity, l.QuantityReceived, b, diff,
                l.CostEach, Money.Round(Math.Abs(diff) * l.CostEach), po.ReceivedAt);
        }));

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string? Cut(string? text, int length) => text is null || text.Length <= length ? text : text[..length];
}
