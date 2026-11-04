using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>A row of the requisitions list.</summary>
public sealed record RequisitionRow(Guid Id, long Number, Guid StoreId, string LocationCode, RequisitionStatus Status, DateTime CreatedAt, string? RequestedBy,
    DateOnly? NeededBy, int Lines, decimal Total, string? DecidedBy, DateTime? DecidedAt, string? Note);

/// <summary>A line of a requisition being saved: an item (by id, or by stock code at the requisition's location).</summary>
public sealed record RequisitionLineInput(Guid? ItemId, string? Plu, decimal Quantity, DateOnly? NeededBy = null, Guid? SupplierId = null, decimal? CostEach = null,
    string? Note = null);

/// <summary>What ordering a requisition made: the purchase orders, one per supplier.</summary>
public sealed record RequisitionOrders(IReadOnlyList<(Guid Id, long Number, string? Supplier)> Orders);

/// <summary>
/// Purchase requisitions: staff ask for goods (Draft, then Submitted); someone with <see cref="Permissions.PurchasingApprove"/>,
/// within their role's limit (<see cref="PurchaseApprovalLimit"/>), approves or rejects; the approved lines then become
/// purchase orders, one per supplier (Ordered). Made at a till's Purchase Orders screen or on the web.
/// </summary>
public sealed class RequisitionService(DataContext data)
{
    public async Task<List<RequisitionRow>> ListAsync(Guid? store = null, Guid? locationId = null, RequisitionStatus? status = null, int take = 500)
    {
        await using var db = data.Create();
        var q = db.PurchaseRequisitions.AsNoTracking();
        if (store is Guid s) q = q.Where(r => r.StoreId == s);
        if (locationId is Guid l) q = q.Where(r => r.LocationId == l);
        if (status is { } st) q = q.Where(r => r.Status == st);
        var rows = await q.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Number).Take(take)
            .Select(r => new { r.Id, r.Number, r.StoreId, Code = r.Location!.Code, r.Status, r.CreatedAt, r.RequestedBy, r.NeededBy, r.DecidedBy, r.DecidedAt, r.Note })
            .ToListAsync();
        var ids = rows.Select(r => r.Id).ToList();
        var totals = (await db.PurchaseRequisitionLines.AsNoTracking().Where(x => ids.Contains(x.PurchaseRequisitionId))
                .Select(x => new { x.PurchaseRequisitionId, x.Quantity, x.CostEach }).ToListAsync())
            .GroupBy(x => x.PurchaseRequisitionId).ToDictionary(g => g.Key, g => (Lines: g.Count(), Total: Money.Round(g.Sum(x => x.Quantity * x.CostEach))));
        return [.. rows.Select(r => new RequisitionRow(r.Id, r.Number, r.StoreId, r.Code, r.Status, r.CreatedAt, r.RequestedBy, r.NeededBy,
            totals.GetValueOrDefault(r.Id).Lines, totals.GetValueOrDefault(r.Id).Total, r.DecidedBy, r.DecidedAt, r.Note))];
    }

    /// <summary>The requisition with its lines in order.</summary>
    public async Task<PurchaseRequisition?> GetAsync(Guid id)
    {
        await using var db = data.Create();
        return await db.PurchaseRequisitions.AsNoTracking().Include(r => r.Location).Include(r => r.Lines.OrderBy(l => l.LineNumber)).SingleOrDefaultAsync(r => r.Id == id);
    }

    /// <summary>The total of a requisition at the expected costs, which the approval limit is checked against.</summary>
    public static decimal Total(PurchaseRequisition requisition) => Money.Round(requisition.Lines.Sum(l => l.Quantity * l.CostEach));

    /// <summary>A new draft requisition for a location, optionally with lines already.</summary>
    public async Task<PurchaseRequisition> CreateAsync(Guid locationId, DateOnly? neededBy, string? note, IReadOnlyList<RequisitionLineInput>? lines = null)
    {
        await using var db = data.Create();
        var location = await db.Locations.AsNoTracking().SingleOrDefaultAsync(l => l.Id == locationId) ?? throw new BusinessRuleException("That location does not exist.");
        var requisition = new PurchaseRequisition
        {
            StoreId = location.StoreId, LocationId = location.Id, Status = RequisitionStatus.Draft, CreatedAt = DateTime.Now, NeededBy = neededBy,
            Note = Clean(note, 200), RequestedBy = Cut(data.CurrentUser?.UserName, 50), RequestedById = data.CurrentUser?.UserId,
        };
        requisition.Number = await DocumentNumbers.NextAsync(data, db, RetailPlusDbContext.Sequences.RequisitionNumbers);
        if (lines is not null) await FillAsync(db, requisition, lines);
        db.PurchaseRequisitions.Add(requisition);
        await db.SaveChangesAsync();
        return requisition;
    }

    /// <summary>
    /// Saves a draft (or rejected, to send again) requisition: its date needed, note and lines, which replace the old ones.
    /// <paramref name="version"/> is the one read; someone else's change in between is a conflict.
    /// </summary>
    public async Task<PurchaseRequisition> SaveAsync(Guid id, Guid? version, DateOnly? neededBy, string? note, IReadOnlyList<RequisitionLineInput> lines)
    {
        await using var db = data.Create();
        await using var tx = await db.Database.BeginTransactionAsync();
        var requisition = await db.PurchaseRequisitions.Include(r => r.Lines).SingleOrDefaultAsync(r => r.Id == id) ?? throw new BusinessRuleException("That requisition no longer exists.");
        if (version is Guid v && v != requisition.Version) throw new ConcurrencyConflictException($"Requisition {requisition.Number} was changed by someone else. Load it again.");
        if (requisition.Status is not (RequisitionStatus.Draft or RequisitionStatus.Rejected))
            throw new BusinessRuleException($"Requisition {requisition.Number} is {requisition.Status.ToString().ToLowerInvariant()}; it cannot be changed.");
        requisition.NeededBy = neededBy;
        requisition.Note = Clean(note, 200);
        db.PurchaseRequisitionLines.RemoveRange(requisition.Lines);
        requisition.Lines.Clear();
        await SaveAsync(db, requisition);   // the old lines go before new ones take their numbers
        await FillAsync(db, requisition, lines);
        if (requisition.Status == RequisitionStatus.Rejected)
        {
            requisition.Status = RequisitionStatus.Draft;
            requisition.DecidedAt = null;
            requisition.DecidedBy = null;
            requisition.DecidedById = null;
        }
        await SaveAsync(db, requisition);
        await tx.CommitAsync();
        return requisition;
    }

    /// <summary>Sends a draft for approval.</summary>
    public async Task<PurchaseRequisition> SubmitAsync(Guid id, Guid? version = null)
    {
        await using var db = data.Create();
        var requisition = await LoadAsync(db, id, version);
        if (requisition.Status != RequisitionStatus.Draft) throw new BusinessRuleException($"Requisition {requisition.Number} is {Word(requisition.Status)}; only a draft is sent.");
        if (requisition.Lines.Count == 0 || requisition.Lines.All(l => l.Quantity <= 0)) throw new BusinessRuleException("Add what is needed first.");
        requisition.Status = RequisitionStatus.Submitted;
        requisition.SubmittedAt = DateTime.Now;
        db.Audit(AuditActions.RequisitionSubmitted, $"Requisition {requisition.Number}: {requisition.Lines.Count} lines, {Total(requisition):N2}", nameof(PurchaseRequisition), requisition.Id);
        await SaveAsync(db, requisition);
        return requisition;
    }

    /// <summary>
    /// Approves a submitted requisition. The approver needs <see cref="Permissions.PurchasingApprove"/>; their role's limit
    /// must cover its total; and when the store's settings say so, the approver is not the person who asked.
    /// </summary>
    public async Task<PurchaseRequisition> ApproveAsync(Guid id, Guid? version = null, string? note = null)
    {
        await using var db = data.Create();
        var requisition = await LoadAsync(db, id, version);
        if (requisition.Status != RequisitionStatus.Submitted) throw new BusinessRuleException($"Requisition {requisition.Number} is {Word(requisition.Status)}; only a submitted one is approved.");
        await CheckApproverAsync(db, requisition);
        requisition.Status = RequisitionStatus.Approved;
        Decide(requisition, note);
        db.Audit(AuditActions.RequisitionApproved, $"Requisition {requisition.Number}: {Total(requisition):N2}" + (requisition.DecisionNote is null ? "" : " " + requisition.DecisionNote),
            nameof(PurchaseRequisition), requisition.Id);
        await SaveAsync(db, requisition);
        return requisition;
    }

    /// <summary>Rejects a submitted requisition, saying why; the person who asked may change it and send it again.</summary>
    public async Task<PurchaseRequisition> RejectAsync(Guid id, string reason, Guid? version = null)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Say why the requisition is rejected.");
        await using var db = data.Create();
        var requisition = await LoadAsync(db, id, version);
        if (requisition.Status != RequisitionStatus.Submitted) throw new BusinessRuleException($"Requisition {requisition.Number} is {Word(requisition.Status)}; only a submitted one is rejected.");
        if (data.CurrentUser is { } user && !user.Can(Permissions.PurchasingApprove)) throw new BusinessRuleException("Rejecting a requisition needs the right to approve purchases.");
        requisition.Status = RequisitionStatus.Rejected;
        Decide(requisition, reason);
        db.Audit(AuditActions.RequisitionRejected, $"Requisition {requisition.Number}: {requisition.DecisionNote}", nameof(PurchaseRequisition), requisition.Id);
        await SaveAsync(db, requisition);
        return requisition;
    }

    /// <summary>
    /// Puts the approved requisition's lines on draft purchase orders, one per supplier (lines without a supplier on one
    /// order of their own), and marks it Ordered. Returns the orders made.
    /// </summary>
    public async Task<RequisitionOrders> OrderAsync(Guid id, Guid? version = null)
    {
        PurchaseRequisition requisition;
        await using (var db = data.Create())
        {
            requisition = await LoadAsync(db, id, version);
            if (requisition.Status != RequisitionStatus.Approved) throw new BusinessRuleException($"Requisition {requisition.Number} is {Word(requisition.Status)}; only an approved one is ordered.");
        }
        var purchasing = new PurchasingService(data);
        var made = new List<(Guid, long, string?)>();
        var placed = new Dictionary<Guid, (Guid Order, Guid Line)>();
        foreach (var group in requisition.Lines.Where(l => l.ItemId != null && l.Quantity > 0).GroupBy(l => l.SupplierId))
        {
            var lines = group.OrderBy(l => l.LineNumber).ToList();
            var needed = lines.Select(l => l.NeededBy ?? requisition.NeededBy).Where(d => d is not null).Min();
            var po = await purchasing.CreateFromItemsAsync(requisition.LocationId, group.Key, $"Requisition {requisition.Number}" + (lines[0].SupplierName is { } s ? " · " + s : ""),
                [.. lines.Select(l => new OrderItem(l.ItemId!.Value, l.Quantity, l.CostEach > 0 ? l.CostEach : null))], needed);
            made.Add((po.Id, po.Number, lines[0].SupplierName));
            // The order's lines are in the same order as the requisition's.
            for (int i = 0; i < lines.Count && i < po.Lines.Count; i++) placed[lines[i].Id] = (po.Id, po.Lines[i].Id);
        }
        if (made.Count == 0) throw new BusinessRuleException("No line of this requisition names an item to order.");
        await using (var db = data.Create())
        {
            var row = await db.PurchaseRequisitions.Include(r => r.Lines).SingleAsync(r => r.Id == id);
            foreach (var line in row.Lines)
                if (placed.TryGetValue(line.Id, out var p)) (line.PurchaseOrderId, line.PurchaseOrderLineId) = (p.Order, p.Line);
            row.Status = RequisitionStatus.Ordered;
            row.OrderedAt = DateTime.Now;
            db.Audit(AuditActions.RequisitionOrdered, $"Requisition {row.Number}: PO {string.Join(", ", made.Select(m => m.Item2))}", nameof(PurchaseRequisition), row.Id);
            await db.SaveChangesAsync();
        }
        return new RequisitionOrders(made);
    }

    /// <summary>Deletes a requisition that is a draft or was rejected.</summary>
    public async Task DeleteAsync(Guid id)
    {
        await using var db = data.Create();
        var requisition = await db.PurchaseRequisitions.SingleOrDefaultAsync(r => r.Id == id) ?? throw new BusinessRuleException("That requisition no longer exists.");
        if (requisition.Status is not (RequisitionStatus.Draft or RequisitionStatus.Rejected))
            throw new BusinessRuleException($"Requisition {requisition.Number} is {Word(requisition.Status)}; it stays.");
        db.PurchaseRequisitions.Remove(requisition);
        await db.SaveChangesAsync();
    }

    // Helpers ---------------------------------------------------------------------------------------------------------

    private async Task CheckApproverAsync(RetailPlusDbContext db, PurchaseRequisition requisition)
    {
        if (data.CurrentUser is not { } user) return;
        if (!user.Can(Permissions.PurchasingApprove)) throw new BusinessRuleException("Approving a requisition needs the right to approve purchases.");
        var settings = await SupplySettingsService.ForStoreAsync(db, requisition.StoreId);
        if (settings.ApproverMustDiffer && user.UserId is Guid me && requisition.RequestedById == me)
            throw new BusinessRuleException("Someone else must approve a requisition you made.");
        if (user.UserId is not Guid userId) return;
        var role = await db.UserAccounts.AsNoTracking().Where(u => u.Id == userId).Select(u => u.RoleId).FirstOrDefaultAsync();
        if (role is null) return;
        var limit = await db.PurchaseApprovalLimits.AsNoTracking().Where(l => l.RoleId == role).Select(l => (decimal?)l.MaxAmount).FirstOrDefaultAsync();
        decimal total = Total(requisition);
        if (limit is decimal max && total > max)
            throw new BusinessRuleException($"Requisition {requisition.Number} comes to {total:N2}; your role may approve up to {max:N2}.");
    }

    private void Decide(PurchaseRequisition requisition, string? note)
    {
        requisition.DecidedAt = DateTime.Now;
        requisition.DecidedBy = Cut(data.CurrentUser?.UserName, 50);
        requisition.DecidedById = data.CurrentUser?.UserId;
        requisition.DecisionNote = Clean(note, 200);
    }

    private static async Task<PurchaseRequisition> LoadAsync(RetailPlusDbContext db, Guid id, Guid? version)
    {
        var requisition = await db.PurchaseRequisitions.Include(r => r.Lines).SingleOrDefaultAsync(r => r.Id == id) ?? throw new BusinessRuleException("That requisition no longer exists.");
        if (version is Guid v && v != requisition.Version) throw new ConcurrencyConflictException($"Requisition {requisition.Number} was changed by someone else. Load it again.");
        return requisition;
    }

    private static async Task SaveAsync(RetailPlusDbContext db, PurchaseRequisition requisition)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException($"Requisition {requisition.Number} was changed at another station. Load it again.");
        }
    }

    /// <summary>Turns the lines given into the requisition's lines: items of its location, with a supplier and a cost.</summary>
    private static async Task FillAsync(RetailPlusDbContext db, PurchaseRequisition requisition, IReadOnlyList<RequisitionLineInput> lines)
    {
        int n = 0;
        foreach (var input in lines)
        {
            if (input.Quantity <= 0) throw new BusinessRuleException("Quantities asked for are above zero.");
            var items = db.Items.AsNoTracking().Include(i => i.Supplier).Include(i => i.Suppliers).Where(i => i.LocationId == requisition.LocationId && !i.IsDeleted);
            var code = input.Plu?.Trim().ToUpperInvariant();
            Item? item = input.ItemId is Guid id ? await items.SingleOrDefaultAsync(i => i.Id == id)
                : !string.IsNullOrEmpty(code) ? await items.SingleOrDefaultAsync(i => i.Plu == code) : null;
            if (item is null) throw new BusinessRuleException(input.Plu is null ? "An item asked for is not at this location." : $"No item has the code {input.Plu.Trim()} here.");
            Guid? supplierId = input.SupplierId ?? item.SupplierId ?? item.Suppliers.OrderBy(s => s.Rank).FirstOrDefault()?.SupplierId;
            string? supplierName = supplierId is Guid sid
                ? await db.Suppliers.AsNoTracking().Where(s => s.Id == sid).Select(s => s.Company).SingleOrDefaultAsync() ?? throw new BusinessRuleException("That supplier no longer exists.")
                : null;
            var preferred = item.Suppliers.Where(s => s.SupplierId == supplierId).OrderBy(s => s.Rank).FirstOrDefault();
            decimal cost = input.CostEach is decimal c && c > 0 ? c : item.LastCost > 0 ? item.LastCost : preferred?.Cost > 0 ? preferred.Cost : item.AverageCost;
            requisition.Lines.Add(new PurchaseRequisitionLine
            {
                PurchaseRequisitionId = requisition.Id, LineNumber = ++n, ItemId = item.Id, Plu = item.Plu, Description = Cut(item.Name, 60)!, Quantity = input.Quantity,
                CostEach = cost, NeededBy = input.NeededBy, SupplierId = supplierId, SupplierName = Cut(supplierName, 30), Note = Clean(input.Note, 200),
            });
        }
    }

    private static string Word(RequisitionStatus status) => status.ToString().ToLowerInvariant();

    private static string? Clean(string? text, int length) => string.IsNullOrWhiteSpace(text) ? null : Cut(text.Trim(), length);

    private static string? Cut(string? text, int length) => text is null || text.Length <= length ? text : text[..length];
}
