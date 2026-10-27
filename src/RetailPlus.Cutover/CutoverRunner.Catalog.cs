using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Cutover.Source;

namespace RetailPlus.Cutover;

public sealed partial class CutoverRunner
{
    /// <summary>A carried-over product: the item it became and the stock it starts with.</summary>
    private sealed record CarriedItem(Guid Id, CarriedStore Store, SourceRow Row, string Plu, string Name, ItemType Type, string? Department);

    private readonly Dictionary<long, CarriedItem> _items = [];
    private readonly Dictionary<long, (Guid Id, Guid ItemId)> _variants = [];
    private readonly Dictionary<long, (Guid Id, string Name)> _suppliers = [];

    private CarriedItem? ItemOf(long? productId) => productId is long id ? _items.GetValueOrDefault(id) : null;

    /// <summary>Retail25 product types, which are the item types of the new model under the same names.</summary>
    private static ItemType TypeOf(int type) => type switch
    {
        1 => ItemType.Matrix,
        2 => ItemType.Serialized,
        3 => ItemType.Kit,
        4 => ItemType.NonStock,
        5 => ItemType.Rental,
        6 => ItemType.Service,
        7 => ItemType.Shipping,
        8 => ItemType.Admission,
        9 => ItemType.GiftCard,
        _ => ItemType.Standard,
    };

    /// <summary>Numbers that are unique among live rows: a repeat (two locations numbering alike) gets the location code in front.</summary>
    private static string? UniqueNumber(string? number, string locationCode, HashSet<string> used, int max)
    {
        if (number is null) return null;
        if (used.Add(number)) return number;
        var prefixed = Cut(locationCode + "-" + number, max);
        for (int n = 2; !used.Add(prefixed); n++) prefixed = Cut(locationCode + Number(n) + "-" + number, max);
        return prefixed;
    }

    private async Task SuppliersAsync(CancellationToken ct)
    {
        Log("Suppliers");
        var area = _report.Area("Suppliers", "suppliers");
        var rows = new List<Supplier>();
        var numbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _src["suppliers"].Rows.OrderBy(r => r.Long("location_id")).ThenBy(r => r.Id))
        {
            area.Read++;
            var store = StoreOf(row.Long("location_id"));
            if (store is null && _report.StoreCode is not null) { area.Skip("store not carried over (--store-code)"); continue; }
            var code = store?.Code ?? "R25";
            bool deleted = row.Bool("is_deleted");
            var number = row.Text("supplier_number");
            var unique = deleted ? number : UniqueNumber(number, code, numbers, 11);
            if (unique != number) Note($"Retail25 supplier number {number} ({row.Text("company")}) is {unique} here: another location's supplier has the same number.");
            var supplier = From(new Supplier
            {
                Id = StableIds.For("supplier", row.Id),
                Company = row.Text("company"), FirstName = row.Text("contact_first_name"), LastName = row.Text("contact_last_name"), Title = row.Text("title"),
                Address1 = row.Text("address_line1"), Address2 = row.Text("address_line2"), City = row.Text("address_city"),
                State = row.Text("address_state_or_province"), Zip = row.Text("address_postal_code"), Country = row.Text("address_country"),
                WorkPhone = row.Text("contact_phone"), Extension = row.Text("contact_extension"), Fax = row.Text("contact_fax"),
                HomePhone = row.Text("contact_mobile"), Email = row.Text("contact_email"), Website = row.Text("contact_website"),
                Number = unique, IsDeleted = deleted,
                DeletedAt = deleted && row.Instant("deleted_at") is { } at ? (store is null ? Seconds(at.LocalDateTime) : Local(store, at)) : null,
                CreatedAtUtc = Utc(row.Instant("created_at")),
            }, row);
            rows.Add(supplier);
            _suppliers[row.Id] = (supplier.Id, Cut(supplier.Company ?? $"{supplier.FirstName} {supplier.LastName}".Trim(), 30));
        }
        await _w.UpsertAsync(area, rows, ct);
    }

    /// <summary>Retail25 moves stock for these product types only (CompleteSaleCommand.TracksStock).</summary>
    private static bool TracksStock(int productType) => productType is 0 or 1 or 2 or 3;

    /// <summary>The lines of open layaways (of carried stores) whose goods Retail25 still counts in stock.</summary>
    private IEnumerable<SourceRow> OpenLayawayStockLines()
    {
        var open = _src["layaways"].Rows.Where(l => l.Int("status") == 0 && _stores.ContainsKey(l.Long("location_id"))).Select(l => l.Id).ToHashSet();
        return _src["layaway_lines"].Rows.Where(l => open.Contains(l.Long("layaway_id"))
            && _src["products"].Find(l.Long("product_id")) is { } product && TracksStock(product.Int("type")) && _stores.ContainsKey(product.Long("location_id")));
    }

    /// <summary>
    /// Quantities on open layaways, by product and variant. Retail25 counts layaway goods in stock (committed); here a
    /// layaway is a sale, so its goods are out of stock: the cutover takes them out with one movement per layaway line.
    /// </summary>
    private Dictionary<(long Product, long? Variant), decimal> LayawayQuantities() =>
        OpenLayawayStockLines().GroupBy(l => (l.Long("product_id"), l.LongOrNull("variant_id")))
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Dec("quantity")));

    /// <summary>
    /// Products become items of their location: prices (level 1 is the regular price, levels 2–4 and their break
    /// quantities), costs, tax flags, the barcode, the supplier list, kit components, matrix columns and lines, serial
    /// numbers and RFID tags, the sale price and bonus pricing, and the picture. Stock comes from Retail25's stock levels.
    /// </summary>
    private async Task ItemsAsync(CancellationToken ct)
    {
        Log("Items");
        var area = _report.Area("Items", "products, product_prices, price_breaks, sale_pricings, bonus_pricings");
        var products = _src["products"];
        var today = DateOnly.FromDateTime(DateTime.Today);
        var prices = _src["product_prices"].Rows.ToLookup(p => p.Long("product_id"));
        var breaks = _src["price_breaks"].Rows.ToLookup(p => p.Long("product_id"));
        var salePricing = _src["sale_pricings"].Rows.ToLookup(p => p.Long("product_id"));
        var bonus = _src["bonus_pricings"].Rows.ToLookup(p => p.Long("product_id"));
        var supplierLinks = _src["product_suppliers"].Rows.ToLookup(p => p.Long("product_id"));
        var levels = _src["stock_levels"].Rows.ToLookup(l => l.Long("product_id"));
        var onLayaway = LayawayQuantities();

        // Stock codes: unique among an location's live items, at most 16 characters.
        var codes = new Dictionary<long, HashSet<string>>();
        var items = new List<Item>();
        foreach (var row in products.Rows.OrderBy(r => r.Long("location_id")).ThenBy(r => r.Id))
        {
            area.Read++;
            if (StoreOf(row.Long("location_id")) is not { } store) { area.Skip("store not carried over"); continue; }
            bool deleted = row.Bool("is_deleted");
            var fullCode = (row.Text("stock_code") ?? "P" + Number(row.Id)).ToUpperInvariant();
            var plu = Cut(fullCode, 16).TrimEnd();
            var used = codes.TryGetValue(row.Long("location_id"), out var set) ? set : codes[row.Long("location_id")] = new(StringComparer.OrdinalIgnoreCase);
            bool flagged = false;
            var notes = new List<string>();
            if (!deleted && !used.Add(plu))
            {
                int n = 2;
                var stem = Cut(fullCode, 12).TrimEnd();
                do plu = stem + "#" + Number(n++);
                while (!used.Add(plu));
                flagged = true;
                notes.Add($"Carried over from Retail25 with the stock code \"{fullCode}\", which another item also has here.");
                Warn($"products {row.Id}: stock code \"{fullCode}\" is \"{plu}\" here and the item is flagged");
            }
            else if (plu != fullCode)
            {
                flagged = true;
                notes.Add($"Stock code in Retail25: \"{fullCode}\" (stock codes have at most 16 characters here).");
                Warn($"products {row.Id}: stock code \"{fullCode}\" is \"{plu}\" here and the item is flagged");
            }

            var name = row.Text("name") ?? plu;
            if (name.Length > 30) notes.Add($"Full name in Retail25: {name}");
            if (row.Text("description") is { } description) notes.Add(description);
            if (row.Text("notes") is { } more) notes.Add(more);

            string? barcode = row.Text("upc");
            if (barcode is not null && (barcode.Length > 14 || !barcode.All(char.IsAsciiDigit)))
            {
                notes.Add($"Code in Retail25: {barcode} (not a product barcode).");
                Warn($"products {row.Id}: \"{barcode}\" is not a UPC or EAN barcode; it is kept in the item's notes");
                barcode = null;
            }

            decimal Level(int level) => Money(prices[row.Id].Where(p => p.Int("level") == level).OrderByDescending(p => p.Id).FirstOrDefault()?.Dec("price") ?? 0, "Item.Price" + Number(level));
            decimal Break(int level) => Three(breaks[row.Id].Where(p => p.Int("level") == level).OrderByDescending(p => p.Id).FirstOrDefault()?.Dec("min_quantity") ?? 0, "Item.BreakQuantity");
            var sale = salePricing[row.Id].Where(s => s.Date("ends_on") >= today).OrderBy(s => s.Date("starts_on")).FirstOrDefault()
                       ?? salePricing[row.Id].OrderByDescending(s => s.Date("ends_on")).FirstOrDefault();
            if (salePricing[row.Id].Count() > 1) Warn($"products {row.Id}: {salePricing[row.Id].Count()} sale prices in Retail25; the current or next one is kept");
            var bonusRow = bonus[row.Id].OrderByDescending(b => b.Id).FirstOrDefault();
            decimal buy = bonusRow?.Dec("buy_qty") ?? 0, free = bonusRow?.Dec("free_qty") ?? 0;
            if (bonusRow is not null && (buy != decimal.Truncate(buy) || free != decimal.Truncate(free)))
                Warn($"products {row.Id}: bonus pricing \"buy {buy:0.###}, {free:0.###} free\" is kept in whole units");

            var preferred = supplierLinks[row.Id].OrderBy(s => s.Int("rank")).ThenBy(s => s.Id).FirstOrDefault();
            var parent = products.Find(row.LongOrNull("parent_product_id"));
            var type = TypeOf(row.Int("type"));
            decimal stock = levels[row.Id].Where(l => l.Long("location_id") == row.Long("location_id")).Sum(l => l.Dec("on_hand"))
                            - onLayaway.Where(q => q.Key.Product == row.Id).Sum(q => q.Value);
            if (levels[row.Id].Any(l => l.Long("location_id") != row.Long("location_id")))
                Warn($"products {row.Id}: stock levels at another location than the product's are left out");
            var item = From(new Item
            {
                Id = StableIds.For("product", row.Id), LocationId = store.LocationId, Plu = plu, Name = name, Barcode = barcode, ItemType = type,
                DepartmentId = row.LongOrNull("department_id") is long d && _departments.TryGetValue(d, out var dept) ? dept.Id : null,
                CategoryId = row.LongOrNull("category_id") is long c && _categories.TryGetValue(c, out var category) ? category : null,
                SupplierId = preferred is not null && _suppliers.TryGetValue(preferred.Long("supplier_id"), out var supplier) ? supplier.Id : null,
                SupplierCode = preferred?.Text("reorder_number"),
                Taxable1 = row.Bool("tax1applies"), Taxable2 = row.Bool("tax2applies"), BinLocation = row.Text("bin_location"),
                OnHand = Three(stock, "Item.OnHand"),
                BaseStock = row.Dec("base_stock"), ReorderPoint = row.Dec("reorder_point"), ReorderQuantity = row.Dec("reorder_qty"),
                OnOrder = Three(levels[row.Id].Sum(l => l.Dec("on_order")) is var onOrder && onOrder != 0 ? onOrder : row.Dec("on_order"), "Item.OnOrder"),
                LastCost = Three(row.Dec("last_cost"), "Item.LastCost"), AverageCost = Three(row.Dec("avg_cost"), "Item.AverageCost"),
                Price1 = Money(prices[row.Id].Where(p => p.Int("level") == 1).OrderByDescending(p => p.Id).FirstOrDefault()?.Dec("price") ?? row.Dec("regular_price"), "Item.Price1"),
                Price2 = Level(2), Price3 = Level(3), Price4 = Level(4),
                BreakQuantity1 = Break(2), BreakQuantity2 = Break(3), BreakQuantity3 = Break(4),
                MarginPercent = Three(row.Dec("gross_margin_pct"), "Item.MarginPercent"),
                SalePrice = sale is null ? 0 : Money(sale.Dec("discount_pct"), "Item.SalePrice"), SaleStart = sale?.Date("starts_on"), SaleEnd = sale?.Date("ends_on"),
                // Retail25 counts the free units in "buy": buy 3, 1 free is buy 2, get 1 here.
                BonusPricing = bonusRow is not null && buy > free && free > 0,
                BonusBuyQuantity = bonusRow is null ? 0 : (int)Math.Max(0, decimal.Truncate(buy - free)), BonusGetQuantity = bonusRow is null ? 0 : (int)decimal.Truncate(free),
                PackQuantity = (int)Math.Max(0, decimal.Truncate(row.Dec("case_qty"))),
                ParentCode = parent?.Text("stock_code")?.ToUpperInvariant(),
                UnitsPerCase = parent is not null && parent.Dec("case_qty") > 1 ? Three(parent.Dec("case_qty"), "Item.UnitsPerCase") : 0,
                SubstituteCode = products.Find(row.LongOrNull("substitute_product_id"))?.Text("stock_code")?.ToUpperInvariant(),
                TagAlongCode = products.Find(row.LongOrNull("tag_along_product_id"))?.Text("stock_code")?.ToUpperInvariant(),
                ShipWeight = Three(row.Dec("ship_weight"), "Item.ShipWeight"),
                PosMessage = row.Text("pos_message"), InvoiceMessage = row.Text("invoice_message"),
                Notes = notes.Count == 0 ? null : string.Join(Environment.NewLine, notes),
                IsFlagged = flagged, IsDeleted = deleted,
                DeletedAt = deleted && row.Instant("deleted_at") is { } at ? Local(store, at) : null,
                CreatedAtUtc = Utc(row.Instant("created_at")),
            }, row);
            items.Add(item);
            _items[row.Id] = new CarriedItem(item.Id, store, row, plu, Cut(name, 60), type, row.LongOrNull("department_id") is long dd && _departments.TryGetValue(dd, out var dn) ? dn.Name : null);
        }
        await _w.UpsertAsync(area, items, ct, merge: (stored, mapped) => mapped.IsFlagged |= stored.IsFlagged);

        await ItemChildrenAsync(onLayaway, ct);
        await SerialsAndTagsAsync(ct);
        await PicturesAsync(ct);
    }

    /// <summary>Supplier lists, matrix columns and lines, and kit components. Rows Retail25 no longer has are removed.</summary>
    private async Task ItemChildrenAsync(Dictionary<(long Product, long? Variant), decimal> onLayaway, CancellationToken ct)
    {
        var carried = _items.Values.Select(i => i.Id).ToHashSet();

        var supplierArea = _report.Area("Item suppliers", "product_suppliers");
        var links = new List<ItemSupplier>();
        foreach (var group in _src["product_suppliers"].Rows.GroupBy(r => r.Long("product_id")))
        {
            supplierArea.Read += group.Count();
            if (ItemOf(group.Key) is not { } item) { supplierArea.Skip("item not carried over", group.Count()); continue; }
            int rank = 0;
            foreach (var row in group.OrderBy(r => r.Int("rank")).ThenBy(r => r.Id))
            {
                var supplier = _suppliers.GetValueOrDefault(row.Long("supplier_id"));
                links.Add(From(new ItemSupplier
                {
                    Id = StableIds.For("item-supplier", item.Id.ToString("N") + "/" + Number(++rank)), ItemId = item.Id, Rank = rank,
                    SupplierId = supplier.Id == Guid.Empty ? null : supplier.Id, SupplierName = supplier.Name ?? "Supplier " + Number(row.Long("supplier_id")),
                    SupplierStockCode = row.Text("reorder_number"), Cost = Three(row.Dec("cost"), "ItemSupplier.Cost"), CaseQuantity = Three(row.Dec("case_qty"), "ItemSupplier.CaseQuantity"),
                    CreatedAtUtc = Utc(row.Instant("created_at")),
                }, row));
            }
        }
        await _w.UpsertAsync(supplierArea, links, ct);
        await _w.RemoveMissingAsync<ItemSupplier>(supplierArea, db => db.ItemSuppliers.Where(s => carried.Contains(s.ItemId)), links.Select(l => l.Id).ToHashSet(), ct);

        var columnArea = _report.Area("Matrix columns", "matrix_dimensions");
        var columns = new List<ItemMatrixColumn>();
        foreach (var row in _src["matrix_dimensions"].Rows.OrderBy(r => r.Long("product_id")).ThenBy(r => r.Int("position")))
        {
            columnArea.Read++;
            if (ItemOf(row.Long("product_id")) is not { } item) { columnArea.Skip("item not carried over"); continue; }
            int position = row.Int("position");
            if (position is < 1 or > 3 || columns.Any(c => c.ItemId == item.Id && c.Ordinal == position)) { columnArea.Skip("position not 1, 2 or 3, or repeated"); continue; }
            columns.Add(From(new ItemMatrixColumn { Id = StableIds.For("matrix-column", item.Id.ToString("N") + "/" + Number(position)), ItemId = item.Id, Ordinal = position, Heading = row.Text("name") ?? "Column " + Number(position) }, row));
        }
        await _w.UpsertAsync(columnArea, columns, ct);
        await _w.RemoveMissingAsync<ItemMatrixColumn>(columnArea, db => db.ItemMatrixColumns.Where(c => carried.Contains(c.ItemId)), columns.Select(c => c.Id).ToHashSet(), ct);

        var variantArea = _report.Area("Matrix lines", "product_variants, stock_levels");
        var levels = _src["stock_levels"].Rows.Where(l => l.LongOrNull("variant_id") is not null).ToLookup(l => l.Long("variant_id"));
        var variants = new List<ItemVariant>();
        int retired = 0;
        foreach (var group in _src["product_variants"].Rows.GroupBy(r => r.Long("product_id")))
        {
            variantArea.Read += group.Count();
            if (ItemOf(group.Key) is not { } item) { variantArea.Skip("item not carried over", group.Count()); continue; }
            int ordinal = 0;
            foreach (var row in group.OrderBy(r => r.Id))
            {
                var stock = levels[row.Id].Sum(l => l.Dec("on_hand")) - onLayaway.GetValueOrDefault((group.Key, row.Id));
                var variant = From(new ItemVariant
                {
                    Id = StableIds.For("variant", row.Id), ItemId = item.Id, Ordinal = ++ordinal,
                    Value1 = row.Text("dim1value"), Value2 = row.Text("dim2value"), Value3 = row.Text("dim3value"),
                    Code = row.Text("upc") ?? row.Text("variant_code"), OnHand = Three(stock, "ItemVariant.OnHand"),
                    CreatedAtUtc = Utc(row.Instant("created_at")),
                }, row);
                variants.Add(variant);
                _variants[row.Id] = (variant.Id, item.Id);
                if (!row.Bool("is_active")) retired++;
            }
        }
        if (retired > 0) Note($"Matrix lines retired in Retail25 ({retired}) are carried over all the same, as sales name them; the new model has no retired flag on matrix lines.");
        await _w.UpsertAsync(variantArea, variants, ct);
        await _w.RemoveMissingAsync<ItemVariant>(variantArea, db => db.ItemVariants.Where(v => carried.Contains(v.ItemId)), variants.Select(v => v.Id).ToHashSet(), ct);

        var kitArea = _report.Area("Kit components", "kit_components");
        var components = new List<KitComponent>();
        int keepStock = 0;
        foreach (var group in _src["kit_components"].Rows.GroupBy(r => r.Long("kit_product_id")))
        {
            kitArea.Read += group.Count();
            if (ItemOf(group.Key) is not { } kit) { kitArea.Skip("kit not carried over", group.Count()); continue; }
            int ordinal = 0;
            foreach (var row in group.OrderBy(r => r.Id))
            {
                var part = _src["products"].Find(row.Long("component_product_id"));
                var partItem = ItemOf(row.Long("component_product_id"));
                if (!row.Bool("reduce_stock")) keepStock++;
                components.Add(From(new KitComponent
                {
                    Id = StableIds.For("kit-component", kit.Id.ToString("N") + "/" + Number(++ordinal)), KitItemId = kit.Id, Ordinal = ordinal,
                    ComponentPlu = partItem?.Plu ?? part?.Text("stock_code")?.ToUpperInvariant() ?? "P" + Number(row.Long("component_product_id")),
                    ComponentItemId = partItem?.Id, Description = part?.Text("name"), Quantity = Three(row.Dec("quantity"), "KitComponent.Quantity"),
                }, row));
            }
        }
        await _w.UpsertAsync(kitArea, components, ct);
        await _w.RemoveMissingAsync<KitComponent>(kitArea, db => db.KitComponents.Where(k => carried.Contains(k.KitItemId)), components.Select(k => k.Id).ToHashSet(), ct);
        if (keepStock > 0)
            _report.NotCarriedOver.Add(new NotCarried("\"Do not reduce stock\" on kit components (kit_components.reduce_stock)", keepStock,
                "Kits here always take their components out of stock. Check these kits after the switch."));
    }

    /// <summary>Retail25's serialized units: their serial numbers (Serials) and their RFID tags (EPCs).</summary>
    private async Task SerialsAndTagsAsync(CancellationToken ct)
    {
        var serialArea = _report.Area("Serial numbers", "serialized_units");
        var tagArea = _report.Area("RFID tags", "serialized_units");
        var serials = new List<ItemSerial>();
        var tags = new List<ItemTag>();
        var epcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _src["serialized_units"].Rows.OrderBy(r => r.Id))
        {
            serialArea.Read++;
            tagArea.Read++;
            if (ItemOf(row.Long("product_id")) is not { } item) { serialArea.Skip("item not carried over"); tagArea.Skip("item not carried over"); continue; }
            var state = row.Text("state") ?? "InStock";
            var at = row.Instant("last_seen_at") ?? row.Instant("received_on");
            var serial = row.Text("serial_number");
            if (serial is null) serialArea.Skip("no serial number");
            else
                serials.Add(From(new ItemSerial
                {
                    Id = StableIds.For("serial", row.Id), ItemId = item.Id, SerialNumber = serial,
                    Status = state switch { "InStock" => "IN STOCK", "InCart" => "IN CART", _ => state.ToUpperInvariant() },
                    StatusDate = at is { } seen ? DateOnly.FromDateTime(Local(item.Store, seen)) : null,
                    CreatedAtUtc = Utc(row.Instant("created_at")),
                }, row));

            var epc = row.Text("epc")?.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
            if (epc is null) { tagArea.Skip("no RFID tag"); continue; }
            if (!epcs.Add(epc)) { tagArea.Skip("an EPC another unit has too"); Warn($"serialized_units {row.Id}: EPC {epc} is on another unit as well; only the first is kept"); continue; }
            tags.Add(From(new ItemTag
            {
                Id = StableIds.For("tag", row.Id), Epc = epc, ItemId = item.Id,
                VariantId = row.LongOrNull("variant_id") is long v && _variants.TryGetValue(v, out var variant) ? variant.Id : null,
                SerialNumber = serial,
                Status = state switch { "Sold" or "Transferred" => ItemTagStatus.Sold, "Lost" or "Void" => ItemTagStatus.Lost, _ => ItemTagStatus.InStock },
                RegisteredAt = row.Instant("received_on") is { } received ? Local(item.Store, received) : Local(item.Store, DateTimeOffset.UtcNow),
                CreatedAtUtc = Utc(row.Instant("created_at")),
            }, row));
        }
        await _w.UpsertAsync(serialArea, serials, ct);
        await _w.UpsertAsync(tagArea, tags, ct, merge: (stored, mapped) => mapped.RegisteredAt = stored.RegisteredAt);
    }

    private async Task PicturesAsync(CancellationToken ct)
    {
        var area = _report.Area("Item pictures", "product_images");
        var pictures = new List<ItemPicture>();
        foreach (var row in _src["product_images"].Rows.OrderByDescending(r => r.Id))
        {
            area.Read++;
            if (ItemOf(row.Long("product_id")) is not { } item) { area.Skip("item not carried over"); continue; }
            if (pictures.Any(p => p.ItemId == item.Id)) { area.Skip("a second picture of the item (the latest is kept)"); continue; }
            var type = row.Text("content_type");
            if (type is not ("image/png" or "image/jpeg")) { area.Skip($"{type ?? "unknown"} picture (the tills show PNG and JPEG)"); continue; }
            var data = row.Bytes("content");
            if (data is null || data.Length == 0) { area.Skip("empty"); continue; }
            pictures.Add(From(new ItemPicture { ItemId = item.Id, Data = data, CreatedAtUtc = Utc(row.Instant("created_at")) }, row));
        }
        await _w.UpsertAsync(area, pictures, ct);
    }
}
