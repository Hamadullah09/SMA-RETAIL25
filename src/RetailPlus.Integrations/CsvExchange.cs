using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.Integrations;

/// <summary>Result of a CSV import.</summary>
public sealed record CsvImportResult(int Added, int Updated, int Skipped, IReadOnlyList<string> Problems);

/// <summary>
/// Tools / Import and Export: inventory and clients as CSV files, e.g. for a supplier's price file, a web shop or a
/// spreadsheet. Items are matched on the stock code, clients on the customer number.
/// </summary>
public sealed class CsvExchange(DataContext data)
{
    public static readonly string[] ItemColumns =
    [
        "PLU", "Description", "Department", "Category", "Brand", "Size", "Supplier", "SupplierCode", "Type", "OnHand", "LastCost", "AverageCost",
        "Price1", "Price2", "Price3", "Price4", "Break1", "Break2", "Break3", "Taxable1", "Taxable2", "BaseStock", "ReorderPoint", "ReorderQuantity", "BinLocation",
    ];

    public static readonly string[] ClientColumns =
    [
        "Number", "FirstName", "LastName", "Company", "Address1", "Address2", "City", "State", "Zip", "Country", "WorkPhone", "HomePhone", "Email",
        "AccountNumber", "Type", "Discount", "PriceLevel", "CreditLimit", "Birthday",
    ];

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async Task<string> ExportItemsAsync(int locationId)
    {
        await using var db = data.Create();
        var items = await db.Items.AsNoTracking().Include(i => i.Department).Include(i => i.Category).Include(i => i.Supplier)
            .Where(i => i.LocationId == locationId && !i.IsDeleted).OrderBy(i => i.Plu).ToListAsync();
        var sb = new StringBuilder().AppendLine(Csv.Line(ItemColumns));
        foreach (var i in items)
            sb.AppendLine(Csv.Line([
                i.Plu, i.Name, i.Department?.Name, i.Category?.Name, i.Brand, i.Size, i.Supplier?.Company, i.SupplierCode, i.ItemType.ToString(),
                N(i.OnHand), N(i.LastCost), N(i.AverageCost), N(i.Price1), N(i.Price2), N(i.Price3), N(i.Price4), N(i.BreakQuantity1), N(i.BreakQuantity2), N(i.BreakQuantity3),
                i.Taxable1 ? "Y" : "N", i.Taxable2 ? "Y" : "N", N(i.BaseStock), N(i.ReorderPoint), N(i.ReorderQuantity), i.BinLocation,
            ]));
        return sb.ToString();
    }

    public async Task<string> ExportClientsAsync()
    {
        await using var db = data.Create();
        var clients = await db.Clients.AsNoTracking().Where(c => !c.IsDeleted).OrderBy(c => c.LastName).ToListAsync();
        var sb = new StringBuilder().AppendLine(Csv.Line(ClientColumns));
        foreach (var c in clients)
            sb.AppendLine(Csv.Line([
                c.Number, c.FirstName, c.LastName, c.Company, c.Address1, c.Address2, c.City, c.State, c.Zip, c.Country, c.WorkPhone, c.HomePhone, c.Email,
                c.AccountNumber, c.Type, N(c.UsualDiscountPercent), c.PriceLevel.ToString(Inv), N(c.CreditLimit), c.Birthday?.ToString("yyyy-MM-dd", Inv),
            ]));
        return sb.ToString();
    }

    private static string N(decimal d) => d.ToString(Inv);

    /// <summary>
    /// Imports items. Only the columns present in the file are changed on existing items, so a price file with just
    /// PLU and Price1 updates prices and nothing else. New items need at least PLU and Description.
    /// </summary>
    public async Task<CsvImportResult> ImportItemsAsync(int locationId, string csv)
    {
        await using var db = data.Create();
        var depts = await db.Departments.ToDictionaryAsync(d => d.Name, StringComparer.OrdinalIgnoreCase);
        var cats = await db.Categories.ToDictionaryAsync(d => d.Name, StringComparer.OrdinalIgnoreCase);
        var suppliers = (await db.Suppliers.Where(s => !s.IsDeleted && s.Company != null).ToListAsync())
            .GroupBy(s => s.Company!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var existing = await db.Items.Where(i => i.LocationId == locationId && !i.IsDeleted).ToDictionaryAsync(i => i.Plu, StringComparer.OrdinalIgnoreCase);
        int added = 0, updated = 0, skipped = 0, rowNo = 1;
        var problems = new List<string>();
        foreach (var r in CsvRecord.Read(csv))
        {
            rowNo++;
            var plu = r["PLU"]?.ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(plu) || plu.Length > 16) { skipped++; problems.Add($"Row {rowNo}: missing or too long stock code."); continue; }
            bool isNew = !existing.TryGetValue(plu, out var item);
            if (isNew)
            {
                if (string.IsNullOrWhiteSpace(r["Description"])) { skipped++; problems.Add($"Row {rowNo}: new item {plu} has no description."); continue; }
                item = new Item { LocationId = locationId, Plu = plu, Taxable1 = true, Taxable2 = true };
                db.Items.Add(item);
                existing[plu] = item;
            }
            try
            {
                Apply(r, item!, depts, cats, suppliers, db);
            }
            catch (FormatException ex)
            {
                skipped++;
                problems.Add($"Row {rowNo} ({plu}): {ex.Message}");
                if (isNew) { db.Items.Remove(item!); existing.Remove(plu); }
                continue;
            }
            if (isNew) added++; else updated++;
        }
        await db.SaveChangesAsync();
        return new CsvImportResult(added, updated, skipped, problems);
    }

    private static void Apply(CsvRecord r, Item item, Dictionary<string, Department> depts, Dictionary<string, Category> cats,
        Dictionary<string, Supplier> suppliers, Data.RetailPlusDbContext db)
    {
        string? S(string name, int max) { var v = r[name]; return v is null ? null : v.Length > max ? v[..max] : v; }
        if (r.Has("Description")) item.Name = S("Description", 30) ?? "";
        if (r.Has("Brand")) item.Brand = S("Brand", 16);
        if (r.Has("Size")) item.Size = S("Size", 6);
        if (r.Has("SupplierCode")) item.SupplierCode = S("SupplierCode", 16);
        if (r.Has("BinLocation")) item.BinLocation = S("BinLocation", 25);
        if (r.Has("Type") && Enum.TryParse<ItemType>(r["Type"], true, out var type)) item.ItemType = type;
        if (r.Has("Department") && !string.IsNullOrWhiteSpace(r["Department"]))
        {
            var name = S("Department", 15)!.ToUpperInvariant();
            if (!depts.TryGetValue(name, out var d)) { d = new Department { Name = name }; db.Departments.Add(d); depts[name] = d; }
            item.Department = d;
        }
        if (r.Has("Category") && !string.IsNullOrWhiteSpace(r["Category"]))
        {
            var name = S("Category", 15)!.ToUpperInvariant();
            if (!cats.TryGetValue(name, out var c)) { c = new Category { Name = name }; db.Categories.Add(c); cats[name] = c; }
            item.Category = c;
        }
        if (r.Has("Supplier") && !string.IsNullOrWhiteSpace(r["Supplier"]) && suppliers.TryGetValue(r["Supplier"]!, out var s)) item.Supplier = s;
        Dec("OnHand", v => item.OnHand = v);
        Dec("LastCost", v => item.LastCost = v);
        Dec("AverageCost", v => item.AverageCost = v);
        Dec("Price1", v => item.Price1 = v);
        Dec("Price2", v => item.Price2 = v);
        Dec("Price3", v => item.Price3 = v);
        Dec("Price4", v => item.Price4 = v);
        Dec("Break1", v => item.BreakQuantity1 = v);
        Dec("Break2", v => item.BreakQuantity2 = v);
        Dec("Break3", v => item.BreakQuantity3 = v);
        Dec("BaseStock", v => item.BaseStock = v);
        Dec("ReorderPoint", v => item.ReorderPoint = v);
        Dec("ReorderQuantity", v => item.ReorderQuantity = v);
        if (r.Has("Taxable1")) item.Taxable1 = Bool(r["Taxable1"]);
        if (r.Has("Taxable2")) item.Taxable2 = Bool(r["Taxable2"]);
        var cost = item.AverageCost > 0 ? item.AverageCost : item.LastCost;
        item.MarginPercent = Money.MarginPercent(item.Price1, cost);

        void Dec(string name, Action<decimal> set)
        {
            var v = r[name];
            if (string.IsNullOrWhiteSpace(v)) return;
            if (!decimal.TryParse(v.Replace("$", "", StringComparison.Ordinal), NumberStyles.Number, Inv, out var d))
                throw new FormatException($"{name} '{v}' is not a number.");
            set(d);
        }
    }

    private static bool Bool(string? v) => v is not null && (v.Equals("Y", StringComparison.OrdinalIgnoreCase) || v.Equals("YES", StringComparison.OrdinalIgnoreCase)
        || v == "1" || v.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || v.Equals("T", StringComparison.OrdinalIgnoreCase));

    public async Task<CsvImportResult> ImportClientsAsync(string csv)
    {
        await using var db = data.Create();
        var byNumber = (await db.Clients.Where(c => !c.IsDeleted && c.Number != null).ToListAsync())
            .GroupBy(c => c.Number!).ToDictionary(g => g.Key, g => g.First());
        int added = 0, updated = 0, skipped = 0, rowNo = 1;
        var problems = new List<string>();
        foreach (var r in CsvRecord.Read(csv))
        {
            rowNo++;
            if (string.IsNullOrWhiteSpace(r["LastName"]) && string.IsNullOrWhiteSpace(r["Company"]) && string.IsNullOrWhiteSpace(r["FirstName"]))
            { skipped++; problems.Add($"Row {rowNo}: no name or company."); continue; }
            var number = r["Number"];
            Client client;
            bool isNew = string.IsNullOrWhiteSpace(number) || !byNumber.TryGetValue(number, out client!);
            if (isNew)
            {
                client = new Client
                {
                    Number = string.IsNullOrWhiteSpace(number)
                        ? (await db.NextNumberAsync(Data.RetailPlusDbContext.Sequences.CustomerNumbers)).ToString(Inv) : number,
                };
                db.Clients.Add(client);
                byNumber[client.Number!] = client;
            }
            else client = byNumber[number!];
            string? S(string name) => r.Has(name) ? r[name] : null;
            if (r.Has("FirstName")) client.FirstName = S("FirstName");
            if (r.Has("LastName")) client.LastName = S("LastName");
            if (r.Has("Company")) client.Company = S("Company");
            if (r.Has("Address1")) client.Address1 = S("Address1");
            if (r.Has("Address2")) client.Address2 = S("Address2");
            if (r.Has("City")) client.City = S("City");
            if (r.Has("State")) client.State = S("State");
            if (r.Has("Zip")) client.Zip = S("Zip");
            if (r.Has("Country")) client.Country = S("Country");
            if (r.Has("WorkPhone")) client.WorkPhone = S("WorkPhone");
            if (r.Has("HomePhone")) client.HomePhone = S("HomePhone");
            if (r.Has("Email")) client.Email = S("Email");
            if (r.Has("Type")) client.Type = S("Type");
            if (decimal.TryParse(r["Discount"], NumberStyles.Number, Inv, out var disc)) client.UsualDiscountPercent = disc;
            if (int.TryParse(r["PriceLevel"], NumberStyles.Integer, Inv, out var level)) client.PriceLevel = level;
            if (decimal.TryParse(r["CreditLimit"], NumberStyles.Number, Inv, out var limit)) client.CreditLimit = limit;
            if (DateOnly.TryParse(r["Birthday"], Inv, out var bd)) client.Birthday = bd;
            if (isNew) added++; else updated++;
        }
        await db.SaveChangesAsync();
        return new CsvImportResult(added, updated, skipped, problems);
    }
}
