using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>A bill of materials in a list.</summary>
public sealed record BomRow(Guid Id, string ProductCode, string ProductName, int Revision, bool IsActive, DateOnly? EffectiveFrom, DateOnly? EffectiveTo,
    decimal YieldQuantity, int Lines, int ByProducts, int Operations, DateTime UpdatedAtUtc);

/// <summary>
/// One line of a bill shown exploded: every level, with the quantity for one unit of the top product (scrap included)
/// and its cost. <see cref="Path"/> is the line's place in the tree ("1", "1.2", "1.2.1").
/// </summary>
public sealed record ExplodedLine(int Level, string Path, string StockCode, string Description, decimal QuantityPerUnit, decimal ScrapPercent,
    IssueMethod IssueMethod, int? OperationSequence, bool IsMade, Guid? BillOfMaterialsId, decimal UnitCost, decimal Cost);

/// <summary>A component's share of a rolled-up cost.</summary>
public sealed record CostLine(string StockCode, string Description, decimal Quantity, decimal UnitCost, decimal Cost, bool IsMade);

/// <summary>An operation's share of a rolled-up cost.</summary>
public sealed record CostOperation(int Sequence, string Name, string WorkCentre, decimal Minutes, decimal Labour, decimal Machine, decimal Overhead);

/// <summary>
/// A product's cost rolled up from its bill for <see cref="LotQuantity"/> units: materials at average cost (made
/// components at their own rolled-up cost), labour and machine time from the routing at the work centres' rates, and
/// overhead (per hour worked, plus a percentage of the material). By-products take their cost share; the product the rest.
/// </summary>
public sealed record CostRollUp(Guid BillOfMaterialsId, string ProductCode, int Revision, decimal LotQuantity, decimal Material, decimal Labour, decimal Machine,
    decimal Overhead, decimal ProductSharePercent, IReadOnlyList<CostLine> Lines, IReadOnlyList<CostOperation> Operations)
{
    public decimal Total => Material + Labour + Machine + Overhead;

    /// <summary>The cost of one unit of the product, after the by-products took their share.</summary>
    public decimal UnitCost => LotQuantity == 0 ? 0 : Math.Round(Total * ProductSharePercent / 100m / LotQuantity, 3, MidpointRounding.AwayFromZero);
}

/// <summary>A difference between two versions of a bill.</summary>
public sealed record BomDifference(string Part, string Key, string Change, string? Before, string? After);

/// <summary>
/// Bills of materials (with their by-products and routing), work centres, and what can be worked out from them: the
/// exploded bill, the rolled-up cost, a copy as a new version and the differences between two versions.
/// </summary>
public sealed class ManufacturingService(DataContext data)
{
    // Work centres ------------------------------------------------------------------------------------------------------

    /// <summary>The work centres of a store (null: every store), by code.</summary>
    public async Task<List<WorkCentre>> WorkCentresAsync(Guid? store = null, bool activeOnly = false)
    {
        await using var db = data.Create();
        var q = db.WorkCentres.AsNoTracking().Include(w => w.Location).AsQueryable();
        if (store is Guid s) q = q.Where(w => w.StoreId == s);
        if (activeOnly) q = q.Where(w => w.IsActive);
        return await q.OrderBy(w => w.Code).ToListAsync();
    }

    public async Task<WorkCentre> SaveWorkCentreAsync(WorkCentre centre)
    {
        var code = (centre.Code ?? "").Trim().ToUpperInvariant();
        var name = (centre.Name ?? "").Trim();
        if (code.Length is 0 or > 10) throw new BusinessRuleException("A work centre needs a code of up to 10 characters.");
        if (name.Length is 0 or > 50) throw new BusinessRuleException("A work centre needs a name of up to 50 characters.");
        if (centre.LabourCostPerHour < 0 || centre.MachineCostPerHour < 0 || centre.OverheadPerHour < 0)
            throw new BusinessRuleException("Hourly costs cannot be below zero.");
        if (centre.CapacityHoursPerDay is < 0 or > 24) throw new BusinessRuleException("A day has 0 to 24 hours of capacity.");
        if (centre.DaysPerWeek is < 1 or > 7) throw new BusinessRuleException("A week has 1 to 7 working days.");
        await using var db = data.Create();
        var location = await db.Locations.AsNoTracking().SingleOrDefaultAsync(l => l.Id == centre.LocationId)
            ?? throw new BusinessRuleException("Choose the location the work centre is at.");
        if (await db.WorkCentres.AnyAsync(w => w.StoreId == location.StoreId && w.Code == code && w.Id != centre.Id))
            throw new BusinessRuleException($"Work centre {code} exists already in this store.");
        var row = centre.Id == Guid.Empty ? null : await db.WorkCentres.SingleOrDefaultAsync(w => w.Id == centre.Id);
        if (row is null)
        {
            row = new WorkCentre { StoreId = location.StoreId };
            db.WorkCentres.Add(row);
        }
        else if (row.StoreId != location.StoreId)
        {
            throw new BusinessRuleException("A work centre stays in its store; choose a location of the same store.");
        }
        row.LocationId = location.Id;
        row.Code = code;
        row.Name = name;
        row.LabourCostPerHour = centre.LabourCostPerHour;
        row.MachineCostPerHour = centre.MachineCostPerHour;
        row.OverheadPerHour = centre.OverheadPerHour;
        row.CapacityHoursPerDay = centre.CapacityHoursPerDay;
        row.DaysPerWeek = centre.DaysPerWeek;
        row.IsActive = centre.IsActive;
        row.Notes = Cut(centre.Notes, 400);
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>Deletes a work centre no routing uses (make it inactive otherwise).</summary>
    public async Task DeleteWorkCentreAsync(Guid id)
    {
        await using var db = data.Create();
        var row = await db.WorkCentres.SingleOrDefaultAsync(w => w.Id == id) ?? throw new BusinessRuleException("That work centre no longer exists.");
        if (await db.RoutingOperations.AnyAsync(o => o.WorkCentreId == id))
            throw new BusinessRuleException($"{row.Code} is in a routing; make it inactive instead.");
        db.WorkCentres.Remove(row);
        await db.SaveChangesAsync();
    }

    // Bills of materials ------------------------------------------------------------------------------------------------

    public async Task<List<BomRow>> ListAsync(string? search = null, bool activeOnly = false)
    {
        await using var db = data.Create();
        var q = db.BillsOfMaterials.AsNoTracking();
        if (activeOnly) q = q.Where(b => b.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var t = search.Trim();
            q = q.Where(b => b.ProductCode.Contains(t) || b.ProductName.Contains(t));
        }
        return await q.OrderBy(b => b.ProductCode).ThenByDescending(b => b.Revision)
            .Select(b => new BomRow(b.Id, b.ProductCode, b.ProductName, b.Revision, b.IsActive, b.EffectiveFrom, b.EffectiveTo, b.YieldQuantity,
                b.Lines.Count, b.ByProducts.Count, b.Operations.Count, b.UpdatedAtUtc))
            .ToListAsync();
    }

    /// <summary>A bill with its lines, by-products and routing (operations with their work centres), in order.</summary>
    public async Task<BillOfMaterials?> GetAsync(Guid id)
    {
        await using var db = data.Create();
        return await LoadAsync(db, id);
    }

    private static async Task<BillOfMaterials?> LoadAsync(RetailPlusDbContext db, Guid id)
    {
        var bom = await db.BillsOfMaterials.AsNoTracking().Include(b => b.Lines).Include(b => b.ByProducts)
            .Include(b => b.Operations).ThenInclude(o => o.WorkCentre).AsSplitQuery().SingleOrDefaultAsync(b => b.Id == id);
        if (bom is null) return null;
        bom.Lines = [.. bom.Lines.OrderBy(l => l.LineNumber)];
        bom.ByProducts = [.. bom.ByProducts.OrderBy(l => l.LineNumber)];
        bom.Operations = [.. bom.Operations.OrderBy(o => o.Sequence)];
        return bom;
    }

    /// <summary>The bill a product is made by on <paramref name="day"/>: active, in force, the highest version. Null when it is bought.</summary>
    public async Task<BillOfMaterials?> ActiveForAsync(string productCode, DateOnly day)
    {
        await using var db = data.Create();
        var id = await ActiveIdAsync(db, productCode, day);
        return id is Guid found ? await LoadAsync(db, found) : null;
    }

    internal static async Task<Guid?> ActiveIdAsync(RetailPlusDbContext db, string productCode, DateOnly day)
    {
        var code = productCode.Trim().ToUpperInvariant();
        var bills = await db.BillsOfMaterials.AsNoTracking().Where(b => b.ProductCode == code && b.IsActive).ToListAsync();
        return bills.Where(b => b.InForce(day)).OrderByDescending(b => b.Revision).Select(b => (Guid?)b.Id).FirstOrDefault();
    }

    /// <summary>
    /// Saves a bill, its lines, by-products and operations (replacing them). A new bill without a version gets the next
    /// one for its product. Checks quantities, scrap and cost shares, that operations are unique and at existing work
    /// centres, that a component's operation is in the routing, and that no component leads back to the product.
    /// </summary>
    public async Task<BillOfMaterials> SaveAsync(BillOfMaterials bill)
    {
        var code = (bill.ProductCode ?? "").Trim().ToUpperInvariant();
        if (code.Length is 0 or > 16) throw new BusinessRuleException("A bill of materials needs the product's stock code (up to 16 characters).");
        if (bill.YieldQuantity <= 0) throw new BusinessRuleException("The yield quantity must be above zero.");
        if (bill.OverheadPercent is < 0 or > 1000) throw new BusinessRuleException("Overhead is 0 to 1000 % of material.");
        if (bill.EffectiveFrom is { } from && bill.EffectiveTo is { } to && to < from) throw new BusinessRuleException("The bill stops being in force before it starts.");
        if (bill.Lines.Count == 0) throw new BusinessRuleException("A bill needs at least one component.");
        foreach (var l in bill.Lines)
        {
            var c = (l.ComponentCode ?? "").Trim().ToUpperInvariant();
            if (c.Length is 0 or > 16) throw new BusinessRuleException("Every component needs a stock code (up to 16 characters).");
            if (c == code) throw new BusinessRuleException($"{code} cannot be a component of itself.");
            if (l.Quantity <= 0) throw new BusinessRuleException($"Component {c}: the quantity must be above zero.");
            if (l.ScrapPercent is < 0 or >= 100) throw new BusinessRuleException($"Component {c}: scrap is 0 to 99.9 %.");
        }
        foreach (var b in bill.ByProducts)
        {
            var c = (b.StockCode ?? "").Trim().ToUpperInvariant();
            if (c.Length is 0 or > 16) throw new BusinessRuleException("Every by-product needs a stock code (up to 16 characters).");
            if (c == code) throw new BusinessRuleException($"{code} is the product; it cannot be its own by-product.");
            if (b.Quantity <= 0) throw new BusinessRuleException($"By-product {c}: the quantity must be above zero.");
            if (b.CostSharePercent is < 0 or >= 100) throw new BusinessRuleException($"By-product {c}: the cost share is 0 to 99.9 %.");
        }
        if (bill.ByProducts.Sum(b => b.CostSharePercent) >= 100) throw new BusinessRuleException("The by-products' cost shares must leave some of the cost to the product (below 100 % together).");
        if (bill.Operations.GroupBy(o => o.Sequence).Any(g => g.Count() > 1)) throw new BusinessRuleException("Two operations have the same sequence number.");
        foreach (var o in bill.Operations)
        {
            if (o.Sequence <= 0) throw new BusinessRuleException("Operation sequence numbers start at 1.");
            if (string.IsNullOrWhiteSpace(o.Name)) throw new BusinessRuleException($"Operation {o.Sequence} needs a name.");
            if (o.SetupMinutes < 0 || o.RunMinutesPerUnit < 0) throw new BusinessRuleException($"Operation {o.Sequence}: times cannot be below zero.");
        }
        var sequences = bill.Operations.Select(o => o.Sequence).ToHashSet();
        foreach (var l in bill.Lines.Where(l => l.OperationSequence is not null))
            if (!sequences.Contains(l.OperationSequence!.Value))
                throw new BusinessRuleException($"Component {l.ComponentCode.Trim().ToUpperInvariant()} is used at operation {l.OperationSequence}, which is not in the routing.");

        await using var db = data.Create();
        var centres = bill.Operations.Select(o => o.WorkCentreId).Distinct().ToList();
        if (centres.Count > 0 && await db.WorkCentres.CountAsync(w => centres.Contains(w.Id)) != centres.Count)
            throw new BusinessRuleException("An operation's work centre no longer exists.");
        await CheckNoCycleAsync(db, code, bill.Lines.Select(l => l.ComponentCode.Trim().ToUpperInvariant()).ToList(), bill.Id);

        var row = bill.Id == Guid.Empty ? null
            : await db.BillsOfMaterials.Include(b => b.Lines).Include(b => b.ByProducts).Include(b => b.Operations).AsSplitQuery().SingleOrDefaultAsync(b => b.Id == bill.Id);
        int revision = bill.Revision;
        if (row is null)
        {
            if (revision <= 0) revision = (await db.BillsOfMaterials.Where(b => b.ProductCode == code).MaxAsync(b => (int?)b.Revision) ?? 0) + 1;
            row = new BillOfMaterials();
            db.BillsOfMaterials.Add(row);
        }
        else if (revision <= 0)
        {
            revision = row.Revision;
        }
        if (await db.BillsOfMaterials.AnyAsync(b => b.ProductCode == code && b.Revision == revision && b.Id != row.Id))
            throw new BusinessRuleException($"{code} has a version {revision} already.");

        row.ProductCode = code;
        row.ProductName = Cut((bill.ProductName ?? "").Trim(), 30) is { Length: > 0 } given ? given : await NameOfAsync(db, code) ?? code;
        row.Revision = revision;
        row.IsActive = bill.IsActive;
        row.EffectiveFrom = bill.EffectiveFrom;
        row.EffectiveTo = bill.EffectiveTo;
        row.YieldQuantity = bill.YieldQuantity;
        row.OverheadPercent = bill.OverheadPercent;
        row.Notes = Cut(bill.Notes?.Trim(), 400);

        db.BomLines.RemoveRange(row.Lines);
        db.BomByProducts.RemoveRange(row.ByProducts);
        db.RoutingOperations.RemoveRange(row.Operations);
        row.Lines.Clear();
        row.ByProducts.Clear();
        row.Operations.Clear();
        var names = await NamesOfAsync(db, [.. bill.Lines.Select(l => l.ComponentCode), .. bill.ByProducts.Select(b => b.StockCode)]);
        int n = 0;
        foreach (var l in bill.Lines)
        {
            var c = l.ComponentCode.Trim().ToUpperInvariant();
            row.Lines.Add(new BomLine
            {
                BillOfMaterialsId = row.Id, LineNumber = ++n, ComponentCode = c, Description = Cut(string.IsNullOrWhiteSpace(l.Description) ? names.GetValueOrDefault(c) : l.Description.Trim(), 60),
                Quantity = l.Quantity, ScrapPercent = l.ScrapPercent, IssueMethod = l.IssueMethod, OperationSequence = l.OperationSequence,
            });
        }
        n = 0;
        foreach (var b in bill.ByProducts)
        {
            var c = b.StockCode.Trim().ToUpperInvariant();
            row.ByProducts.Add(new BomByProduct
            {
                BillOfMaterialsId = row.Id, LineNumber = ++n, StockCode = c, Description = Cut(string.IsNullOrWhiteSpace(b.Description) ? names.GetValueOrDefault(c) : b.Description.Trim(), 60),
                Quantity = b.Quantity, CostSharePercent = b.CostSharePercent,
            });
        }
        foreach (var o in bill.Operations.OrderBy(o => o.Sequence))
            row.Operations.Add(new RoutingOperation
            {
                BillOfMaterialsId = row.Id, Sequence = o.Sequence, WorkCentreId = o.WorkCentreId, Name = Cut(o.Name.Trim(), 50)!,
                SetupMinutes = o.SetupMinutes, RunMinutesPerUnit = o.RunMinutesPerUnit, Instructions = Cut(o.Instructions?.Trim(), 1000),
            });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new BusinessRuleException($"{code} version {revision} was saved by someone else at the same time. Load it again.");
        }
        return (await LoadAsync(db, row.Id))!;
    }

    /// <summary>Saves only a bill's routing (the Routings page), leaving its components and by-products as they are.</summary>
    public async Task<BillOfMaterials> SaveRoutingAsync(Guid bomId, IReadOnlyList<RoutingOperation> operations)
    {
        var bill = await GetAsync(bomId) ?? throw new BusinessRuleException("That bill of materials no longer exists.");
        bill.Operations = [.. operations];
        var sequences = operations.Select(o => o.Sequence).ToHashSet();
        // Components used at an operation that is gone are used at the start instead.
        foreach (var l in bill.Lines.Where(l => l.OperationSequence is int s && !sequences.Contains(s))) l.OperationSequence = null;
        return await SaveAsync(bill);
    }

    /// <summary>
    /// Copies a bill as the product's next version, inactive until someone makes it active, so a change can be prepared
    /// while the current version stays in use.
    /// </summary>
    public async Task<BillOfMaterials> CopyAsync(Guid bomId, string? toProductCode = null)
    {
        var source = await GetAsync(bomId) ?? throw new BusinessRuleException("That bill of materials no longer exists.");
        var code = string.IsNullOrWhiteSpace(toProductCode) ? source.ProductCode : toProductCode.Trim().ToUpperInvariant();
        var copy = new BillOfMaterials
        {
            ProductCode = code, ProductName = code == source.ProductCode ? source.ProductName : "", Revision = 0, IsActive = false,
            YieldQuantity = source.YieldQuantity, OverheadPercent = source.OverheadPercent,
            Notes = Cut($"Copied from {source.ProductCode} version {source.Revision}." + (string.IsNullOrWhiteSpace(source.Notes) ? "" : " " + source.Notes), 400),
            Lines = [.. source.Lines.Select(l => new BomLine
            {
                ComponentCode = l.ComponentCode, Description = l.Description, Quantity = l.Quantity, ScrapPercent = l.ScrapPercent, IssueMethod = l.IssueMethod,
                OperationSequence = l.OperationSequence,
            })],
            ByProducts = [.. source.ByProducts.Select(b => new BomByProduct { StockCode = b.StockCode, Description = b.Description, Quantity = b.Quantity, CostSharePercent = b.CostSharePercent })],
            Operations = [.. source.Operations.Select(o => new RoutingOperation
            {
                Sequence = o.Sequence, WorkCentreId = o.WorkCentreId, Name = o.Name, SetupMinutes = o.SetupMinutes, RunMinutesPerUnit = o.RunMinutesPerUnit,
                Instructions = o.Instructions,
            })],
        };
        return await SaveAsync(copy);
    }

    /// <summary>Deletes a bill. Orders made by it keep their own copy of its lines.</summary>
    public async Task DeleteAsync(Guid bomId)
    {
        await using var db = data.Create();
        var row = await db.BillsOfMaterials.Include(b => b.Lines).Include(b => b.ByProducts).Include(b => b.Operations).AsSplitQuery()
            .SingleOrDefaultAsync(b => b.Id == bomId) ?? throw new BusinessRuleException("That bill of materials no longer exists.");
        if (await db.ProductionOrders.AnyAsync(o => o.BillOfMaterialsId == bomId && o.Status == ProductionOrderStatus.Planned))
            throw new BusinessRuleException($"A planned production order uses {row.ProductCode} version {row.Revision}; make it inactive instead.");
        db.BillsOfMaterials.Remove(row);
        await db.SaveChangesAsync();
    }

    /// <summary>What changed from one version to another: the header, components (by code), by-products and operations (by sequence).</summary>
    public async Task<List<BomDifference>> CompareAsync(Guid fromId, Guid toId)
    {
        var a = await GetAsync(fromId) ?? throw new BusinessRuleException("The first bill no longer exists.");
        var b = await GetAsync(toId) ?? throw new BusinessRuleException("The second bill no longer exists.");
        var list = new List<BomDifference>();
        void Header(string key, string? before, string? after)
        {
            if (before != after) list.Add(new BomDifference("Bill", key, "Changed", before, after));
        }
        Header("Product", a.ProductCode, b.ProductCode);
        Header("Yield", Q(a.YieldQuantity), Q(b.YieldQuantity));
        Header("Overhead % of material", Q(a.OverheadPercent), Q(b.OverheadPercent));
        Header("Active", a.IsActive ? "Yes" : "No", b.IsActive ? "Yes" : "No");
        Header("In force from", a.EffectiveFrom?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), b.EffectiveFrom?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        Header("In force to", a.EffectiveTo?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), b.EffectiveTo?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        Compare(list, "Component", a.Lines, b.Lines, l => l.ComponentCode,
            l => $"{Q(l.Quantity)} · scrap {Q(l.ScrapPercent)} % · {l.IssueMethod}" + (l.OperationSequence is int s ? $" · at operation {s}" : ""));
        Compare(list, "By-product", a.ByProducts, b.ByProducts, p => p.StockCode, p => $"{Q(p.Quantity)} · cost share {Q(p.CostSharePercent)} %");
        Compare(list, "Operation", a.Operations, b.Operations, o => o.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            o => $"{o.Name} at {o.WorkCentre?.Code} · setup {Q(o.SetupMinutes)} min · {Q(o.RunMinutesPerUnit)} min a unit");
        return list;
    }

    private static void Compare<T>(List<BomDifference> list, string part, IEnumerable<T> before, IEnumerable<T> after, Func<T, string> key, Func<T, string> show)
    {
        var old = before.GroupBy(key).ToDictionary(g => g.Key, g => string.Join(" + ", g.Select(show)), StringComparer.OrdinalIgnoreCase);
        var now = after.GroupBy(key).ToDictionary(g => g.Key, g => string.Join(" + ", g.Select(show)), StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in old)
            if (!now.TryGetValue(k, out var w)) list.Add(new BomDifference(part, k, "Removed", v, null));
            else if (v != w) list.Add(new BomDifference(part, k, "Changed", v, w));
        foreach (var (k, w) in now.Where(n => !old.ContainsKey(n.Key))) list.Add(new BomDifference(part, k, "Added", null, w));
    }

    // Explosion and cost roll-up ----------------------------------------------------------------------------------------

    /// <summary>
    /// The bill exploded over every level: a made component shows its own components under it (by its bill in force on
    /// <paramref name="day"/>). Costs are for one unit of the top product, at <paramref name="locationId"/>'s average costs
    /// (every location's, weighted by stock, when null).
    /// </summary>
    public async Task<List<ExplodedLine>> ExplodeAsync(Guid bomId, DateOnly? day = null, Guid? locationId = null)
    {
        var on = day ?? DateOnly.FromDateTime(DateTime.Today);
        await using var db = data.Create();
        var bom = await LoadAsync(db, bomId) ?? throw new BusinessRuleException("That bill of materials no longer exists.");
        var costs = new CostBook(db, on, locationId);
        var rows = new List<ExplodedLine>();
        await ExplodeIntoAsync(db, costs, bom, 1m, 1, "", rows, [bom.ProductCode], on);
        return rows;
    }

    private static async Task ExplodeIntoAsync(RetailPlusDbContext db, CostBook costs, BillOfMaterials bom, decimal perTop, int level, string prefix,
        List<ExplodedLine> rows, HashSet<string> path, DateOnly day)
    {
        int n = 0;
        foreach (var line in bom.Lines)
        {
            n++;
            var place = prefix.Length == 0 ? $"{n}" : $"{prefix}.{n}";
            decimal qty = Math.Round(line.Quantity / bom.YieldQuantity * (1 + line.ScrapPercent / 100m) * perTop, 6, MidpointRounding.AwayFromZero);
            var childId = await ActiveIdAsync(db, line.ComponentCode, day);
            decimal unit = await costs.UnitCostAsync(line.ComponentCode);
            rows.Add(new ExplodedLine(level, place, line.ComponentCode, line.Description ?? "", qty, line.ScrapPercent, line.IssueMethod, line.OperationSequence,
                childId is not null, childId, unit, Money.Round(qty * unit)));
            if (childId is Guid id && path.Add(line.ComponentCode))
            {
                var child = await LoadAsync(db, id);
                if (child is not null) await ExplodeIntoAsync(db, costs, child, qty, level + 1, place, rows, path, day);
                path.Remove(line.ComponentCode);
            }
        }
    }

    /// <summary>
    /// Rolls the cost of a bill up for <paramref name="lotQuantity"/> units (its yield by default): see <see cref="CostRollUp"/>.
    /// Setup time is spread over the lot, so a larger order costs less a unit.
    /// </summary>
    public async Task<CostRollUp> RollUpAsync(Guid bomId, decimal? lotQuantity = null, Guid? locationId = null, DateOnly? day = null)
    {
        var on = day ?? DateOnly.FromDateTime(DateTime.Today);
        await using var db = data.Create();
        var bom = await LoadAsync(db, bomId) ?? throw new BusinessRuleException("That bill of materials no longer exists.");
        return await new CostBook(db, on, locationId).RollUpAsync(bom, lotQuantity ?? bom.YieldQuantity);
    }

    /// <summary>
    /// Unit costs of stock codes: average cost (last cost when there is none) at one location, or weighted by stock over every
    /// location; made codes at the rolled-up cost of their bill. Remembers what it worked out, and stops at a loop.
    /// </summary>
    internal sealed class CostBook(RetailPlusDbContext db, DateOnly day, Guid? locationId)
    {
        private readonly Dictionary<string, decimal> _known = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _working = new(StringComparer.OrdinalIgnoreCase);

        public async Task<decimal> UnitCostAsync(string code)
        {
            if (_known.TryGetValue(code, out var known)) return known;
            decimal cost = 0;
            if (_working.Add(code))
            {
                try
                {
                    if (await ActiveIdAsync(db, code, day) is Guid bomId && await LoadAsync(db, bomId) is { } bom)
                        cost = (await RollUpAsync(bom, bom.YieldQuantity)).UnitCost;
                    else
                        cost = await StockCostAsync(db, code, locationId);
                }
                finally
                {
                    _working.Remove(code);
                }
            }
            return _known[code] = cost;
        }

        public async Task<CostRollUp> RollUpAsync(BillOfMaterials bom, decimal lot)
        {
            if (lot <= 0) lot = bom.YieldQuantity;
            _working.Add(bom.ProductCode);
            try
            {
                var lines = new List<CostLine>();
                foreach (var l in bom.Lines)
                {
                    decimal qty = l.Quantity / bom.YieldQuantity * (1 + l.ScrapPercent / 100m) * lot;
                    decimal unit = await UnitCostAsync(l.ComponentCode);
                    bool made = await ActiveIdAsync(db, l.ComponentCode, day) is not null;
                    lines.Add(new CostLine(l.ComponentCode, l.Description ?? "", Math.Round(qty, 4, MidpointRounding.AwayFromZero), unit, Money.Round(qty * unit), made));
                }
                var ops = new List<CostOperation>();
                foreach (var o in bom.Operations)
                {
                    var centre = o.WorkCentre ?? await db.WorkCentres.AsNoTracking().SingleOrDefaultAsync(w => w.Id == o.WorkCentreId);
                    decimal minutes = o.SetupMinutes + o.RunMinutesPerUnit * lot;
                    decimal hours = minutes / 60m;
                    ops.Add(new CostOperation(o.Sequence, o.Name, centre?.Code ?? "", Math.Round(minutes, 2, MidpointRounding.AwayFromZero),
                        Money.Round(hours * (centre?.LabourCostPerHour ?? 0)), Money.Round(hours * (centre?.MachineCostPerHour ?? 0)), Money.Round(hours * (centre?.OverheadPerHour ?? 0))));
                }
                decimal material = lines.Sum(l => l.Cost);
                decimal overhead = ops.Sum(o => o.Overhead) + Money.Round(material * bom.OverheadPercent / 100m);
                decimal share = 100m - bom.ByProducts.Sum(b => b.CostSharePercent);
                return new CostRollUp(bom.Id, bom.ProductCode, bom.Revision, lot, material, ops.Sum(o => o.Labour), ops.Sum(o => o.Machine), overhead, share, lines, ops);
            }
            finally
            {
                _working.Remove(bom.ProductCode);
            }
        }
    }

    /// <summary>
    /// What one unit of a stock code costs in stock: its average cost (last cost when none) at the location, or over every
    /// location weighted by what each holds (a plain average where nothing is held).
    /// </summary>
    internal static async Task<decimal> StockCostAsync(RetailPlusDbContext db, string code, Guid? locationId)
    {
        var c = code.Trim().ToUpperInvariant();
        var items = await db.Items.AsNoTracking().Where(i => i.Plu == c && !i.IsDeleted && (locationId == null || i.LocationId == locationId))
            .Select(i => new { i.OnHand, i.AverageCost, i.LastCost }).ToListAsync();
        var costed = items.Select(i => (OnHand: Math.Max(0, i.OnHand), Cost: i.AverageCost > 0 ? i.AverageCost : i.LastCost)).Where(i => i.Cost > 0).ToList();
        if (costed.Count == 0) return 0;
        decimal held = costed.Sum(i => i.OnHand);
        var cost = held > 0 ? costed.Sum(i => i.OnHand * i.Cost) / held : costed.Average(i => i.Cost);
        return Math.Round(cost, 3, MidpointRounding.AwayFromZero);
    }

    /// <summary>Refuses a bill whose components (through their own bills) lead back to the product.</summary>
    private static async Task CheckNoCycleAsync(RetailPlusDbContext db, string product, List<string> components, Guid exceptBom)
    {
        var all = await db.BillsOfMaterials.AsNoTracking().Where(b => b.IsActive && b.Id != exceptBom)
            .Select(b => new { b.ProductCode, Codes = b.Lines.Select(l => l.ComponentCode).ToList() }).ToListAsync();
        var uses = all.GroupBy(b => b.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.SelectMany(b => b.Codes).ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<(string Code, string Path)>(components.Select(c => (c, $"{product} → {c}")));
        while (stack.Count > 0)
        {
            var (code, path) = stack.Pop();
            if (string.Equals(code, product, StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException($"The bill goes round in a loop: {path}.");
            if (!seen.Add(code) || !uses.TryGetValue(code, out var next)) continue;
            foreach (var n in next) stack.Push((n, $"{path} → {n}"));
        }
    }

    private static async Task<string?> NameOfAsync(RetailPlusDbContext db, string code) =>
        (await NamesOfAsync(db, [code])).GetValueOrDefault(code);

    /// <summary>The names items have for the stock codes (from any location).</summary>
    internal static async Task<Dictionary<string, string>> NamesOfAsync(RetailPlusDbContext db, IEnumerable<string> codes)
    {
        var list = codes.Select(c => (c ?? "").Trim().ToUpperInvariant()).Where(c => c.Length > 0).Distinct().ToList();
        var rows = await db.Items.AsNoTracking().Where(i => list.Contains(i.Plu) && !i.IsDeleted).Select(i => new { i.Plu, i.Name }).ToListAsync();
        return rows.GroupBy(r => r.Plu, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
    }

    private static string Q(decimal value) => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    internal static string? Cut(string? text, int length) => text is null ? null : text.Length <= length ? text : text[..length];
}
