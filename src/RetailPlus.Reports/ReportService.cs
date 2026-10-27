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
        new("countvar", "Inventory", "Stock count variances", true),
        new("transit", "Inventory", "Transfers in transit", false),
        new("giftcards", "Gift cards", "Gift card liability", false),
        new("loyalty", "Clients", "Loyalty points earned and redeemed", true),
    ];

    public async Task<ReportTable> RunAsync(string key, Guid locationId, DateOnly from, DateOnly to)
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
                var rows = await db.StockMovements.AsNoTracking().Where(a => a.At >= start && a.At < end && (a.Kind == StockMovementKind.Adjustment || a.Kind == StockMovementKind.Import))
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
            case "countvar":
            {
                var rows = await db.StockCountLines.AsNoTracking().Where(l => l.Variance != 0)
                    .Join(db.StockCounts.AsNoTracking().Where(c => c.Status == StockCountStatus.Posted && c.LocationId == locationId && c.PostedAt >= start && c.PostedAt < end),
                        l => l.StockCountId, c => c.Id, (l, c) => new { c.Number, c.PostedAt, l.Plu, l.Description, l.Expected, l.Counted, l.Variance, l.UnitCost, l.VarianceCost, l.NotCounted })
                    .ToListAsync();
                return Table("Stock count variances", period, [Col("Count", ColumnKind.Count), Col("Posted", ColumnKind.Date), Col("Stock code"), Col("Description"),
                        Col("Expected", ColumnKind.Quantity), Col("Counted", ColumnKind.Quantity), Col("Variance", ColumnKind.Quantity, true), Col("Unit cost", ColumnKind.Money),
                        Col("Variance value", ColumnKind.Money, true), Col("Note")],
                    rows.OrderBy(r => r.PostedAt).ThenBy(r => r.Plu, StringComparer.Ordinal)
                        .Select(r => Row(r.Number, r.PostedAt, r.Plu, r.Description, r.Expected, r.Counted, r.Variance, r.UnitCost, r.VarianceCost, r.NotCounted ? "Not counted, set to zero" : null)));
            }
            case "transit":
            {
                var transfers = db.StockTransfers.AsNoTracking().Where(t => t.Status == StockTransferStatus.Shipped);
                if (data.Station is { } here) transfers = transfers.Where(StockTransfer.OfStore(here.StoreId));
                var rows = await db.StockTransferLines.AsNoTracking()
                    .Join(transfers, l => l.StockTransferId, t => t.Id, (l, t) => new
                    {
                        t.Number, t.ShippedAt, From = t.FromLocation!.Code, FromStore = t.StoreId, To = t.ToLocation!.Code, ToStore = t.ToStoreId,
                        l.LineNumber, l.Plu, l.Description, l.QuantityShipped, l.UnitCost,
                    })
                    .ToListAsync();
                var codes = await db.Stores.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Code);
                return Table("Transfers in transit", "Shipped and not received yet", [Col("Transfer", ColumnKind.Count), Col("Shipped", ColumnKind.Date), Col("From"), Col("To"),
                        Col("Stock code"), Col("Description"), Col("Quantity", ColumnKind.Quantity, true), Col("Value at cost", ColumnKind.Money, true)],
                    rows.OrderBy(r => r.ShippedAt).ThenBy(r => r.Number).ThenBy(r => r.LineNumber)
                        .Select(r => Row(r.Number, r.ShippedAt, $"{codes.GetValueOrDefault(r.FromStore)} {r.From}", $"{codes.GetValueOrDefault(r.ToStore)} {r.To}", r.Plu, r.Description,
                            r.QuantityShipped, Math.Round(r.QuantityShipped * r.UnitCost, 2))));
            }
            case "giftcards":
            {
                var cards = await db.GiftCards.AsNoTracking()
                    .Select(c => new { c.Id, c.Number, c.Kind, c.IssuedAt, c.ExpiresOn, c.IsActive, Client = c.Client == null ? null : c.Client.Company ?? (c.Client.LastName + ", " + c.Client.FirstName) })
                    .ToListAsync();
                var balances = (await db.GiftCardTransactions.AsNoTracking().Select(t => new { t.GiftCardId, t.Amount }).ToListAsync())
                    .GroupBy(t => t.GiftCardId).ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
                return Table("Gift card liability", $"Balances on gift cards and certificates as of {DateTime.Now:g}", [Col("Number"), Col("Kind"), Col("Issued", ColumnKind.Date),
                        Col("Expires", ColumnKind.Date), Col("Active"), Col("Client"), Col("Balance", ColumnKind.Money, true)],
                    cards.Select(c => (Card: c, Balance: balances.GetValueOrDefault(c.Id))).Where(x => x.Balance != 0).OrderBy(x => x.Card.IssuedAt)
                        .Select(x => Row(x.Card.Number, x.Card.Kind.ToString(), x.Card.IssuedAt, x.Card.ExpiresOn, x.Card.IsActive, x.Card.Client, x.Balance)));
            }
            case "loyalty":
            {
                var moves = await db.LoyaltyTransactions.AsNoTracking().Where(t => t.At < end).Select(t => new { t.ClientId, t.Kind, t.Points, t.At }).ToListAsync();
                var clients = await db.Clients.AsNoTracking().Select(c => new { c.Id, c.Number, Name = c.Company ?? (c.LastName + ", " + c.FirstName) }).ToDictionaryAsync(c => c.Id);
                var rows = moves.GroupBy(m => m.ClientId).Select(g =>
                {
                    var inPeriod = g.Where(m => m.At >= start).ToList();
                    return new
                    {
                        Client = clients.GetValueOrDefault(g.Key), Opening = g.Where(m => m.At < start).Sum(m => m.Points),
                        Earned = inPeriod.Where(m => m.Kind == LoyaltyTransactionKind.Earn).Sum(m => m.Points),
                        Redeemed = -inPeriod.Where(m => m.Kind == LoyaltyTransactionKind.Redeem).Sum(m => m.Points),
                        Adjusted = inPeriod.Where(m => m.Kind is LoyaltyTransactionKind.Adjust or LoyaltyTransactionKind.Opening).Sum(m => m.Points),
                        Closing = g.Sum(m => m.Points), Active = inPeriod.Count > 0,
                    };
                }).Where(r => r.Active || r.Closing != 0).OrderByDescending(r => r.Closing).ToList();
                return Table("Loyalty points earned and redeemed", period, [Col("Number"), Col("Client"), Col("Opening", ColumnKind.Quantity, true), Col("Earned", ColumnKind.Quantity, true),
                        Col("Redeemed", ColumnKind.Quantity, true), Col("Adjusted", ColumnKind.Quantity, true), Col("Closing", ColumnKind.Quantity, true)],
                    rows.Select(r => Row(r.Client?.Number, r.Client?.Name, r.Opening, r.Earned, r.Redeemed, r.Adjusted, r.Closing)));
            }
            default:
                throw new ArgumentException($"Unknown report {key}", nameof(key));
        }
    }

    /// <summary>
    /// The count sheet of a stock count: every stocked item of the location in bin and stock code order, what was counted
    /// so far, and an empty column to write the count in. <paramref name="showOnHand"/> adds the expected stock (leave it
    /// out for a blind count).
    /// </summary>
    public async Task<ReportTable> CountSheetAsync(Guid countId, bool showOnHand)
    {
        await using var db = data.Create();
        var count = await db.StockCounts.AsNoTracking().Include(c => c.Location).SingleAsync(c => c.Id == countId);
        var counted = (await db.StockCountLines.AsNoTracking().Where(l => l.StockCountId == countId).Select(l => new { l.ItemId, l.Counted }).ToListAsync())
            .GroupBy(l => l.ItemId).ToDictionary(g => g.Key, g => g.Sum(l => l.Counted));
        var items = await db.Items.AsNoTracking()
            .Where(i => i.LocationId == count.LocationId && !i.IsDeleted
                        && (i.ItemType == ItemType.Standard || i.ItemType == ItemType.Matrix || i.ItemType == ItemType.Serialized || i.ItemType == ItemType.Kit))
            .Select(i => new { i.Id, i.BinLocation, i.Plu, i.Barcode, i.Name, i.Size, i.OnHand }).ToListAsync();
        var columns = new List<ReportColumn> { Col("Bin"), Col("Stock code"), Col("Barcode"), Col("Description"), Col("Size") };
        if (showOnHand) columns.Add(Col("On hand", ColumnKind.Quantity));
        columns.Add(Col("Counted so far", ColumnKind.Quantity));
        columns.Add(Col("Count"));
        return Table($"Count sheet {count.Number}", $"Location {count.Location?.Code} · started {count.StartedAt:g}" + (string.IsNullOrWhiteSpace(count.Note) ? "" : " · " + count.Note), columns,
            items.OrderBy(i => i.BinLocation ?? "", StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Plu, StringComparer.OrdinalIgnoreCase)
                .Select(i =>
                {
                    var cells = new List<object?> { i.BinLocation, i.Plu, i.Barcode, i.Name, i.Size };
                    if (showOnHand) cells.Add(i.OnHand);
                    cells.Add(counted.TryGetValue(i.Id, out var c) ? c : null);
                    cells.Add("");
                    return cells.ToArray();
                }));
    }

    /// <summary>The lines of a stock count with their variances and the value of each (only the differences when asked).</summary>
    public async Task<ReportTable> CountVarianceAsync(Guid countId, bool differencesOnly)
    {
        await using var db = data.Create();
        var count = await db.StockCounts.AsNoTracking().Include(c => c.Location).SingleAsync(c => c.Id == countId);
        var lines = await db.StockCountLines.AsNoTracking().Where(l => l.StockCountId == countId && (!differencesOnly || l.Variance != 0)).ToListAsync();
        return Table($"Stock count {count.Number} variances", $"Location {count.Location?.Code} · {count.Status} · started {count.StartedAt:g}",
            [Col("Stock code"), Col("Description"), Col("Expected", ColumnKind.Quantity), Col("Counted", ColumnKind.Quantity), Col("Variance", ColumnKind.Quantity, true),
                Col("Unit cost", ColumnKind.Money), Col("Variance value", ColumnKind.Money, true)],
            lines.OrderBy(l => l.Plu, StringComparer.OrdinalIgnoreCase).Select(l => Row(l.Plu, l.Description, l.Expected, l.Counted, l.Variance, l.UnitCost, l.VarianceCost)));
    }

    private static ReportColumn Col(string header, ColumnKind kind = ColumnKind.Text, bool total = false) => new(header, kind, total);
    private static object?[] Row(params object?[] values) => values;

    private static ReportTable Table(string title, string? subtitle, List<ReportColumn> columns, IEnumerable<object?[]> rows) =>
        new() { Title = title, Subtitle = subtitle, Columns = columns, Rows = rows.ToList() };
}
