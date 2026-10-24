using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Migration.Codecs;

namespace RetailPlus.Migration.Import;

public sealed partial class DbfImporter
{
    private static readonly string[] MonthPrefixes = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

    /// <summary>A stock location: its code, its inventory table, and its folder (null for an inventory kept in the root).</summary>
    private sealed record SourceLocation(string Code, string? InventoryPath, string? Folder);

    private sealed record InventoryNames(IReadOnlyCollection<string> Departments, IReadOnlyCollection<string> Categories, IReadOnlyCollection<string> Suppliers);

    /// <summary>Live item ids by location and stock code, for linking customer orders, sales and PO lines.</summary>
    private readonly Dictionary<string, int> _items = new(StringComparer.OrdinalIgnoreCase);

    private static string ItemKey(int locationId, string plu) => locationId.ToString(CultureInfo.InvariantCulture) + "|" + plu;

    private int? ItemId(int locationId, string? plu) =>
        plu is not null && _items.TryGetValue(ItemKey(locationId, plu), out var id) ? id : null;

    [GeneratedRegex(@"^[A-Za-z0-9]{3}$")]
    private static partial Regex LocationCode();

    /// <summary>
    /// Locations: every three-character folder holding &lt;LOC&gt;INV.DBF, plus &lt;LOC&gt;INV.DBF files in the root
    /// (older layouts and the shipped TST sample). A folder without an inventory still counts when it holds BACKORD or
    /// SALESnnn files.
    /// </summary>
    private List<SourceLocation> FindLocations()
    {
        var found = new Dictionary<string, SourceLocation>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in Directory.EnumerateDirectories(_root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var code = Path.GetFileName(dir);
            if (!LocationCode().IsMatch(code) || code.Equals("PO", StringComparison.OrdinalIgnoreCase)) continue;
            var inventory = FindFile(dir, code + "INV.DBF");
            bool hasLocationFiles = inventory is not null || FindFile(dir, "BACKORD.DBF") is not null ||
                                    Directory.EnumerateFiles(dir, "SALES*.DBF").Any();
            if (hasLocationFiles) found[code] = new SourceLocation(code.ToUpperInvariant(), inventory, dir);
        }
        foreach (var file in Directory.EnumerateFiles(_root, "*INV.DBF").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.Length != 6) continue;
            var code = name[..3];
            if (!LocationCode().IsMatch(code)) continue;
            if (found.TryGetValue(code, out var existing))
            {
                if (existing.InventoryPath is null) found[code] = existing with { InventoryPath = file };
                else _report.NotImported.Add($"{Path.GetFileName(file)}: location {code} already has an inventory in its folder");
                continue;
            }
            found[code] = new SourceLocation(code.ToUpperInvariant(), file, null);
        }
        return found.Values.OrderBy(l => l.Code, StringComparer.Ordinal).ToList();
    }

    /// <summary>Department, category and supplier names used on items, so the lists can be completed first.</summary>
    private InventoryNames CollectInventoryNames(IEnumerable<SourceLocation> locations)
    {
        var departments = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var categories = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var suppliers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var location in locations)
        {
            if (location.InventoryPath is null) continue;
            using var dbf = Dbf.DbfTable.Open(location.InventoryPath);
            using var table = new SourceTable(dbf, Path.GetRelativePath(_root, location.InventoryPath), new ImportLog());
            foreach (var record in table.Records())
            {
                if (record.IsBlank) continue;
                if (record.Text("DEPT") is { } d) departments.Add(d);
                if (record.Text("CATEGORY") is { } c) categories.Add(c);
                if (record.Text("SUPPLIER") is { } s) suppliers.Add(s);
            }
        }
        return new InventoryNames(departments, categories, suppliers);
    }

    private async Task<Dictionary<string, int>> ImportLocationsAsync(List<SourceLocation> locations, CancellationToken ct)
    {
        if (locations.Count == 0)
        {
            _log.Warn("no location found; location POS created");
            locations.Add(new SourceLocation("POS", null, null));
        }
        await SaveAsync(locations.Select(l => new Pending(new Location { Code = l.Code })).ToList(), ct);
        await using var db = NewContext();
        return await db.Locations.ToDictionaryAsync(l => l.Code, l => l.Id, StringComparer.OrdinalIgnoreCase, ct);
    }

    private async Task ImportInventoryAsync(string code, int locationId, string path, CancellationToken ct)
    {
        using var table = Open(path);
        var result = Result(table, nameof(Item));
        int year = options.SalesYear ?? table.LastUpdated?.Year ?? _now.Year;
        _log.Info($"Location {code}: monthly and weekly sales columns read as {year}, LASTYR as {year - 1}");
        var rows = new List<Pending>();
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        decimal liveOnHand = 0, liveMatrixStock = 0;
        int serials = 0, liveItems = 0;
        foreach (var record in table.Records())
        {
            if (record.IsBlank) { result.Blank++; continue; }
            if (record.IsDeleted) result.Deleted++;
            var item = MapItem(record, locationId, year);
            if (!item.IsDeleted)
            {
                if (!codes.Add(item.Plu))
                {
                    var original = item.Plu;
                    int n = 2;
                    string candidate;
                    do candidate = Truncate(original, 12) + "#" + (n++).ToString(CultureInfo.InvariantCulture);
                    while (!codes.Add(candidate));
                    item.Plu = candidate;
                    item.IsFlagged = true;
                    item.Notes = AppendNote(item.Notes, $"Imported with stock code \"{original}\", which another item also used.");
                    _log.Warn(record.Where, $"duplicate stock code \"{original}\" imported as \"{candidate}\" and flagged");
                }
                liveItems++;
                liveOnHand += item.OnHand;
                liveMatrixStock += item.Variants.Sum(v => v.OnHand);
            }
            serials += item.Serials.Count;
            rows.Add(One(item, nameof(Item), record));
        }
        result.Imported = await SaveAsync(rows, ct);

        // Kit components point at items of the same location by stock code.
        await ExecAsync(
            "UPDATE k SET k.ComponentItemId = i.Id FROM [dbo].[KitComponents] k " +
            "JOIN [dbo].[Items] kit ON kit.Id = k.KitItemId " +
            "JOIN [dbo].[Items] i ON i.LocationId = kit.LocationId AND i.Plu = k.ComponentPlu AND i.IsDeleted = 0 " +
            "WHERE k.ComponentItemId IS NULL AND kit.LocationId = " + locationId.ToString(CultureInfo.InvariantCulture), ct);

        await using (var db = NewContext())
        {
            foreach (var i in await db.Items.Where(i => i.LocationId == locationId && !i.IsDeleted).Select(i => new { i.Id, i.Plu }).ToListAsync(ct))
                _items[ItemKey(locationId, i.Plu)] = i.Id;
            int unresolved = await db.KitComponents.CountAsync(k => k.ComponentItemId == null && db.Items.Any(i => i.Id == k.KitItemId && i.LocationId == locationId), ct);
            if (unresolved > 0) _log.Warn($"Location {code}: {unresolved} kit components name stock codes that are not in the inventory");
        }

        int records = result.Records - result.Blank;
        Check($"Items {code}: records", records, async (db, t) => await db.Items.CountAsync(i => i.LocationId == locationId, t));
        Check($"Items {code}: live items", liveItems, async (db, t) => await db.Items.CountAsync(i => i.LocationId == locationId && !i.IsDeleted, t));
        Check($"Items {code}: on hand (live)", liveOnHand,
            async (db, t) => await db.Items.Where(i => i.LocationId == locationId && !i.IsDeleted).SumAsync(i => i.OnHand, t));
        Check($"Items {code}: matrix stock (live)", liveMatrixStock,
            async (db, t) => await db.ItemVariants.Where(v => db.Items.Any(i => i.Id == v.ItemId && i.LocationId == locationId && !i.IsDeleted)).SumAsync(v => v.OnHand, t));
        Check($"Items {code}: serial numbers", serials,
            async (db, t) => await db.ItemSerials.CountAsync(s => db.Items.Any(i => i.Id == s.ItemId && i.LocationId == locationId), t));
    }

    private Item MapItem(SourceRecord record, int locationId, int year)
    {
        var warnings = new List<string>();
        var item = new Item
        {
            LocationId = locationId,
            Plu = record.Text("PLU") ?? "",
            Name = record.Text("PROD") ?? "",
            ItemType = ParseItemType(record),
            DepartmentId = record.Text("DEPT") is { } dept && _departments.TryGetValue(dept, out var d) ? d : null,
            CategoryId = record.Text("CATEGORY") is { } cat && _categories.TryGetValue(cat, out var c) ? c : null,
            Brand = record.Text("BRAND"),
            Size = record.Text("BSIZE"),
            PackQuantity = record.Int("PKG", 0, 9999),
            SupplierId = SupplierId(record.Text("SUPPLIER")),
            SupplierCode = record.Text("SUPPCODE"),
            Taxable1 = record.Bool("TX1"),
            Taxable2 = record.Bool("TX2"),
            BinLocation = record.Text("BINLOC"),
            ReadScale = record.Bool("SCALE"),
            WebDelete = record.Bool("WEBDELETE"),
            OnHand = record.Dec("ONHAND"),
            BaseStock = record.Dec("BASTOCK"),
            ReorderPoint = record.Dec("ROP"),
            ReorderQuantity = record.Dec("ROQ"),
            OnOrder = record.Dec("ONORDER"),
            LastOrderDate = record.Date("ORDATE"),
            LastReceivedDate = record.Date("LASTSHIP"),
            LastReceivedQuantity = record.Dec("RECVD"),
            CustomerOrdered = record.Dec("BACKORD"),
            OnHandBeforeYearEnd = record.Dec("B4YREND"),
            LastUpdated = record.Date("LASTUPD"),
            LastCost = record.Dec("LASTCOST"),
            AverageCost = record.Dec("AVGCOST"),
            Price1 = record.Dec("UNITPRICE1"),
            Price2 = record.Dec("UNITPRICE2"),
            Price3 = record.Dec("UNITPRICE3"),
            Price4 = record.Dec("UNITPRICE4"),
            BreakQuantity1 = record.Dec("PRICEBRK1"),
            BreakQuantity2 = record.Dec("PRICEBRK2"),
            BreakQuantity3 = record.Dec("PRICEBRK3"),
            MarginPercent = record.Dec("MARGIN"),
            SaleStart = record.Date("SALESTART"),
            SaleEnd = record.Date("SALEEND"),
            SalePrice = record.Dec("SALEPRICE"),
            BonusPricing = record.Bool("TWOFORONE"),
            BonusBuyQuantity = record.Int("BONUSBUY", 0, 99),
            BonusGetQuantity = record.Int("BONUSGET", 0, 99),
            SubstituteCode = record.Text("SUBCODE"),
            TagAlongCode = record.Text("TAGALONG"),
            ParentCode = record.Text("PARENT"),
            ShipWeight = record.Dec("SHIPWEIGHT"),
            ThcWeight = record.Dec("THCWEIGHT"),
            Commission = record.Dec("COMMISSION"),
            MaxCommission = record.Dec("MAXCOMM"),
            CommissionType = record.Text("COMMTYPE"),
            PhotoPath = PhotoPath(record.Text("PICPATH")),
            Notes = record.Memo("NOTES"),
            PosMessage = record.Memo("SALESMSG"),
            InvoiceMessage = record.Memo("INVOICEMSG"),
            IsFlagged = record.Bool("PRTFLAG"),
            IsDeleted = record.IsDeleted,
            DeletedAt = record.IsDeleted ? _now : null,
        };
        if (item.Plu.Length == 0) warnings.Add("PLU is blank");

        var matrix = PackedLists.ParseMatrix(record.Memo("MATRIX"), warnings);
        for (int i = 0; i < 3; i++)
            if (matrix.Headings[i] is { } heading)
                item.MatrixColumns.Add(Remember(new ItemMatrixColumn { Ordinal = i + 1, Heading = heading }, record));
        int ordinal = 0;
        foreach (var row in matrix.Rows)
            item.Variants.Add(Remember(new ItemVariant
            {
                Ordinal = ++ordinal,
                Value1 = row.Value1,
                Value2 = row.Value2,
                Value3 = row.Value3,
                Code = matrix.HasCodeColumn ? row.Value1 : null,
                OnHand = row.OnHand,
            }, record));

        int rank = 0;
        foreach (var s in PackedLists.ParseSupplierList(record.Memo("SUPLIST"), warnings))
            item.Suppliers.Add(Remember(new ItemSupplier
            {
                Rank = ++rank,
                SupplierName = s.Name,
                SupplierId = SupplierId(s.Name),
                SupplierStockCode = s.StockCode,
                Cost = s.Cost,
                CaseQuantity = s.CaseQuantity,
            }, record));

        ordinal = 0;
        foreach (var k in PackedLists.ParseKit(record.Memo("KIT"), warnings))
            item.KitComponents.Add(Remember(new KitComponent { Ordinal = ++ordinal, ComponentPlu = k.Plu, Description = k.Description, Quantity = k.Quantity }, record));

        foreach (var s in PackedLists.ParseSerials(record.Memo("SERNO"), _ukDates, warnings))
            item.Serials.Add(Remember(new ItemSerial { SerialNumber = s.SerialNumber, Status = s.Status, StatusDate = s.Date }, record));

        for (int m = 0; m < 12; m++)
        {
            decimal units = record.Dec(MonthPrefixes[m] + "_VOLUME"), gross = record.Dec(MonthPrefixes[m] + "_GROSS");
            if (units != 0 || gross != 0)
                item.PeriodSales.Add(new ItemPeriodSales { Year = year, Kind = SalesPeriodKind.Month, Period = m + 1, Units = units, Gross = gross });
        }
        var lastYear = PackedLists.ParseNumbers(record.RawText("LASTYR"), "LASTYR", warnings);
        if (lastYear.Count > 12) warnings.Add($"LASTYR has {lastYear.Count} values; the first 12 were used");
        for (int m = 0; m < Math.Min(12, lastYear.Count); m++)
            if (lastYear[m] != 0)
                item.PeriodSales.Add(new ItemPeriodSales { Year = year - 1, Kind = SalesPeriodKind.Month, Period = m + 1, Units = lastYear[m] });
        var weekly = PackedLists.ParseNumbers(record.RawText("WEEKLY"), "WEEKLY", warnings);
        if (weekly.Count > 54) warnings.Add($"WEEKLY has {weekly.Count} values; the first 54 were used");
        for (int w = 0; w < Math.Min(54, weekly.Count); w++)
            if (weekly[w] != 0)
                item.PeriodSales.Add(new ItemPeriodSales { Year = year, Kind = SalesPeriodKind.Week, Period = w + 1, Units = weekly[w] });

        Warn(record, warnings);
        return item;
    }

    private ItemType ParseItemType(SourceRecord record)
    {
        var text = record.Text("ITEMTYPE");
        var key = text?.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).ToUpperInvariant();
        ItemType? type = key switch
        {
            null or "STANDARD" or "STOCK" or "REGULAR" => ItemType.Standard,
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
        if (type is null) _log.Warn(record.Where, $"unknown ITEMTYPE \"{text}\"; imported as Standard");
        return type ?? ItemType.Standard;
    }

    /// <summary>The per-location files besides the inventory: return address, customer orders, sales, drawer totals and POs.</summary>
    private async Task ImportLocationFolderAsync(string code, int locationId, string folder, CancellationToken ct)
    {
        if (FindFile(folder, "RETADDR.ADD") is { } addressFile && ReadText(addressFile) is { } address)
        {
            await SaveAsync([new Pending(new DocumentText { Kind = DocumentTextKind.ReturnAddress, LocationId = locationId, Text = address })], ct);
            _report.Tables.Add(new TableResult(Path.GetRelativePath(_root, addressFile), nameof(DocumentText)) { Records = 1, Imported = 1 });
        }
        if (FindFile(folder, "BACKORD.DBF") is { } backorders) await ImportCustomerOrdersAsync(code, locationId, backorders, ct);
        await ImportSalesAsync(code, locationId, folder, ct);
        var poFolder = Path.Combine(folder, "PO");
        if (Directory.Exists(poFolder)) await ImportPurchaseOrdersAsync(code, locationId, poFolder, ct);

        foreach (var (file, what) in new[] { ("POSTRANS.DBF", "stock transfer staging"), ("SUSPEND.DBF", "suspended sales") })
        {
            if (FindFile(folder, file) is not { } path) continue;
            using var table = Open(path);
            int live = table.Records().Count(r => !r.IsDeleted && !r.IsBlank);
            _report.NotImported.Add(live == 0
                ? $"{table.RelativePath}: {what}, empty"
                : $"{table.RelativePath}: {what}, {live} records NOT imported (format not captured yet; finish or clear them in Retail Plus before the cutover)");
            if (live > 0) _log.Warn(table.RelativePath, $"{live} {what} records were not imported");
        }
    }

    private async Task ImportCustomerOrdersAsync(string code, int locationId, string path, CancellationToken ct)
    {
        using var table = Open(path);
        var result = Result(table, nameof(CustomerOrder));
        var rows = new List<Pending>();
        foreach (var record in table.Records())
        {
            if (record.IsBlank) { result.Blank++; continue; }
            if (record.IsDeleted) { result.Deleted++; rows.Add(TraceOnly(nameof(CustomerOrder), record)); continue; }
            var plu = record.Text("PLU");
            var customerNumber = record.Text("CUSTNO");
            rows.Add(One(new CustomerOrder
            {
                LocationId = locationId,
                ClientId = ClientId(null, customerNumber),
                Name = record.Text("NAME"),
                Company = record.Text("COMPANY"),
                Phone = record.Text("PHONE"),
                OrderDate = record.Date("ORDATE"),
                SoldBy = record.Text("SOLDBY"),
                InvoiceNumber = (long)record.Dec("INVOICE"),
                CustomerNumber = customerNumber,
                ItemId = ItemId(locationId, plu),
                Plu = plu,
                Description = record.Text("PROD") ?? "",
                Size = record.Text("BSIZE"),
                UnitPrice = record.Dec("UNITPRICE"),
                Quantity = record.Dec("QTY"),
                Paid = record.Bool("PAID"),
                IsFlagged = record.Bool("PRTFLAG"),
            }, nameof(CustomerOrder), record));
        }
        result.Imported = await SaveAsync(rows, ct);
        int expected = result.Records - result.Blank - result.Deleted;
        Check($"Customer orders {code}: records", expected, async (db, t) => await db.CustomerOrders.CountAsync(o => o.LocationId == locationId, t));
    }
}
