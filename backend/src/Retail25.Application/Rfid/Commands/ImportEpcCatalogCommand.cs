using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Application.Inventory;
using Retail25.Application.Rfid.Import;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;
using Retail25.Domain.Inventory;

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

        if (request.DryRun)
        {
            // Nothing has been added to the change tracker, so there is nothing to undo. The counts
            // are what the caller wanted; a dry run that wrote anything would not be one.
            var wouldSkip = await CountMappedTagsAsync(parsed.Rows, ct);

            return Result.Success(new EpcCatalogImportResult(
                parsed.DataRows,
                TagsCreated: parsed.Rows.Count - wouldSkip,
                TagsAlreadyMapped: wouldSkip,
                ProductsCreated: created.Count,
                ProductsMatched: existing.Count,
                StockCodes: codes,
                Problems: problems));
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

        // --- Pass two: the tags --------------------------------------------------------------

        var epcs = parsed.Rows.Select(r => r.Epc).ToList();

        var mapped = await _db.SerializedUnits.AsNoTracking()
            .Where(u => u.Epc != null && epcs.Contains(u.Epc))
            .Select(u => u.Epc!)
            .ToListAsync(ct);

        var alreadyMapped = new HashSet<string>(mapped, StringComparer.Ordinal);
        var imported = new List<string>();

        foreach (var row in parsed.Rows)
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
