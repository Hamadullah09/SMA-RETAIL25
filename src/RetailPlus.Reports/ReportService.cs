using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Data.Services;

namespace RetailPlus.Reports;

/// <summary>A report the Reports window offers, with the inputs it needs.</summary>
public sealed record ReportDefinition(string Key, string Group, string Name, bool NeedsDates);

/// <summary>The reports of the Reports menu, computed from the database.</summary>
public sealed class ReportService(DataContext data)
{
    public static readonly IReadOnlyList<ReportDefinition> All =
    [
        new("daily", "Sales", "Daily sales summary", true),
        new("dept", "Sales", "Sales by department", true),
        new("items", "Sales", "Sales by item (best sellers)", true),
        new("staff", "Sales", "Sales and commission by staff", true),
        new("hourly", "Sales", "Sales by hour of day", true),
        new("tenders", "Sales", "Payments by tender type", true),
        new("tax", "Sales", "Sales tax collected", true),
        new("voids", "Sales", "Voided sales", true),
        new("stock", "Inventory", "Inventory valuation", false),
        new("reorder", "Inventory", "Items at or below reorder point", false),
        new("pricelist", "Inventory", "Price list", false),
        new("negative", "Inventory", "Items with negative stock", false),
        new("adjust", "Inventory", "Stock adjustments", true),
        new("nosales", "Inventory", "Items with no sales in period", true),
        new("clients", "Clients", "Client list", false),
        new("balances", "Clients", "Client balances", false),
        new("aging", "Clients", "Aged receivables", false),
        new("points", "Clients", "Bonus points", false),
        new("suppliers", "Suppliers", "Supplier list", false),
        new("orders", "Suppliers", "Open purchase orders", false),
        new("drawer", "Drawer", "End of day history", true),
    ];

    public async Task<ReportTable> RunAsync(string key, int locationId, DateOnly from, DateOnly to)
    {
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var period = $"{from:d} to {to:d}";
        await using var db = data.Create();
        var sales = db.Sales.AsNoTracking().Where(s => s.SoldAt >= start && s.SoldAt < end && s.LocationId == locationId);
        var completed = sales.Where(s => s.Status == SaleStatus.Completed);
        var lines = db.SaleLines.AsNoTracking().Where(l => completed.Any(s => s.Id == l.SaleId));
        var items = db.Items.AsNoTracking().Where(i => i.LocationId == locationId && !i.IsDeleted);

        switch (key)
        {
            case "daily":
            {
                var rows = await completed.GroupBy(s => s.SoldAt.Date)
                    .Select(g => new { Day = g.Key, Count = g.Count(), Net = g.Sum(s => s.Subtotal - s.SubtotalDiscount), T1 = g.Sum(s => s.Tax1), T2 = g.Sum(s => s.Tax2), Total = g.Sum(s => s.Total), Cogs = g.Sum(s => s.Cogs) })
                    .OrderBy(r => r.Day).ToListAsync();
                return Table("Daily sales summary", period, [Col("Date", ColumnKind.Date), Col("Customers", ColumnKind.Count, true), Col("Net sales", ColumnKind.Money, true),
                    Col("Tax 1", ColumnKind.Money, true), Col("Tax 2", ColumnKind.Money, true), Col("Total", ColumnKind.Money, true), Col("Cost", ColumnKind.Money, true), Col("Profit", ColumnKind.Money, true), Col("Margin", ColumnKind.Percent)],
                    rows.Select(r => Row(DateOnly.FromDateTime(r.Day), r.Count, r.Net, r.T1, r.T2, r.Total, r.Cogs, r.Net - r.Cogs, r.Net == 0 ? 0m : (r.Net - r.Cogs) / r.Net * 100)));
            }
            case "dept":
            {
                var rows = await lines.Where(l => l.Kind == SaleLineKind.Sale || l.Kind == SaleLineKind.Return)
                    .GroupBy(l => l.Department ?? "(none)")
                    .Select(g => new { Dept = g.Key, Units = g.Sum(l => l.Quantity), Sales = g.Sum(l => l.LineTotal), Cogs = g.Sum(l => l.Cogs) })
                    .OrderByDescending(r => r.Sales).ToListAsync();
                return Table("Sales by department", period, [Col("Department"), Col("Units", ColumnKind.Quantity, true), Col("Sales", ColumnKind.Money, true), Col("Cost", ColumnKind.Money, true), Col("Profit", ColumnKind.Money, true), Col("Margin", ColumnKind.Percent)],
                    rows.Select(r => Row(r.Dept, r.Units, r.Sales, r.Cogs, r.Sales - r.Cogs, r.Sales == 0 ? 0m : (r.Sales - r.Cogs) / r.Sales * 100)));
            }
            case "items":
            {
                var rows = await lines.Where(l => l.Kind == SaleLineKind.Sale || l.Kind == SaleLineKind.Return)
                    .GroupBy(l => new { l.Plu, l.Description })
                    .Select(g => new { g.Key.Plu, g.Key.Description, Units = g.Sum(l => l.Quantity), Sales = g.Sum(l => l.LineTotal), Cogs = g.Sum(l => l.Cogs) })
                    .OrderByDescending(r => r.Sales).Take(500).ToListAsync();
                return Table("Sales by item", period, [Col("Stock code"), Col("Description"), Col("Units", ColumnKind.Quantity, true), Col("Sales", ColumnKind.Money, true), Col("Cost", ColumnKind.Money, true), Col("Profit", ColumnKind.Money, true)],
                    rows.Select(r => Row(r.Plu, r.Description, r.Units, r.Sales, r.Cogs, r.Sales - r.Cogs)));
            }
            case "staff":
            {
                var rows = await completed.GroupBy(s => s.Staff ?? "(none)")
                    .Select(g => new { Staff = g.Key, Count = g.Count(), Net = g.Sum(s => s.Subtotal - s.SubtotalDiscount), Commission = g.SelectMany(s => s.Lines).Sum(l => l.Commission) })
                    .OrderByDescending(r => r.Net).ToListAsync();
                return Table("Sales and commission by staff", period, [Col("Staff"), Col("Sales", ColumnKind.Count, true), Col("Net sales", ColumnKind.Money, true), Col("Average sale", ColumnKind.Money), Col("Commission", ColumnKind.Money, true)],
                    rows.Select(r => Row(r.Staff, r.Count, r.Net, r.Count == 0 ? 0m : r.Net / r.Count, r.Commission)));
            }
            case "hourly":
            {
                var rows = await completed.GroupBy(s => s.SoldAt.Hour)
                    .Select(g => new { Hour = g.Key, Count = g.Count(), Total = g.Sum(s => s.Total) }).OrderBy(r => r.Hour).ToListAsync();
                return Table("Sales by hour of day", period, [Col("Hour"), Col("Customers", ColumnKind.Count, true), Col("Total", ColumnKind.Money, true), Col("Average", ColumnKind.Money)],
                    rows.Select(r => Row($"{r.Hour:00}:00–{r.Hour:00}:59", r.Count, r.Total, r.Count == 0 ? 0m : r.Total / r.Count)));
            }
            case "tenders":
            {
                var rows = await db.SalePayments.AsNoTracking().Where(p => completed.Any(s => s.Id == p.SaleId))
                    .GroupBy(p => p.TenderType).Select(g => new { Tender = g.Key, Count = g.Count(), Amount = g.Sum(p => p.Amount) })
                    .OrderByDescending(r => r.Amount).ToListAsync();
                var received = await db.InvoicePayments.AsNoTracking().Where(p => p.PaidOn >= from && p.PaidOn <= to && p.TenderType != "Deposit")
                    .GroupBy(p => p.TenderType).Select(g => new { Tender = g.Key, Count = g.Count(), Amount = g.Sum(p => p.Amount) }).ToListAsync();
                return Table("Payments by tender type", period, [Col("Tender"), Col("Source"), Col("Count", ColumnKind.Count, true), Col("Amount", ColumnKind.Money, true)],
                    rows.Select(r => Row(r.Tender, "Sales", r.Count, r.Amount)).Concat(received.Select(r => Row(r.Tender, "Account payments", r.Count, r.Amount))));
            }
            case "tax":
            {
                var rows = await completed.GroupBy(s => s.SoldAt.Date)
                    .Select(g => new { Day = g.Key, Taxable = g.SelectMany(s => s.Lines).Where(l => l.Taxable1 || l.Taxable2).Sum(l => l.LineTotal), NoTax = g.SelectMany(s => s.Lines).Where(l => !l.Taxable1 && !l.Taxable2).Sum(l => l.LineTotal), T1 = g.Sum(s => s.Tax1), T2 = g.Sum(s => s.Tax2) })
                    .OrderBy(r => r.Day).ToListAsync();
                return Table("Sales tax collected", period, [Col("Date", ColumnKind.Date), Col("Taxable sales", ColumnKind.Money, true), Col("Non-taxable", ColumnKind.Money, true), Col("Tax 1", ColumnKind.Money, true), Col("Tax 2", ColumnKind.Money, true)],
                    rows.Select(r => Row(DateOnly.FromDateTime(r.Day), r.Taxable, r.NoTax, r.T1, r.T2)));
            }
            case "voids":
            {
                var rows = await sales.Where(s => s.Status == SaleStatus.Voided).OrderBy(s => s.SoldAt)
                    .Select(s => new { s.InvoiceNumber, s.SoldAt, s.StationNumber, s.Staff, s.VoidedBy, s.VoidedAt, s.Total }).ToListAsync();
                return Table("Voided sales", period, [Col("Invoice", ColumnKind.Count), Col("Sold", ColumnKind.Date), Col("Station", ColumnKind.Count), Col("Staff"), Col("Voided by"), Col("Voided", ColumnKind.Date), Col("Total", ColumnKind.Money, true)],
                    rows.Select(r => Row(r.InvoiceNumber, r.SoldAt, r.StationNumber, r.Staff, r.VoidedBy, r.VoidedAt, r.Total)));
            }
            case "stock":
            {
                var rows = await items.OrderBy(i => i.Plu)
                    .Select(i => new { i.Plu, i.Name, Dept = i.Department != null ? i.Department.Name : null, i.OnHand, Cost = i.AverageCost > 0 ? i.AverageCost : i.LastCost, i.Price1 }).ToListAsync();
                return Table("Inventory valuation", "On hand at cost and at retail", [Col("Stock code"), Col("Description"), Col("Department"), Col("On hand", ColumnKind.Quantity, true), Col("Cost", ColumnKind.Money), Col("Value at cost", ColumnKind.Money, true), Col("Price", ColumnKind.Money), Col("Value at retail", ColumnKind.Money, true)],
                    rows.Select(r => Row(r.Plu, r.Name, r.Dept, r.OnHand, r.Cost, Math.Round(r.OnHand * r.Cost, 2), r.Price1, Math.Round(r.OnHand * r.Price1, 2))));
            }
            case "reorder":
            {
                var rows = await items.Where(i => i.ReorderPoint > 0 && i.OnHand + i.OnOrder <= i.ReorderPoint).OrderBy(i => i.Plu)
                    .Select(i => new { i.Plu, i.Name, Supplier = i.Supplier != null ? i.Supplier.Company : null, i.OnHand, i.OnOrder, i.ReorderPoint, i.ReorderQuantity }).ToListAsync();
                return Table("Items at or below reorder point", null, [Col("Stock code"), Col("Description"), Col("Supplier"), Col("On hand", ColumnKind.Quantity), Col("On order", ColumnKind.Quantity), Col("Reorder point", ColumnKind.Quantity), Col("Reorder qty", ColumnKind.Quantity)],
                    rows.Select(r => Row(r.Plu, r.Name, r.Supplier, r.OnHand, r.OnOrder, r.ReorderPoint, r.ReorderQuantity)));
            }
            case "pricelist":
            {
                var rows = await items.OrderBy(i => i.Name).Select(i => new { i.Plu, i.Name, i.Size, i.Price1, i.Price2, i.Price3, i.Price4 }).ToListAsync();
                return Table("Price list", null, [Col("Stock code"), Col("Description"), Col("Size"), Col("Price 1", ColumnKind.Money), Col("Price 2", ColumnKind.Money), Col("Price 3", ColumnKind.Money), Col("Price 4", ColumnKind.Money)],
                    rows.Select(r => Row(r.Plu, r.Name, r.Size, r.Price1, r.Price2, r.Price3, r.Price4)));
            }
            case "negative":
            {
                var rows = await items.Where(i => i.OnHand < 0).OrderBy(i => i.Plu).Select(i => new { i.Plu, i.Name, i.OnHand }).ToListAsync();
                return Table("Items with negative stock", null, [Col("Stock code"), Col("Description"), Col("On hand", ColumnKind.Quantity, true)], rows.Select(r => Row(r.Plu, r.Name, r.OnHand)));
            }
            case "adjust":
            {
                var rows = await db.StockAdjustments.AsNoTracking().Where(a => a.At >= start && a.At < end)
                    .Join(items, a => a.ItemId, i => i.Id, (a, i) => new { a.At, i.Plu, i.Name, a.Quantity, a.OnHandAfter, a.Reason, a.User })
                    .OrderBy(r => r.At).ToListAsync();
                return Table("Stock adjustments", period, [Col("When", ColumnKind.Date), Col("Stock code"), Col("Description"), Col("Change", ColumnKind.Quantity, true), Col("On hand after", ColumnKind.Quantity), Col("Reason"), Col("User")],
                    rows.Select(r => Row(r.At, r.Plu, r.Name, r.Quantity, r.OnHandAfter, r.Reason, r.User)));
            }
            case "nosales":
            {
                var sold = lines.Where(l => l.ItemId != null).Select(l => l.ItemId!.Value).Distinct();
                var rows = await items.Where(i => !sold.Contains(i.Id) && i.OnHand > 0).OrderBy(i => i.Plu)
                    .Select(i => new { i.Plu, i.Name, i.OnHand, Cost = i.AverageCost > 0 ? i.AverageCost : i.LastCost }).ToListAsync();
                return Table("Items with no sales in period", period, [Col("Stock code"), Col("Description"), Col("On hand", ColumnKind.Quantity, true), Col("Value at cost", ColumnKind.Money, true)],
                    rows.Select(r => Row(r.Plu, r.Name, r.OnHand, Math.Round(r.OnHand * r.Cost, 2))));
            }
            case "clients":
            {
                var rows = await db.Clients.AsNoTracking().Where(c => !c.IsDeleted).OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
                    .Select(c => new { c.Number, c.LastName, c.FirstName, c.Company, c.City, c.WorkPhone, c.Email, c.LastPurchase }).ToListAsync();
                return Table("Client list", null, [Col("Number"), Col("Last name"), Col("First name"), Col("Company"), Col("City"), Col("Phone"), Col("E-mail"), Col("Last purchase", ColumnKind.Date)],
                    rows.Select(r => Row(r.Number, r.LastName, r.FirstName, r.Company, r.City, r.WorkPhone, r.Email, r.LastPurchase)));
            }
            case "balances":
            {
                var rows = await db.Clients.AsNoTracking().Where(c => !c.IsDeleted && c.Balance != 0).OrderByDescending(c => c.Balance)
                    .Select(c => new { c.Number, Name = c.Company ?? (c.LastName + ", " + c.FirstName), c.CreditLimit, c.Balance }).ToListAsync();
                return Table("Client balances", null, [Col("Number"), Col("Client"), Col("Credit limit", ColumnKind.Money), Col("Balance", ColumnKind.Money, true)],
                    rows.Select(r => Row(r.Number, r.Name, r.CreditLimit, r.Balance)));
            }
            case "aging":
            {
                var rows = await new InvoiceService(data).AgingAsync(DateOnly.FromDateTime(DateTime.Today));
                return Table("Aged receivables", $"As of {DateTime.Today:d}", [Col("Client"), Col("Current", ColumnKind.Money, true), Col("30 days", ColumnKind.Money, true), Col("60 days", ColumnKind.Money, true), Col("90+ days", ColumnKind.Money, true), Col("Total", ColumnKind.Money, true)],
                    rows.Select(r => Row(r.Name, r.Current, r.Days30, r.Days60, r.Days90Plus, r.Total)));
            }
            case "points":
            {
                var rows = await db.Clients.AsNoTracking().Where(c => !c.IsDeleted && c.Points != 0).OrderByDescending(c => c.Points)
                    .Select(c => new { c.Number, Name = c.Company ?? (c.LastName + ", " + c.FirstName), c.Points }).ToListAsync();
                return Table("Bonus points", null, [Col("Number"), Col("Client"), Col("Points", ColumnKind.Quantity, true)], rows.Select(r => Row(r.Number, r.Name, r.Points)));
            }
            case "suppliers":
            {
                var rows = await db.Suppliers.AsNoTracking().Where(c => !c.IsDeleted).OrderBy(c => c.Company)
                    .Select(c => new { c.Company, Contact = (c.FirstName + " " + c.LastName).Trim(), c.WorkPhone, c.Fax, c.Email, c.AccountNumber }).ToListAsync();
                return Table("Supplier list", null, [Col("Company"), Col("Contact"), Col("Phone"), Col("Fax"), Col("E-mail"), Col("Our account")],
                    rows.Select(r => Row(r.Company, r.Contact, r.WorkPhone, r.Fax, r.Email, r.AccountNumber)));
            }
            case "orders":
            {
                var rows = await db.PurchaseOrders.AsNoTracking().Where(p => p.LocationId == locationId && p.Status != PurchaseOrderStatus.Received).OrderBy(p => p.Number)
                    .Select(p => new { p.Number, p.CreatedAt, p.Status, p.Description, Lines = p.Lines.Count, Cost = p.Lines.Sum(l => l.OrderCost) }).ToListAsync();
                return Table("Open purchase orders", null, [Col("PO", ColumnKind.Count), Col("Created", ColumnKind.Date), Col("Status"), Col("Description"), Col("Lines", ColumnKind.Count, true), Col("Cost", ColumnKind.Money, true)],
                    rows.Select(r => Row(r.Number, r.CreatedAt, r.Status.ToString(), r.Description, r.Lines, r.Cost)));
            }
            case "drawer":
            {
                var rows = await db.DrawerSessions.AsNoTracking().Where(d => d.OpenedAt >= start && d.OpenedAt < end).OrderBy(d => d.OpenedAt).ToListAsync();
                return Table("End of day history", period, [Col("Station", ColumnKind.Count), Col("Opened", ColumnKind.Date), Col("Closed", ColumnKind.Date), Col("Customers", ColumnKind.Count, true), Col("Net sales", ColumnKind.Money, true), Col("Tax 1", ColumnKind.Money, true), Col("Tax 2", ColumnKind.Money, true), Col("Receipts", ColumnKind.Money, true)],
                    rows.Select(r => Row(r.StationNumber, r.OpenedAt, r.ClosedAt, r.CustomerCount, r.NetSales, r.Tax1Total, r.Tax2Total, r.TotalReceipts)));
            }
            default:
                throw new ArgumentException($"Unknown report {key}", nameof(key));
        }
    }

    private static ReportColumn Col(string header, ColumnKind kind = ColumnKind.Text, bool total = false) => new(header, kind, total);
    private static object?[] Row(params object?[] values) => values;

    private static ReportTable Table(string title, string? subtitle, List<ReportColumn> columns, IEnumerable<object?[]> rows) =>
        new() { Title = title, Subtitle = subtitle, Columns = columns, Rows = rows.ToList() };
}
