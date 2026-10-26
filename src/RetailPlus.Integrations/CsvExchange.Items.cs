using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Integrations;

/// <summary>
/// The inventory file, in Retail Plus's INVENTORY layout with the RFID columns: EPC (one tagged piece per row), PWEIGHT
/// and TOLRANCE (a number and its unit: "90 GRAM", "5 GRAM"), SSTATUS (0 in stock, 1 sold) and RECEIPT# (the receipt it
/// was sold on); then BARCODE, BRAND and SUPPCODE. An item with three tagged pieces is three rows with the same PLU; an
/// item without tags is one row with no EPC.
/// </summary>
public sealed partial class CsvExchange
{
    private static readonly string[] Months = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

    public static readonly string[] ItemColumns =
    [
        "PROD", "PLU", "EPC", "DEPT", "BSIZE", "PWEIGHT", "TOLRANCE", "PKG", "LASTCOST", "ITEMTYPE", "CATEGORY", "AVGCOST",
        "UNITPRICE1", "UNITPRICE2", "UNITPRICE3", "UNITPRICE4", "PRICEBRK1", "PRICEBRK2", "PRICEBRK3", "MARGIN", "YTDTOTAL", "YTDGROSS",
        "SALESTART", "SALEEND", "SALEPRICE", "LASTYR_TOT", "BASTOCK", "ONHAND", "ONORDER", "ORDATE", "LASTSHIP", "RECVD", "B4YREND",
        "BACKORD", "SUPPLIER", "ROP", "ROQ", "SUBCODE", "TAGALONG", "PARENT", "BINLOC", "PICPATH", "LASTUPD", "SHIPWEIGHT", "COMMISSION",
        "MAXCOMM", "COMMTYPE", .. Months.Select(m => m + "_GROSS"), .. Months.Select(m => m + "_VOLUME"), "TX1", "TX2", "PRTFLAG", "LASTYR",
        "WEEKLY", "BONUSBUY", "TWOFORONE", "BONUSGET", "SALESMSG", "INVOICEMSG", "NOTES", "KIT", "MATRIX", "SUPLIST", "SERNO", "SSTATUS",
        "RECEIPT#", "BARCODE", "BRAND", "SUPPCODE",
    ];

    /// <summary>Rows of a template whose stock code starts with this are examples and are never imported.</summary>
    public const string ExamplePrefix = "EXAMPLE";

    /// <summary>
    /// The inventory CSV template: every column, example rows (skipped by the import) for a tagged item of two pieces and
    /// an item with a barcode and no tags, and a notes row (no PLU, so not imported) saying what goes in the new columns.
    /// </summary>
    public static string ItemTemplate()
    {
        var sb = new StringBuilder().AppendLine(Csv.Line(ItemColumns));
        var chocolate = Row(new()
        {
            ["PROD"] = "CADBURY DAIRY MILK 90G", ["PLU"] = ExamplePrefix + "-1", ["DEPT"] = "CONFECTIONERY", ["PWEIGHT"] = "90 GRAM", ["TOLRANCE"] = "2 GRAM",
            ["PKG"] = "1", ["LASTCOST"] = "369", ["ITEMTYPE"] = "STANDARD", ["CATEGORY"] = "CHOCOLATE", ["AVGCOST"] = "369", ["UNITPRICE1"] = "450",
            ["BASTOCK"] = "2", ["ONHAND"] = "2", ["SUPPLIER"] = "Al-Noor Distributors", ["ROP"] = "3", ["ROQ"] = "24", ["BINLOC"] = "AISLE 1 A",
            ["TX1"] = "T", ["TX2"] = "F", ["PRTFLAG"] = "F", ["BRAND"] = "Cadbury",
        });
        sb.AppendLine(Csv.Line(ItemColumns.Select(c => c == "EPC" ? "E28011700000000000000001" : c == "SSTATUS" ? "0" : chocolate.GetValueOrDefault(c))));
        sb.AppendLine(Csv.Line(ItemColumns.Select(c => c == "EPC" ? "E28011700000000000000002" : c == "SSTATUS" ? "1" : c == "RECEIPT#" ? "1005" : chocolate.GetValueOrDefault(c))));
        var cola = Row(new()
        {
            ["PROD"] = "COCA-COLA 1.5L", ["PLU"] = ExamplePrefix + "-2", ["DEPT"] = "BEVERAGE", ["PWEIGHT"] = "1.5 L", ["PKG"] = "1", ["LASTCOST"] = "180",
            ["ITEMTYPE"] = "STANDARD", ["CATEGORY"] = "SOFT DRINKS", ["AVGCOST"] = "180", ["UNITPRICE1"] = "220", ["BASTOCK"] = "6", ["ONHAND"] = "6",
            ["ROP"] = "2", ["ROQ"] = "12", ["BINLOC"] = "CHILLER 1", ["TX1"] = "T", ["TX2"] = "F", ["PRTFLAG"] = "F", ["BARCODE"] = Upc.InStore(99_999),
            ["BRAND"] = "Coca-Cola",
        });
        sb.AppendLine(Csv.Line(ItemColumns.Select(c => cola.GetValueOrDefault(c))));
        sb.AppendLine();
        sb.AppendLine(Csv.Line(ItemColumns.Select(c => c switch
        {
            "EPC" => "24 hexadecimal characters; one row per tagged piece",
            "PWEIGHT" => "90 GRAM / 500 MILIGRAM / 1.5 KG",
            "TOLRANCE" => "2 GRAM / 500 MILIGRAM",
            "SSTATUS" => "0 = in stock (default), 1 = sold",
            "RECEIPT#" => "sales receipt it was sold on",
            "BARCODE" => "UPC-A (12 digits) or EAN-13",
            _ => null,
        })));
        return sb.ToString();

        static Dictionary<string, string?> Row(Dictionary<string, string?> values) => new(values, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<string> ExportItemsAsync(int locationId)
    {
        await using var db = data.Create();
        var items = await db.Items.AsNoTracking().Include(i => i.Department).Include(i => i.Category).Include(i => i.Supplier)
            .Where(i => i.LocationId == locationId && !i.IsDeleted).OrderBy(i => i.Plu).ToListAsync();
        var ids = items.Select(i => i.Id).ToList();
        var tags = (await db.ItemTags.AsNoTracking().Where(t => ids.Contains(t.ItemId)).OrderBy(t => t.SerialNumber).ThenBy(t => t.Epc).ToListAsync())
            .ToLookup(t => t.ItemId);
        int year = DateTime.Today.Year;
        var sales = (await db.ItemPeriodSales.AsNoTracking()
            .Where(p => ids.Contains(p.ItemId) && p.Kind == SalesPeriodKind.Month && (p.Year == year || p.Year == year - 1)).ToListAsync())
            .ToLookup(p => p.ItemId);
        var sb = new StringBuilder().AppendLine(Csv.Line(ItemColumns));
        foreach (var i in items)
        {
            var values = ItemValues(i, sales[i.Id].ToList(), year);
            var pieces = tags[i.Id].ToList();
            if (pieces.Count == 0) sb.AppendLine(Csv.Line(ItemColumns.Select(c => values.GetValueOrDefault(c))));
            foreach (var t in pieces)
            {
                values["EPC"] = t.Epc;
                values["SSTATUS"] = t.Status == ItemTagStatus.Sold ? "1" : "0";
                values["RECEIPT#"] = t.SoldOnInvoice?.ToString(Inv);
                sb.AppendLine(Csv.Line(ItemColumns.Select(c => values.GetValueOrDefault(c))));
            }
        }
        return sb.ToString();
    }

    private static Dictionary<string, string?> ItemValues(Item i, List<ItemPeriodSales> sales, int year)
    {
        var thisYear = sales.Where(p => p.Year == year).ToList();
        var v = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["PROD"] = i.Name, ["PLU"] = i.Plu, ["DEPT"] = i.Department?.Name, ["BSIZE"] = i.Size,
            ["PWEIGHT"] = Weights.Format(i.Weight, i.WeightUnit), ["TOLRANCE"] = Weights.Format(i.WeightTolerance, i.WeightToleranceUnit),
            ["PKG"] = i.PackQuantity.ToString(Inv), ["LASTCOST"] = N(i.LastCost), ["ITEMTYPE"] = TypeWord(i.ItemType), ["CATEGORY"] = i.Category?.Name,
            ["AVGCOST"] = N(i.AverageCost), ["UNITPRICE1"] = N(i.Price1), ["UNITPRICE2"] = N(i.Price2), ["UNITPRICE3"] = N(i.Price3),
            ["UNITPRICE4"] = N(i.Price4), ["PRICEBRK1"] = N(i.BreakQuantity1), ["PRICEBRK2"] = N(i.BreakQuantity2), ["PRICEBRK3"] = N(i.BreakQuantity3),
            ["MARGIN"] = N(i.MarginPercent), ["YTDTOTAL"] = N(thisYear.Sum(p => p.Units)), ["YTDGROSS"] = N(thisYear.Sum(p => p.Gross)),
            ["SALESTART"] = D(i.SaleStart), ["SALEEND"] = D(i.SaleEnd), ["SALEPRICE"] = N(i.SalePrice),
            ["LASTYR_TOT"] = N(sales.Where(p => p.Year == year - 1).Sum(p => p.Units)), ["BASTOCK"] = N(i.BaseStock), ["ONHAND"] = N(i.OnHand),
            ["ONORDER"] = N(i.OnOrder), ["ORDATE"] = D(i.LastOrderDate), ["LASTSHIP"] = D(i.LastReceivedDate), ["RECVD"] = N(i.LastReceivedQuantity),
            ["B4YREND"] = N(i.OnHandBeforeYearEnd), ["BACKORD"] = N(i.CustomerOrdered), ["SUPPLIER"] = i.Supplier?.Company, ["ROP"] = N(i.ReorderPoint),
            ["ROQ"] = N(i.ReorderQuantity), ["SUBCODE"] = i.SubstituteCode, ["TAGALONG"] = i.TagAlongCode, ["PARENT"] = i.ParentCode,
            ["BINLOC"] = i.BinLocation, ["PICPATH"] = i.PhotoPath, ["LASTUPD"] = D(i.LastUpdated), ["SHIPWEIGHT"] = N(i.ShipWeight),
            ["COMMISSION"] = N(i.Commission), ["MAXCOMM"] = N(i.MaxCommission), ["COMMTYPE"] = i.CommissionType,
            ["TX1"] = TF(i.Taxable1), ["TX2"] = TF(i.Taxable2), ["PRTFLAG"] = TF(i.IsFlagged), ["BONUSBUY"] = i.BonusBuyQuantity.ToString(Inv),
            ["TWOFORONE"] = TF(i.BonusPricing), ["BONUSGET"] = i.BonusGetQuantity.ToString(Inv), ["SALESMSG"] = i.PosMessage,
            ["INVOICEMSG"] = i.InvoiceMessage, ["NOTES"] = i.Notes, ["BARCODE"] = i.Barcode, ["BRAND"] = i.Brand, ["SUPPCODE"] = i.SupplierCode,
        };
        for (int m = 0; m < 12; m++)
        {
            var month = thisYear.Where(p => p.Period == m + 1).ToList();
            v[Months[m] + "_GROSS"] = N(month.Sum(p => p.Gross));
            v[Months[m] + "_VOLUME"] = N(month.Sum(p => p.Units));
        }
        return v;
    }

    private static string N(decimal d) => d.ToString("0.####", Inv);
    private static string? D(DateOnly? d) => d?.ToString("M/d/yyyy", Inv);
    private static string TF(bool b) => b ? "T" : "F";

    private static string TypeWord(ItemType type) => type switch
    {
        ItemType.GiftCard => "GIFT CARD",
        ItemType.NonStock => "NON-STOCK",
        _ => type.ToString().ToUpperInvariant(),
    };

    /// <summary>The item type from Retail Plus's words (STANDARD, MATRIX, SERIAL, KIT, GIFT CARD, NON-STOCK, SERVICE…).</summary>
    private static ItemType? ParseType(string? text) =>
        text?.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).ToUpperInvariant() switch
        {
            null or "" or "STANDARD" or "STOCK" or "REGULAR" => ItemType.Standard,
            "MATRIX" => ItemType.Matrix,
            "SERIAL" or "SERIALIZED" or "SERIALISED" or "SERIALNO" or "SERIALNUMBER" => ItemType.Serialized,
            "KIT" => ItemType.Kit,
            "GIFTCARD" or "GIFTCERT" or "GIFTCERTIFICATE" => ItemType.GiftCard,
            "NONSTOCK" or "NONINVENTORY" => ItemType.NonStock,
            "RENTAL" => ItemType.Rental,
            "SERVICE" => ItemType.Service,
            "SHIPPING" => ItemType.Shipping,
            "ADMISSION" or "TICKET" => ItemType.Admission,
            _ => null,
        };

    /// <summary>
    /// Imports items, in this layout or with the short names of earlier files (Description, Price1…). Only the columns
    /// present are changed on existing items, so a file with just PLU and UNITPRICE1 changes prices. The first row of a
    /// PLU sets the item; every row's EPC registers one tagged piece with its SSTATUS and RECEIPT#. New items need PLU and
    /// PROD. Template example rows (stock code EXAMPLE…) and rows without a PLU (notes under the table) are skipped.
    /// </summary>
    public async Task<CsvImportResult> ImportItemsAsync(int locationId, string csv)
    {
        var rows = CsvRecord.Read(csv).ToList();
        await using var db = data.Create();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var lookups = new Lookups(
            await db.Departments.ToDictionaryAsync(d => d.Name, StringComparer.OrdinalIgnoreCase),
            await db.Categories.ToDictionaryAsync(d => d.Name, StringComparer.OrdinalIgnoreCase),
            (await db.Suppliers.Where(s => !s.IsDeleted && s.Company != null).ToListAsync())
                .GroupBy(s => s.Company!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase),
            db);
        var existing = await db.Items.Where(i => i.LocationId == locationId && !i.IsDeleted).ToDictionaryAsync(i => i.Plu, StringComparer.OrdinalIgnoreCase);
        var barcodes = existing.Values.Where(i => i.Barcode != null).GroupBy(i => i.Barcode!).ToDictionary(g => g.Key, g => g.First());
        var fileEpcs = rows.Select(r => Epc.Normalize(Get(r, "EPC"))).OfType<string>().Distinct().ToList();
        var knownTags = await db.ItemTags.Where(t => fileEpcs.Contains(t.Epc)).ToDictionaryAsync(t => t.Epc);
        var firstRow = new Dictionary<string, (int Row, string Name)>(StringComparer.OrdinalIgnoreCase);
        var pieces = new List<(Item Item, string Epc, ItemTagStatus Status, long? Receipt)>();
        var piecesInFile = new HashSet<string>();
        int added = 0, updated = 0, skipped = 0, rowNo = 1;
        var problems = new List<string>();
        foreach (var r in rows)
        {
            rowNo++;
            var plu = Get(r, "PLU")?.ToUpperInvariant();
            var name = Get(r, "PROD", "Description");
            if (string.IsNullOrWhiteSpace(plu))
            {
                if (!string.IsNullOrWhiteSpace(name)) { skipped++; problems.Add($"Row {rowNo}: {name} has no stock code (PLU)."); }
                continue;   // blank rows and notes under the table
            }
            if (plu.Length > 16) { skipped++; problems.Add($"Row {rowNo}: stock code {plu} is longer than 16 characters."); continue; }
            if (plu.StartsWith(ExamplePrefix, StringComparison.Ordinal)) { skipped++; problems.Add($"Row {rowNo}: example row {plu} skipped (replace it with your own item)."); continue; }

            Item item;
            if (firstRow.TryGetValue(plu, out var first))
            {
                // Another piece of an item already read from the file: only its tag counts.
                if (!string.IsNullOrWhiteSpace(name) && !string.Equals(name.Trim(), first.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    problems.Add($"Row {rowNo}: stock code {plu} is {first.Name} (row {first.Row}), not {name}; this row was skipped.");
                    continue;
                }
                item = existing[plu];
            }
            else
            {
                bool isNew = !existing.TryGetValue(plu, out var found);
                if (isNew && string.IsNullOrWhiteSpace(name)) { skipped++; problems.Add($"Row {rowNo}: new item {plu} has no description (PROD)."); continue; }
                item = found ?? new Item { LocationId = locationId, Plu = plu, Taxable1 = true, Taxable2 = true };
                if (isNew)
                {
                    db.Items.Add(item);
                    existing[plu] = item;
                }
                try
                {
                    Apply(r, item, lookups, barcodes);
                }
                catch (FormatException ex)
                {
                    skipped++;
                    problems.Add($"Row {rowNo} ({plu}): {ex.Message}");
                    if (isNew) { db.Items.Remove(item); existing.Remove(plu); }
                    continue;
                }
                firstRow[plu] = (rowNo, item.Name);
                if (isNew) added++; else updated++;
            }

            // The row's tagged piece.
            var epcText = Get(r, "EPC");
            if (string.IsNullOrWhiteSpace(epcText)) continue;
            var epc = Epc.Normalize(epcText);
            if (epc is null) { problems.Add($"Row {rowNo} ({plu}): EPC {epcText} is not a tag code (hexadecimal, 24 characters)."); continue; }
            if (!piecesInFile.Add(epc)) { problems.Add($"Row {rowNo} ({plu}): EPC {epc} is in the file twice; the first row was kept."); continue; }
            if (knownTags.TryGetValue(epc, out var known) && known.ItemId != item.Id)
            {
                var owner = await db.Items.Where(i => i.Id == known.ItemId).Select(i => i.Plu + " " + i.Name).FirstOrDefaultAsync();
                problems.Add($"Row {rowNo} ({plu}): EPC {epc} already belongs to {owner}; it was not moved.");
                continue;
            }
            var status = Get(r, "SSTATUS")?.Trim() == "1" ? ItemTagStatus.Sold : ItemTagStatus.InStock;
            long? receipt = long.TryParse(Get(r, "RECEIPT#", "RECEIPT"), NumberStyles.Integer, Inv, out var number) ? number : null;
            pieces.Add((item, epc, status, receipt));
        }

        await db.SaveChangesAsync();   // new items get their ids before their tags are added
        foreach (var (item, epc, status, receipt) in pieces)
        {
            if (!knownTags.TryGetValue(epc, out var tag))
            {
                tag = new ItemTag { Epc = epc, ItemId = item.Id, RegisteredAt = DateTime.Now };
                db.ItemTags.Add(tag);
            }
            tag.Status = status;
            tag.SoldOnInvoice = status == ItemTagStatus.Sold ? receipt : null;
            if (status == ItemTagStatus.InStock) tag.SoldAt = null;
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return new CsvImportResult(added, updated, skipped, problems, pieces.Count);
    }

    /// <summary>The value of the first of the columns the file has (a column may go by several names), or null.</summary>
    private static string? Get(CsvRecord r, params string[] names) => names.FirstOrDefault(r.Has) is { } name ? r[name] : null;

    private sealed record Lookups(Dictionary<string, Department> Departments, Dictionary<string, Category> Categories,
        Dictionary<string, Supplier> Suppliers, Data.RetailPlusDbContext Db);

    private static void Apply(CsvRecord r, Item item, Lookups lookups, Dictionary<string, Item> barcodes)
    {
        bool Has(params string[] names) => names.Any(r.Has);
        void Text(Action<string?> set, int max, params string[] names)
        {
            if (!Has(names)) return;
            var v = Get(r, names);
            set(string.IsNullOrWhiteSpace(v) ? null : v.Length > max ? v[..max] : v);
        }
        void Dec(Action<decimal> set, params string[] names)
        {
            var v = Get(r, names);
            if (string.IsNullOrWhiteSpace(v)) return;
            if (!decimal.TryParse(v.Replace("$", "", StringComparison.Ordinal), NumberStyles.Number, Inv, out var d))
                throw new FormatException($"{names[0]} '{v}' is not a number.");
            set(d);
        }
        void Int(Action<int> set, params string[] names) => Dec(d => set((int)Math.Round(d)), names);
        void Flag(Action<bool> set, params string[] names)
        {
            var v = Get(r, names);
            if (!string.IsNullOrWhiteSpace(v)) set(Bool(v));
        }
        void Date(Action<DateOnly?> set, params string[] names)
        {
            if (!Has(names)) return;
            var v = Get(r, names);
            if (string.IsNullOrWhiteSpace(v)) { set(null); return; }
            if (!DateOnly.TryParseExact(v.Trim(), ["M/d/yyyy", "M/d/yy", "yyyy-MM-dd", "d-MMM-yyyy"], Inv, DateTimeStyles.None, out var d))
                throw new FormatException($"{names[0]} '{v}' is not a date (month/day/year).");
            set(d);
        }
        void Weight(string name, string unitColumn, Action<decimal, string?> set, string fallbackUnit)
        {
            var v = Get(r, name, unitColumn == "WeightUnit" ? "Weight" : "Tolerance");
            if (string.IsNullOrWhiteSpace(v)) return;
            if (!Weights.TryParse(v, out var value, out var unit))
                throw new FormatException($"{name} '{v}' is not a weight (for example 90 GRAM, 500 MILIGRAM or 1.5 KG).");
            var given = Get(r, unitColumn);
            if (unit is null && !string.IsNullOrWhiteSpace(given))
                unit = Weights.Normalize(given) ?? throw new FormatException($"{unitColumn} '{given}' is not a unit (mg, g, kg, ml, L).");
            set(value, unit ?? fallbackUnit);
        }

        if (Has("BARCODE"))
        {
            var code = Get(r, "BARCODE")?.Trim();
            if (string.IsNullOrEmpty(code)) code = null;
            else if (!Upc.IsValid(code)) throw new FormatException($"Barcode {code} is not valid (12 digits for UPC-A, 13 or 8, ending in the check digit).");
            else if (Upc.Forms(code).Select(f => barcodes.GetValueOrDefault(f)).FirstOrDefault(o => o is not null && o != item) is { } other)
                throw new FormatException($"Barcode {code} is already on {other.Plu}.");
            if (item.Barcode is not null) barcodes.Remove(item.Barcode);
            item.Barcode = code;
            if (code is not null) barcodes[code] = item;
        }
        if (Get(r, "PROD", "Description") is { } name && !string.IsNullOrWhiteSpace(name)) item.Name = name.Length > 30 ? name[..30] : name;
        if (Has("ITEMTYPE", "Type") && Get(r, "ITEMTYPE", "Type") is { } typeText && typeText.Length > 0)
            item.ItemType = ParseType(typeText) ?? throw new FormatException($"ITEMTYPE '{typeText}' is not known (STANDARD, MATRIX, SERIAL, KIT, SERVICE…).");
        if (Get(r, "DEPT", "Department") is { } dept && !string.IsNullOrWhiteSpace(dept))
        {
            var key = (dept.Length > 15 ? dept[..15] : dept).ToUpperInvariant();
            if (!lookups.Departments.TryGetValue(key, out var d)) { d = new Department { Name = key }; lookups.Db.Departments.Add(d); lookups.Departments[key] = d; }
            item.Department = d;
        }
        if (Get(r, "CATEGORY", "Category") is { } cat && !string.IsNullOrWhiteSpace(cat))
        {
            var key = (cat.Length > 15 ? cat[..15] : cat).ToUpperInvariant();
            if (!lookups.Categories.TryGetValue(key, out var c)) { c = new Category { Name = key }; lookups.Db.Categories.Add(c); lookups.Categories[key] = c; }
            item.Category = c;
        }
        if (Get(r, "SUPPLIER", "Supplier") is { } supplier && !string.IsNullOrWhiteSpace(supplier))
        {
            var key = supplier.Length > 30 ? supplier[..30] : supplier;
            if (!lookups.Suppliers.TryGetValue(key, out var s)) { s = new Supplier { Company = key }; lookups.Db.Suppliers.Add(s); lookups.Suppliers[key] = s; }
            item.Supplier = s;
        }
        Text(v => item.Size = v, 6, "BSIZE", "Size");
        Text(v => item.Brand = v, 16, "BRAND", "Brand");
        Text(v => item.SupplierCode = v, 16, "SUPPCODE", "SupplierCode");
        Text(v => item.BinLocation = v, 25, "BINLOC", "BinLocation");
        Text(v => item.SubstituteCode = v, 16, "SUBCODE");
        Text(v => item.TagAlongCode = v, 16, "TAGALONG");
        Text(v => item.ParentCode = v, 16, "PARENT");
        Text(v => item.PhotoPath = v, 260, "PICPATH");
        Text(v => item.CommissionType = v, 20, "COMMTYPE");
        Text(v => item.PosMessage = v, 4000, "SALESMSG");
        Text(v => item.InvoiceMessage = v, 4000, "INVOICEMSG");
        Text(v => item.Notes = v, 4000, "NOTES");
        Weight("PWEIGHT", "WeightUnit", (value, unit) => { item.Weight = value; item.WeightUnit = unit; }, item.WeightUnit ?? "g");
        Weight("TOLRANCE", "ToleranceUnit", (value, unit) => { item.WeightTolerance = value; item.WeightToleranceUnit = unit; }, item.WeightToleranceUnit ?? "g");
        Int(v => item.PackQuantity = v, "PKG");
        Dec(v => item.LastCost = v, "LASTCOST", "LastCost");
        Dec(v => item.AverageCost = v, "AVGCOST", "AverageCost");
        Dec(v => item.Price1 = v, "UNITPRICE1", "Price1");
        Dec(v => item.Price2 = v, "UNITPRICE2", "Price2");
        Dec(v => item.Price3 = v, "UNITPRICE3", "Price3");
        Dec(v => item.Price4 = v, "UNITPRICE4", "Price4");
        Dec(v => item.BreakQuantity1 = v, "PRICEBRK1", "Break1");
        Dec(v => item.BreakQuantity2 = v, "PRICEBRK2", "Break2");
        Dec(v => item.BreakQuantity3 = v, "PRICEBRK3", "Break3");
        Dec(v => item.SalePrice = v, "SALEPRICE");
        Dec(v => item.BaseStock = v, "BASTOCK", "BaseStock");
        Dec(v => item.OnHand = v, "ONHAND", "OnHand");
        Dec(v => item.OnOrder = v, "ONORDER");
        Dec(v => item.LastReceivedQuantity = v, "RECVD");
        Dec(v => item.OnHandBeforeYearEnd = v, "B4YREND");
        Dec(v => item.CustomerOrdered = v, "BACKORD");
        Dec(v => item.ReorderPoint = v, "ROP", "ReorderPoint");
        Dec(v => item.ReorderQuantity = v, "ROQ", "ReorderQuantity");
        Dec(v => item.ShipWeight = v, "SHIPWEIGHT");
        Dec(v => item.Commission = v, "COMMISSION");
        Dec(v => item.MaxCommission = v, "MAXCOMM");
        Int(v => item.BonusBuyQuantity = Math.Clamp(v, 0, 99), "BONUSBUY");
        Int(v => item.BonusGetQuantity = Math.Clamp(v, 0, 99), "BONUSGET");
        Date(v => item.SaleStart = v, "SALESTART");
        Date(v => item.SaleEnd = v, "SALEEND");
        Date(v => item.LastOrderDate = v, "ORDATE");
        Date(v => item.LastReceivedDate = v, "LASTSHIP");
        Date(v => item.LastUpdated = v, "LASTUPD");
        Flag(v => item.Taxable1 = v, "TX1", "Taxable1");
        Flag(v => item.Taxable2 = v, "TX2", "Taxable2");
        Flag(v => item.IsFlagged = v, "PRTFLAG");
        Flag(v => item.BonusPricing = v, "TWOFORONE");
        var cost = item.AverageCost > 0 ? item.AverageCost : item.LastCost;
        item.MarginPercent = Money.MarginPercent(item.Price1, cost);
    }

    private static bool Bool(string? v) => v is not null && (v.Equals("Y", StringComparison.OrdinalIgnoreCase) || v.Equals("YES", StringComparison.OrdinalIgnoreCase)
        || v == "1" || v.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || v.Equals("T", StringComparison.OrdinalIgnoreCase));
}
