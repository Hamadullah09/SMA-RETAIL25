using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>A component of an order: what the good output should have used against what it did use.</summary>
public sealed record MaterialUsage(string StockCode, string Description, decimal StandardQuantity, decimal ActualQuantity, decimal StandardUnitCost,
    decimal StandardCost, decimal ActualCost)
{
    /// <summary>More (positive) or less material used than the standard, at the standard price.</summary>
    public decimal UsageVariance => Money.Round((ActualQuantity - StandardQuantity) * StandardUnitCost);
    /// <summary>What the material used cost above (positive) or below its standard price.</summary>
    public decimal PriceVariance => Money.Round(ActualCost - ActualQuantity * StandardUnitCost);
}

/// <summary>
/// An order's standard cost for the good quantity made against its actual cost, and the variances: material usage and
/// price, labour, machine and overhead. Positive variances cost more than the standard.
/// </summary>
public sealed record OrderVariances(decimal OutputQuantity, decimal StandardMaterial, decimal ActualMaterial, decimal StandardLabour, decimal ActualLabour,
    decimal StandardMachine, decimal ActualMachine, decimal StandardOverhead, decimal ActualOverhead, IReadOnlyList<MaterialUsage> Materials)
{
    public decimal MaterialUsageVariance => Materials.Sum(m => m.UsageVariance);
    public decimal MaterialPriceVariance => Materials.Sum(m => m.PriceVariance);
    public decimal LabourVariance => ActualLabour - StandardLabour;
    public decimal MachineVariance => ActualMachine - StandardMachine;
    public decimal OverheadVariance => ActualOverhead - StandardOverhead;
    public decimal StandardTotal => StandardMaterial + StandardLabour + StandardMachine + StandardOverhead;
    public decimal ActualTotal => ActualMaterial + ActualLabour + ActualMachine + ActualOverhead;
    public decimal TotalVariance => ActualTotal - StandardTotal;
}

/// <summary>A ledger entry posted for an order: one per step (issue, conversion, receipt, close).</summary>
public sealed record ProductionPosting(string Step, Guid EntryId, long Number, DateOnly Date, decimal Amount, string Memo);

/// <summary>An order's costs, variances and ledger entries.</summary>
public sealed record OrderCostSummary(ProductionOrder Order, OrderCosts Costs, OrderVariances Variances, IReadOnlyList<ProductionPosting> Postings);

/// <summary>An open order's work in progress.</summary>
public sealed record WipRow(Guid OrderId, Guid StoreId, long Number, string ProductCode, string ProductName, ProductionOrderStatus Status, decimal Material,
    decimal Conversion, decimal Received, decimal WorkInProgress);

/// <summary>
/// What production orders cost, against their standard, and their postings to the general ledger. Each order posts
/// through <see cref="LedgerService.PostAsync"/>, one entry per step and month under the sources
/// <c>prod:{order}:{step}:{yyyy-MM}</c>, each the total of everything of its kind done in that month, dated on the last
/// day something was done (posting again replaces it, or posts the difference once its period is locked):
/// <list type="bullet">
/// <item><b>issue</b>: material issued and scrapped (and output scrapped back into the order): Dr Work in progress, Cr Inventory;</item>
/// <item><b>conversion</b>: labour, machine and overhead applied: Dr Work in progress, Cr Manufacturing overhead (absorbed);</item>
/// <item><b>receipt</b>: output and by-products received at their cost: Dr Inventory, Cr Work in progress;</item>
/// <item><b>close</b> (<c>prod:{order}:close</c>): once closed, what is left in work in progress: Dr Production variances, Cr Work in progress.</item>
/// </list>
/// So each month's books hold that month's production. An entry whose month is locked already is dated in the first open
/// period. The ledger is kept on the server: the server posts after every change it makes to an order and after every
/// package a till sends; tills never post.
/// </summary>
public sealed class ProductionCostingService(DataContext data)
{
    public static readonly string[] Steps = ["issue", "conversion", "receipt", "close"];

    /// <summary>The source of a step's entry: of one month (<paramref name="month"/>), or of the close.</summary>
    public static string SourceOf(Guid orderId, string step, DateOnly? month = null) =>
        month is { } m ? $"prod:{orderId:N}:{step}:{m:yyyy-MM}" : $"prod:{orderId:N}:{step}";

    /// <summary>An order's costs so far, its variances for the good quantity made, and what was posted for it.</summary>
    public async Task<OrderCostSummary?> SummaryAsync(Guid orderId)
    {
        await using var db = data.Create();
        var order = await db.ProductionOrders.AsNoTracking().Include(o => o.Lines).Include(o => o.Operations).AsSplitQuery().SingleOrDefaultAsync(o => o.Id == orderId);
        if (order is null) return null;
        var progress = await ProductionService.ProgressAsync(db, order);
        var prefix = $"prod:{orderId:N}:";
        var entries = await db.JournalEntries.AsNoTracking().Include(e => e.Lines)
            .Where(e => e.Source != null && e.Source.StartsWith(prefix) && e.Status != JournalStatus.Draft).OrderBy(e => e.Number).ToListAsync();
        // An entry's amount is what it moves: its debits, less a line that a credit to the same account cancels.
        var postings = entries.Select(e => new ProductionPosting(StepOf(e.Source![prefix.Length..]), e.Id, e.Number, e.Date,
            e.Lines.GroupBy(l => l.AccountId).Sum(g => Math.Max(0, g.Sum(l => l.Debit - l.Credit))), e.Memo)).ToList();
        return new OrderCostSummary(order, progress.Costs, Variances(order, progress), postings);
    }

    /// <summary>The step of a source after its order ("issue:2026-11", "close#adj1"): "issue", "close (change)".</summary>
    private static string StepOf(string rest)
    {
        int adj = rest.IndexOf("#adj", StringComparison.Ordinal);
        var step = adj < 0 ? rest : rest[..adj];
        int month = step.IndexOf(':', StringComparison.Ordinal);
        if (month >= 0) step = step[..month];
        return adj < 0 ? step : step + " (change)";
    }

    /// <summary>The variances of an order (see <see cref="OrderVariances"/>).</summary>
    public static OrderVariances Variances(ProductionOrder order, OrderProgress progress)
    {
        decimal q = progress.Received;
        var costs = progress.Costs;
        var codes = progress.ItemCodes;
        var materials = new List<MaterialUsage>();
        var used = progress.Movements.Where(m => m.Kind is StockMovementKind.ProductionIssue or StockMovementKind.ProductionScrap).ToList();
        foreach (var line in order.Lines.Where(l => l.Kind == ProductionLineKind.Component).OrderBy(l => l.LineNumber))
        {
            var mine = used.Where(m => string.Equals(codes.GetValueOrDefault(m.ItemId), line.StockCode, StringComparison.OrdinalIgnoreCase)).ToList();
            decimal stdQty = Math.Round(line.QuantityPerUnit * q, 3, MidpointRounding.AwayFromZero);
            materials.Add(new MaterialUsage(line.StockCode, line.Description, stdQty, -mine.Sum(m => m.Quantity), line.StandardUnitCost,
                Money.Round(stdQty * line.StandardUnitCost), Money.Round(-mine.Sum(m => m.Quantity * m.UnitCost))));
        }
        // Material issued that is not on the bill (a substitute): all of it is a usage variance.
        var known = order.Lines.Select(l => l.StockCode).Append(order.ProductCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var g in used.Where(m => !known.Contains(codes.GetValueOrDefault(m.ItemId) ?? "")).GroupBy(m => codes.GetValueOrDefault(m.ItemId) ?? ""))
        {
            decimal qty = -g.Sum(m => m.Quantity), cost = Money.Round(-g.Sum(m => m.Quantity * m.UnitCost));
            materials.Add(new MaterialUsage(g.Key, "Not on the bill", 0, qty, qty == 0 ? 0 : Math.Round(cost / qty, 3, MidpointRounding.AwayFromZero), 0, cost));
        }
        decimal stdMaterial = q == 0 ? 0 : Money.Round(order.StandardMaterialCost * q);
        decimal stdLabour = q == 0 ? 0 : Money.Round(order.Operations.Sum(o => o.StandardMinutes(q) / 60m * o.LabourRate));
        decimal stdMachine = q == 0 ? 0 : Money.Round(order.Operations.Sum(o => o.StandardMinutes(q) / 60m * o.MachineRate));
        decimal stdOverhead = q == 0 ? 0 : Money.Round(order.Operations.Sum(o => o.StandardMinutes(q) / 60m * o.OverheadRate) + stdMaterial * order.OverheadPercent / 100m);
        return new OrderVariances(q, stdMaterial, costs.Material, stdLabour, costs.Labour, stdMachine, costs.Machine, stdOverhead,
            costs.HourlyOverhead + costs.MaterialOverhead, materials);
    }

    /// <summary>The work in progress of the store's orders that hold any (null: every store).</summary>
    public async Task<List<WipRow>> WorkInProgressAsync(Guid? store = null)
    {
        await using var db = data.Create();
        var q = db.ProductionOrders.AsNoTracking().Include(o => o.Lines).Include(o => o.Operations).AsSplitQuery()
            .Where(o => o.Status == ProductionOrderStatus.Released || o.Status == ProductionOrderStatus.InProgress || o.Status == ProductionOrderStatus.Completed);
        if (store is Guid s) q = q.Where(o => o.StoreId == s);
        var rows = new List<WipRow>();
        foreach (var order in await q.OrderBy(o => o.Number).ToListAsync())
        {
            var costs = (await ProductionService.ProgressAsync(db, order)).Costs;
            if (costs.Charged == 0 && costs.Received == 0) continue;
            rows.Add(new WipRow(order.Id, order.StoreId, order.Number, order.ProductCode, order.ProductName, order.Status, costs.Material + costs.OutputScrap,
                costs.Conversion, costs.Received, costs.WorkInProgress));
        }
        return rows;
    }

    /// <summary>
    /// Posts an order's costs to the ledger, step by step and month by month (see the class). A step with nothing in it is
    /// not posted; one that came back to nothing after it was posted (all the material given back) is posted again as an
    /// entry whose lines cancel out. Returns the entries that hold the postings.
    /// </summary>
    public async Task<List<JournalEntry>> PostAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        ProductionOrder order;
        OrderProgress progress;
        HashSet<string> posted;
        var prefix = $"prod:{orderId:N}:";
        await using (var db = data.Create())
        {
            order = await db.ProductionOrders.AsNoTracking().Include(o => o.Lines).Include(o => o.Operations).AsSplitQuery().SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken)
                ?? throw new BusinessRuleException("That production order no longer exists.");
            progress = await ProductionService.ProgressAsync(db, order);
            posted = (await db.JournalEntries.AsNoTracking().Where(e => e.Source != null && e.Source.StartsWith(prefix) && e.Status == JournalStatus.Posted)
                .Select(e => e.Source!).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        }
        var codes = progress.ItemCodes;
        bool IsOutput(StockMovement m) => codes.GetValueOrDefault(m.ItemId) is { } code
            && (string.Equals(code, order.ProductCode, StringComparison.OrdinalIgnoreCase)
                || order.Lines.Any(l => l.Kind == ProductionLineKind.ByProduct && string.Equals(l.StockCode, code, StringComparison.OrdinalIgnoreCase)));
        static decimal Value(StockMovement m) => m.Quantity * m.UnitCost;
        static DateOnly MonthOf(DateTime at) => new(at.Year, at.Month, 1);
        static DateOnly LastOf(IEnumerable<DateTime> times) => DateOnly.FromDateTime(times.Max());
        var used = progress.Movements.Where(m => m.Kind is StockMovementKind.ProductionIssue or StockMovementKind.ProductionScrap).ToList();
        var receipts = progress.Movements.Where(m => m.Kind == StockMovementKind.ProductionReceipt).ToList();
        var months = used.Select(m => MonthOf(m.At)).Concat(receipts.Select(m => MonthOf(m.At))).Concat(progress.Reports.Select(r => MonthOf(r.At)))
            .Distinct().Order().ToList();

        var steps = new List<(string Source, decimal Amount, DateOnly Date, string Debit, string Credit, string What)>();
        decimal charged = 0, received = 0;
        foreach (var month in months)
        {
            var inMonth = used.Where(m => MonthOf(m.At) == month).ToList();
            var reports = progress.Reports.Where(r => MonthOf(r.At) == month).ToList();
            var outputs = receipts.Where(m => MonthOf(m.At) == month).ToList();
            // Material and output scrapped back into the order; the bill's overhead percentage is on the material only.
            decimal issue = Money.Round(-inMonth.Sum(Value));
            decimal material = Money.Round(-inMonth.Where(m => !IsOutput(m)).Sum(Value));
            decimal conversion = Money.Round(reports.Sum(r => r.LabourCost + r.MachineCost + r.OverheadCost)) + Money.Round(material * order.OverheadPercent / 100m);
            decimal receipt = Money.Round(outputs.Sum(Value));
            charged += issue + conversion;
            received += receipt;
            if (inMonth.Count > 0)
                steps.Add((SourceOf(orderId, "issue", month), issue, LastOf(inMonth.Select(m => m.At)), LedgerRoles.WorkInProgress, LedgerRoles.Inventory, "material issued"));
            if (reports.Count > 0 || inMonth.Count > 0)
                steps.Add((SourceOf(orderId, "conversion", month), conversion, LastOf(reports.Select(r => r.At).Concat(inMonth.Select(m => m.At))), LedgerRoles.WorkInProgress,
                    LedgerRoles.ManufacturingOverhead, "labour, machine and overhead applied"));
            if (outputs.Count > 0)
                steps.Add((SourceOf(orderId, "receipt", month), receipt, LastOf(outputs.Select(m => m.At)), LedgerRoles.Inventory, LedgerRoles.WorkInProgress, "output received"));
        }
        // What the months left in work in progress, so the order's work in progress in the ledger comes to nothing.
        if (order.Status == ProductionOrderStatus.Closed)
            steps.Add((SourceOf(orderId, "close"), charged - received, DateOnly.FromDateTime(order.ClosedAt ?? DateTime.Today), LedgerRoles.ProductionVariance,
                LedgerRoles.WorkInProgress, "variance at close"));

        var ledger = new LedgerService(data);
        var entries = new List<JournalEntry>();
        foreach (var (source, amount, date, debit, credit, what) in steps)
        {
            var memo = $"Production order {order.Number} ({order.ProductCode}): {what}";
            List<JournalDraftLine> lines;
            if (amount != 0)
            {
                var (dr, cr) = amount > 0 ? (debit, credit) : (credit, debit);
                decimal value = Math.Abs(amount);
                lines = [JournalDraftLine.Role(dr, value, 0, order.StoreId), JournalDraftLine.Role(cr, 0, value, order.StoreId)];
            }
            else if (posted.Contains(source))
            {
                // An entry cannot be of nothing: one whose lines cancel out takes the place of what was posted.
                memo += ", nothing left";
                lines = [JournalDraftLine.Role(debit, 0.01m, 0, order.StoreId), JournalDraftLine.Role(debit, 0, 0.01m, order.StoreId)];
            }
            else
            {
                continue;
            }
            var draft = new JournalDraft(await OpenDateAsync(ledger, date), JournalKind.Production, memo, lines)
            {
                Source = source, StoreId = order.StoreId, Reference = order.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            entries.Add(await ledger.PostAsync(draft, cancellationToken));
        }
        return entries;
    }

    /// <summary>
    /// <paramref name="date"/> when its period is open, else the first day of the first open period after it: work done in
    /// a month that is locked already (a till that sent late) goes into the books of the first month still open.
    /// </summary>
    private async Task<DateOnly> OpenDateAsync(LedgerService ledger, DateOnly date)
    {
        if (await ledger.IsOpenAsync(date)) return date;
        for (int tries = 0; tries < 2; tries++)
        {
            await using (var db = data.Create())
            {
                var period = await db.FiscalPeriods.AsNoTracking().Where(p => p.Status == FiscalStatus.Open && p.EndDate >= date).OrderBy(p => p.StartDate).FirstOrDefaultAsync();
                if (period is not null) return period.StartDate > date ? period.StartDate : date;
                var last = await db.FiscalYears.AsNoTracking().OrderByDescending(y => y.EndDate).Select(y => (DateOnly?)y.EndDate).FirstOrDefaultAsync();
                if (last is not DateOnly end) break;
                // The year after the last one is made (with its open periods) and looked at again.
                await ledger.EnsureYearAsync(end.AddDays(1));
            }
        }
        throw new BusinessRuleException($"There is no open period on or after {date:d} to post production costs in; open one first.");
    }

    /// <summary>
    /// Posts every order of a store (null: every store) that may have changed: released, in progress and completed ones,
    /// and those closed in the last <paramref name="closedWithinDays"/> days. Orders whose posting fails (a missing account
    /// role, a locked period with no open one after it) are skipped and named in the answer.
    /// </summary>
    public async Task<(int Orders, List<string> Problems)> PostOpenAsync(Guid? store = null, int closedWithinDays = 31, CancellationToken cancellationToken = default)
    {
        List<(Guid Id, long Number)> orders;
        var since = DateTime.Now.AddDays(-closedWithinDays);
        await using (var db = data.Create())
        {
            var q = db.ProductionOrders.AsNoTracking().Where(o => o.Status == ProductionOrderStatus.Released || o.Status == ProductionOrderStatus.InProgress
                || o.Status == ProductionOrderStatus.Completed || (o.Status == ProductionOrderStatus.Closed && o.ClosedAt >= since));
            if (store is Guid s) q = q.Where(o => o.StoreId == s);
            orders = [.. (await q.Select(o => new { o.Id, o.Number }).ToListAsync(cancellationToken)).Select(o => (o.Id, o.Number))];
        }
        var problems = new List<string>();
        int done = 0;
        foreach (var (id, number) in orders)
        {
            try
            {
                await PostAsync(id, cancellationToken);
                done++;
            }
            catch (BusinessRuleException ex)
            {
                problems.Add($"Order {number}: {ex.Message}");
            }
        }
        return (done, problems);
    }
}
