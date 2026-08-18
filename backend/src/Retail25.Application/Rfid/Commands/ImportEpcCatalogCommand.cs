using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Application.Inventory;
using Retail25.Application.Rfid.Import;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;
using Retail25.Domain.Inventory;
using Retail25.Domain.Purchasing;

namespace Retail25.Application.Rfid.Commands;

public sealed record EpcCatalogImportResult(
    int RowsRead,
    int TagsCreated,
    int TagsAlreadyMapped,
    int ProductsCreated,
    int ProductsMatched,
    IReadOnlyList<string> StockCodes,
    IReadOnlyList<EpcCatalogProblem> Problems);

/// <summary>
/// Loads a tag export into the catalogue: an item per stock code, a tag per EPC.
/// <para>
/// Separate from <see cref="CommissionTagBatchCommand"/>, which commissions tags against an item
/// that already exists. This is the case where the items do not — a file arrives holding both
/// halves, and the point is to land the whole thing in one pass rather than key twenty items by
/// hand first.
/// </para>
/// <para>
/// An item that already exists is matched, never overwritten. The export's prices are whatever the
/// system it came from happened to hold, and a re-import must not quietly reprice a live catalogue.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Inventory.CommissionTags)]
public sealed record ImportEpcCatalogCommand(
    long LocationId,
    string Csv,
    bool DryRun = false,
    bool ResetToInStock = true) : IRequest<Result<EpcCatalogImportResult>>;

public sealed class ImportEpcCatalogHandler
    : IRequestHandler<ImportEpcCatalogCommand, Result<EpcCatalogImportResult>>
{
    public static readonly Error LocationNotFound = new("location.not_found", "No such location.");
    public static readonly Error NothingToImport = new("import.no_rows", "The file held no rows this importer could use.");

    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;
    private readonly TagStreamRegistry _tagStreams;

    public ImportEpcCatalogHandler(IApplicationDbContext db, IDateTime clock, TagStreamRegistry tagStreams)
    {
        _db = db;
        _clock = clock;
        _tagStreams = tagStreams;
    }

    public async Task<Result<EpcCatalogImportResult>> Handle(ImportEpcCatalogCommand request, CancellationToken ct)
    {
        var locationExists = await _db.Locations.AsNoTracking()
            .AnyAsync(l => l.Id == request.LocationId && !l.IsDeleted, ct);

        if (!locationExists)
        {
            return Result.Failure<EpcCatalogImportResult>(LocationNotFound.With("locationId", request.LocationId));
        }

        var parsed = EpcCatalogCsv.Parse(request.Csv ?? string.Empty);
        var problems = new List<EpcCatalogProblem>(parsed.Problems);

        if (parsed.Rows.Count == 0)
        {
            return Result.Failure<EpcCatalogImportResult>(
                NothingToImport.With("rowsRead", parsed.DataRows).With("rejected", problems.Count));
        }

        // --- Pass one: the items -------------------------------------------------------------
        //
        // Every tag in the file names its item by stock code, and one code covers many tags, so the
        // codes are resolved as a set before a single tag is looked at.

        var byStockCode = parsed.Rows
            .GroupBy(r => r.StockCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var codes = byStockCode.Select(g => g.Key).ToList();

        var existing = await _db.Products
            .Where(p => p.LocationId == request.LocationId && !p.IsDeleted && codes.Contains(p.StockCode))
            .ToDictionaryAsync(p => p.StockCode, StringComparer.OrdinalIgnoreCase, ct);

        var created = new List<Product>();

        foreach (var group in byStockCode)
        {
            if (existing.ContainsKey(group.Key))
            {
                continue;
            }

            var first = group.First();

            var product = Product.Create(
                request.LocationId,
                group.Key,
                first.ProductName,
                first.Type,
                first.RegularPrice);

            if (product.IsFailure)
            {
                problems.Add(new EpcCatalogProblem(
                    first.LineNumber,
                    group.Key,
                    product.Error.Code,
                    product.Error.Message,
                    RowDropped: true));

                continue;
            }

            created.Add(product.Value);
        }

        // Only the rows that actually carry a tag. An untagged row is an ordinary item and has
        // nothing to do in the tag pass; left in, its empty EPC would be looked up against every
        // untagged unit in the database and then handed to SerializedUnit.Create as though it were
        // a tag. Counted here as well as used below, so a dry run does not report a tag per item
        // for a file that has no tags in it at all.
        var tagged = parsed.Rows.Where(r => r.Epc.Length > 0).ToList();

        if (request.DryRun)
        {
            // Nothing has been added to the change tracker, so there is nothing to undo. The counts
            // are what the caller wanted; a dry run that wrote anything would not be one.
            var wouldSkip = await CountMappedTagsAsync(tagged, ct);

            return Result.Success(new EpcCatalogImportResult(
                parsed.DataRows,
                TagsCreated: tagged.Count - wouldSkip,
                TagsAlreadyMapped: wouldSkip,
                ProductsCreated: created.Count,
                ProductsMatched: existing.Count,
                StockCodes: codes,
                Problems: problems));
        }

        // Everything the file said about an item beyond its code, name and price.
        //
        // Applied to items this import creates and to no others. The class comment's promise --
        // matched, never overwritten -- is what makes a re-import safe, and it would mean nothing if
        // a second pass quietly rewrote departments and costs across a live catalogue.
        var lookups = await ResolveLookupsAsync(request.LocationId, parsed.Rows, ct);
        var firstRowFor = byStockCode.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var product in created)
        {
            if (firstRowFor.TryGetValue(product.StockCode, out var row))
            {
                Enrich(product, row, lookups);
            }
        }

        _db.Products.AddRange(created);

        // The ids the tags need are assigned here and nowhere earlier. Every unit below carries a
        // ProductId, and reading it before this line yields 0 for every item created in this pass —
        // a tag pointing at no item, which resolves to "not recognised" at the till.
        await _db.SaveChangesAsync(ct);

        foreach (var product in created)
        {
            existing[product.StockCode] = product;
        }

        await LinkSuppliersAndOpeningStockAsync(request.LocationId, created, firstRowFor, lookups, ct);

        // --- Pass two: the tags --------------------------------------------------------------

        var epcs = tagged.Select(r => r.Epc).ToList();

        var mapped = await _db.SerializedUnits.AsNoTracking()
            .Where(u => u.Epc != null && epcs.Contains(u.Epc))
            .Select(u => u.Epc!)
            .ToListAsync(ct);

        var alreadyMapped = new HashSet<string>(mapped, StringComparer.Ordinal);
        var imported = new List<string>();

        foreach (var row in tagged)
        {
            if (alreadyMapped.Contains(row.Epc))
            {
                problems.Add(new EpcCatalogProblem(
                    row.LineNumber,
                    row.Epc,
                    "epc.already_mapped",
                    "This tag is already on an item in this database and was left alone.",
                    RowDropped: true));

                continue;
            }

            if (!existing.TryGetValue(row.StockCode, out var product))
            {
                // Its item failed to be created — already reported above; this is the consequence.
                continue;
            }

            var unit = SerializedUnit.Create(
                product.Id,
                request.LocationId,
                serialNumber: null,
                epc: row.Epc,
                receivedOn: row.ReceivedOn ?? _clock.Now);

            if (unit.IsFailure)
            {
                problems.Add(new EpcCatalogProblem(
                    row.LineNumber,
                    row.Epc,
                    unit.Error.Code,
                    unit.Error.Message,
                    RowDropped: true));

                continue;
            }

            var target = request.ResetToInStock ? SerializedUnitState.InStock : row.State;

            var moved = MoveTo(unit.Value, target);
            if (moved.IsFailure)
            {
                problems.Add(new EpcCatalogProblem(
                    row.LineNumber,
                    row.Epc,
                    moved.Error.Code,
                    moved.Error.Message,
                    RowDropped: true));

                continue;
            }

            _db.SerializedUnits.Add(unit.Value);
            imported.Add(row.Epc);

            // Stock arrives with the tag.
            //
            // Commissioning a unit moves it to InStock through the state machine, and that used to
            // be all that happened — so a shop that imported two hundred tagged garments had two
            // hundred units the till would sell and an inventory screen that said it owned none of
            // them. The first sale of each took its on-hand to −1, and the stock valuation was
            // understated by the whole tagged catalogue.
            //
            // Only for units that end up on hand. A row imported as already Sold, Lost or
            // Transferred came and went before this import ran; writing +1 and −1 for it would
            // invent a movement on a day it did not happen, and this ledger is meant to be
            // replayable.
            if (EndsUpOnHand(unit.Value.State))
            {
                await StockMovements.ApplyAsync(
                    _db,
                    unit.Value.ProductId,
                    unit.Value.VariantId,
                    unit.Value.LocationId,
                    quantity: 1m,
                    unitCost: 0m,
                    MovementType.Adjustment,
                    reason: "EPC catalogue import",
                    occurredAt: _clock.Now,
                    // No staff id: this is the file speaking, not a person at a till. The reason
                    // and the timestamp are what an auditor needs to trace it back to the import.
                    staffId: null,
                    ct: ct);
            }
        }

        await _db.SaveChangesAsync(ct);

        // The read feed caches what an EPC resolves to, misses included. Every tag here was a miss
        // until a moment ago; without this the tills go on reporting "Not recognised" for tags the
        // database now knows perfectly well.
        foreach (var epc in imported)
        {
            _tagStreams.ForgetCatalogue(epc);
        }

        return Result.Success(new EpcCatalogImportResult(
            parsed.DataRows,
            imported.Count,
            alreadyMapped.Count,
            created.Count,
            existing.Count - created.Count,
            codes,
            problems));
    }

    private async Task<int> CountMappedTagsAsync(IReadOnlyList<EpcCatalogRow> rows, CancellationToken ct)
    {
        var epcs = rows.Select(r => r.Epc).ToList();

        return await _db.SerializedUnits.AsNoTracking()
            .CountAsync(u => u.Epc != null && epcs.Contains(u.Epc), ct);
    }

    /// <summary>
    /// Walks the unit's state machine to the imported state rather than assigning it.
    /// <para>
    /// A unit is born <see cref="SerializedUnitState.Provisioned"/>, and the transitions exist to
    /// stop a tag reaching a state it could not have reached in the shop. Importing must not be the
    /// one path that bypasses them — a tag that arrives already <c>Sold</c> without ever being
    /// in stock is a stock figure nobody can reconcile.
    /// </para>
    /// </summary>
    /// <summary>
    /// Whether the shop is holding this unit once the import has finished.
    /// <para>
    /// <c>Returned</c> counts: it came back and is on the shelf again. <c>InCart</c> counts too —
    /// it is on somebody's basket, still owned and not yet paid for.
    /// </para>
    /// </summary>
    /// <summary>
    /// The departments, categories and suppliers the file names, by name, created where absent.
    /// <para>
    /// By name because that is what a shopkeeper has. A file exported from a supplier's system says
    /// "Menswear", not the id of a row in a database it has never seen, and demanding ids would mean
    /// keying every department by hand before the import that was supposed to save the keying.
    /// </para>
    /// <para>
    /// Matched case-insensitively so "menswear" and "Menswear" do not become two departments.
    /// </para>
    /// </summary>
    private async Task<ImportLookups> ResolveLookupsAsync(
        long locationId,
        IReadOnlyList<EpcCatalogRow> rows,
        CancellationToken ct)
    {
        var departments = await ResolveAsync(
            rows.Select(r => r.Department),
            async names => (await _db.Departments
                    .Where(d => d.LocationId == locationId && !d.IsDeleted && names.Contains(d.Name))
                    .ToListAsync(ct))
                .ToDictionary(d => d.Name, d => d.Id, StringComparer.OrdinalIgnoreCase),
            name =>
            {
                var created = Department.Create(locationId, name);
                if (created.IsFailure)
                {
                    return null;
                }

                _db.Departments.Add(created.Value);
                return created.Value;
            },
            d => d.Id,
            ct);

        var categories = await ResolveAsync(
            rows.Select(r => r.Category),
            async names => (await _db.Categories
                    .Where(c => c.LocationId == locationId && !c.IsDeleted && names.Contains(c.Name))
                    .ToListAsync(ct))
                .ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase),
            name =>
            {
                var created = Category.Create(locationId, name);
                if (created.IsFailure)
                {
                    return null;
                }

                _db.Categories.Add(created.Value);
                return created.Value;
            },
            c => c.Id,
            ct);

        var suppliers = await ResolveAsync(
            rows.Select(r => r.Supplier),
            async names => (await _db.Suppliers
                    .Where(s => s.LocationId == locationId && !s.IsDeleted && names.Contains(s.Company))
                    .ToListAsync(ct))
                .ToDictionary(s => s.Company, s => s.Id, StringComparer.OrdinalIgnoreCase),
            name =>
            {
                // The supplier number is required and the file has none, so the name doubles as one.
                // A shop that cares can rename it afterwards; a shop that does not never sees it.
                var created = Supplier.Create(locationId, name, name);
                if (created.IsFailure)
                {
                    return null;
                }

                _db.Suppliers.Add(created.Value);
                return created.Value;
            },
            s => s.Id,
            ct);

        return new ImportLookups(departments, categories, suppliers);
    }

    /// <summary>
    /// Loads what exists, creates what does not, and saves once so the new rows have ids.
    /// </summary>
    private async Task<Dictionary<string, long>> ResolveAsync<TEntity>(
        IEnumerable<string?> names,
        Func<List<string>, Task<Dictionary<string, long>>> load,
        Func<string, TEntity?> create,
        Func<TEntity, long> idOf,
        CancellationToken ct)
        where TEntity : class
    {
        var wanted = names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (wanted.Count == 0)
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }

        var found = await load(wanted);
        var made = new List<(string Name, TEntity Entity)>();

        foreach (var name in wanted.Where(n => !found.ContainsKey(n)))
        {
            var entity = create(name);

            if (entity is not null)
            {
                made.Add((name, entity));
            }
        }

        if (made.Count > 0)
        {
            await _db.SaveChangesAsync(ct);

            foreach (var (name, entity) in made)
            {
                found[name] = idOf(entity);
            }
        }

        return found;
    }

    /// <summary>Applies the file's remaining columns to an item this import is creating.</summary>
    private static void Enrich(Product product, EpcCatalogRow row, ImportLookups lookups)
    {
        if (row.Department is not null && lookups.Departments.TryGetValue(row.Department, out var departmentId))
        {
            product.SetDepartment(departmentId);
        }

        if (row.Category is not null && lookups.Categories.TryGetValue(row.Category, out var categoryId))
        {
            product.SetCategory(categoryId);
        }

        // Barcode and UPC are the same field here: Product.Upc is what the label renderer prints and
        // what the till matches a scan against. A file may head the column either way, and one that
        // carries both is taken at its word on the more specific name.
        var barcode = row.Upc ?? row.Barcode;

        if (row.Description is not null || barcode is not null || row.BinLocation is not null)
        {
            product.UpdateDetails(row.ProductName, row.Description, barcode, row.BinLocation, null);
        }

        // Cost seeds both last and average cost, because one import is the only history there is.
        // Left alone when the column is absent -- a missing cost is not a cost of zero, and a zero
        // would make the first margin report claim the whole catalogue is pure profit.
        if (row.Cost is { } cost)
        {
            product.UpdatePricing(row.RegularPrice, cost, cost);
        }

        // Weight, and the ordering figures beside it.
        //
        // Weight is what the till's WEIGHT column shows, and it stays blank at zero on purpose: no
        // weight on file and weighing nothing are different claims, and a column of blanks is the
        // honest signal that a catalogue has not been weighed. Importing it is the only way a shop
        // fills that column without opening every item by hand.
        //
        // Set together because UpdateOrdering takes them together; each falls back to what the item
        // already has, so a file carrying only a reorder point does not zero the rest.
        if (row.BaseStock is not null
            || row.ReorderPoint is not null
            || row.ReorderQty is not null
            || row.CaseQty is not null
            || row.Weight is not null)
        {
            product.UpdateOrdering(
                row.BaseStock ?? product.BaseStock,
                row.ReorderPoint ?? product.ReorderPoint,
                row.ReorderQty ?? product.ReorderQty,
                row.CaseQty ?? product.CaseQty,
                row.Weight ?? product.ShipWeight);
        }

        // Absent means "leave the default alone". A file that never mentions tax must not quietly
        // make a whole catalogue non-taxable, which is the one mistake here that shows up as missing
        // money rather than as a wrong-looking screen.
        if (row.Tax1Applies is not null || row.Tax2Applies is not null)
        {
            product.SetTaxFlags(
                row.Tax1Applies ?? product.Tax1Applies,
                row.Tax2Applies ?? product.Tax2Applies);
        }

        if (row.PosMessage is not null || row.InvoiceMessage is not null)
        {
            product.UpdateMessages(row.PosMessage, row.InvoiceMessage);
        }

        if (row.Notes is not null)
        {
            product.UpdateDetails(
                row.ProductName,
                row.Description ?? product.Description,
                row.Upc ?? row.Barcode ?? product.Upc,
                row.BinLocation ?? product.BinLocation,
                row.Notes);
        }
    }

    /// <summary>
    /// The supplier link and the opening stock, both of which need item ids and so happen after the
    /// save.
    /// </summary>
    private async Task LinkSuppliersAndOpeningStockAsync(
        long locationId,
        IReadOnlyList<Product> created,
        Dictionary<string, EpcCatalogRow> firstRowFor,
        ImportLookups lookups,
        CancellationToken ct)
    {
        foreach (var product in created)
        {
            if (!firstRowFor.TryGetValue(product.StockCode, out var row))
            {
                continue;
            }

            if (row.Supplier is not null && lookups.Suppliers.TryGetValue(row.Supplier, out var supplierId))
            {
                var link = ProductSupplier.Create(product.Id, supplierId, rank: 1, cost: row.Cost ?? 0m);

                if (link.IsSuccess)
                {
                    _db.ProductSuppliers.Add(link.Value);
                }
            }

            // Opening stock, for items counted by quantity rather than by tag.
            //
            // Tagged items get their stock from the tag pass, one unit per EPC, so adding an on-hand
            // figure here as well would count the same garments twice. And only for items this
            // import created: running the same file again must not keep adding stock that never
            // arrived. It is a ledger entry rather than a column, because on-hand is derived --
            // writing the number straight onto the item is the fault that produced negative stock.
            var untagged = row.Epc.Length == 0;

            if (untagged && row.OnHand is { } quantity && quantity != 0m)
            {
                await StockMovements.ApplyAsync(
                    _db,
                    product.Id,
                    variantId: null,
                    locationId,
                    quantity,
                    unitCost: row.Cost ?? 0m,
                    MovementType.Adjustment,
                    reason: "Catalogue import: opening stock",
                    occurredAt: _clock.Now,
                    staffId: null,
                    ct: ct);
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Names resolved to ids, once, for the whole file.</summary>
    private sealed record ImportLookups(
        Dictionary<string, long> Departments,
        Dictionary<string, long> Categories,
        Dictionary<string, long> Suppliers);

    private static bool EndsUpOnHand(SerializedUnitState state)
        => state is SerializedUnitState.InStock or SerializedUnitState.InCart or SerializedUnitState.Returned;

    private static Result MoveTo(SerializedUnit unit, SerializedUnitState target)
    {
        if (target == SerializedUnitState.Provisioned)
        {
            return Result.Success();
        }

        var commissioned = unit.Commission();
        if (commissioned.IsFailure || target == SerializedUnitState.InStock)
        {
            return commissioned;
        }

        switch (target)
        {
            case SerializedUnitState.Lost:
                return unit.MarkLost();

            case SerializedUnitState.Transferred:
                return unit.Transfer();

            case SerializedUnitState.InCart:
                return unit.ClaimForCart();

            case SerializedUnitState.Sold:
            case SerializedUnitState.Returned:
                var claimed = unit.ClaimForCart();
                if (claimed.IsFailure)
                {
                    return claimed;
                }

                var sold = unit.Sell();
                return sold.IsFailure || target == SerializedUnitState.Sold ? sold : unit.Return();

            default:
                return Result.Failure(SerializedUnit.InvalidStateTransition.With("to", target.ToString()));
        }
    }
}
