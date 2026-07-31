using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;
using Retail25.Domain.Inventory;
using Retail25.Domain.Configuration;

namespace Retail25.Application.Inventory;

public sealed record TransferLineDto(
    Guid Id,
    Guid ProductId,
    string StockCode,
    string ProductName,
    decimal Quantity,
    decimal QuantityReceived,
    decimal Outstanding,
    decimal UnitCost,
    decimal SourceOnHand);

public sealed record TransferDto(
    Guid Id,
    long TransferNumber,
    Guid FromLocationId,
    string FromLocationName,
    Guid ToLocationId,
    string ToLocationName,
    TransferStatus Status,
    string? Notes,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? ReceivedAt,
    decimal TotalValue,
    IReadOnlyList<TransferLineDto> Lines);

public sealed record TransferRowDto(
    Guid Id,
    long TransferNumber,
    string FromLocationName,
    string ToLocationName,
    TransferStatus Status,
    int LineCount,
    decimal TotalValue,
    DateTimeOffset? ShippedAt,
    DateTimeOffset CreatedAt);

[RequiresPermission(PermissionKeys.Inventory.Transfer)]
public sealed record BrowseTransfersQuery(
    Guid LocationId,
    TransferStatus? Status = null,
    /// <summary>Include transfers heading here as well as leaving here — the receiving end's view.</summary>
    bool IncludeInbound = true,
    int Skip = 0,
    int Take = 50) : IRequest<IReadOnlyList<TransferRowDto>>;

[RequiresPermission(PermissionKeys.Inventory.Transfer)]
public sealed record GetTransferQuery(Guid TransferId) : IRequest<Result<TransferDto>>;

public sealed record TransferDestinationDto(Guid Id, string Code, string Name);

/// <summary>
/// The other stores stock can be sent to. Excludes the one asking — a transfer to yourself is
/// refused by the domain, so offering it is only a way to earn an error.
/// </summary>
[RequiresPermission(PermissionKeys.Inventory.Transfer)]
public sealed record ListTransferDestinationsQuery(Guid ExcludeLocationId) : IRequest<IReadOnlyList<TransferDestinationDto>>;

[RequiresPermission(PermissionKeys.Inventory.Transfer)]
public sealed record CreateTransferCommand(
    Guid FromLocationId,
    Guid ToLocationId,
    string? Notes = null) : IRequest<Result<TransferDto>>;

/// <summary>Adds an item, or changes the quantity if it is already on the transfer.</summary>
[RequiresPermission(PermissionKeys.Inventory.Transfer)]
public sealed record UpsertTransferLineCommand(
    Guid TransferId,
    Guid ProductId,
    decimal Quantity) : IRequest<Result<TransferDto>>;

[RequiresPermission(PermissionKeys.Inventory.Transfer)]
public sealed record RemoveTransferLineCommand(Guid TransferId, Guid LineId) : IRequest<Result<TransferDto>>;

/// <summary>The van leaves: stock comes off the source and the transfer becomes in-transit.</summary>
[RequiresPermission(PermissionKeys.Inventory.Transfer)]
public sealed record ShipTransferCommand(Guid TransferId) : IRequest<Result<TransferDto>>;

public sealed record ReceiveTransferLine(Guid LineId, decimal Quantity);

/// <summary>
/// The box is opened. Omitting <paramref name="Lines"/> receives everything outstanding, which is
/// what happens almost every time.
/// </summary>
[RequiresPermission(PermissionKeys.Inventory.Transfer)]
public sealed record ReceiveTransferCommand(
    Guid TransferId,
    IReadOnlyList<ReceiveTransferLine>? Lines = null) : IRequest<Result<TransferDto>>;

[RequiresPermission(PermissionKeys.Inventory.Transfer)]
public sealed record CancelTransferCommand(Guid TransferId) : IRequest<Result<TransferDto>>;

/// <summary>
/// Transfers between locations (guide p.20–21).
/// <para>
/// The one genuinely awkward part is that a product row belongs to a location: <c>Product</c> is one
/// row per (LocationId, StockCode). So receiving at the destination has to find the matching row
/// there — and create it, copying the catalogue attributes across, when the item has never been
/// stocked at that store before.
/// </para>
/// </summary>
public sealed class TransferHandlers :
    IRequestHandler<BrowseTransfersQuery, IReadOnlyList<TransferRowDto>>,
    IRequestHandler<GetTransferQuery, Result<TransferDto>>,
    IRequestHandler<ListTransferDestinationsQuery, IReadOnlyList<TransferDestinationDto>>,
    IRequestHandler<CreateTransferCommand, Result<TransferDto>>,
    IRequestHandler<UpsertTransferLineCommand, Result<TransferDto>>,
    IRequestHandler<RemoveTransferLineCommand, Result<TransferDto>>,
    IRequestHandler<ShipTransferCommand, Result<TransferDto>>,
    IRequestHandler<ReceiveTransferCommand, Result<TransferDto>>,
    IRequestHandler<CancelTransferCommand, Result<TransferDto>>
{
    public static readonly Error TransferNotFound = new("transfer.not_found", "No such transfer.");
    public static readonly Error LineNotFound = new("transfer.line_not_found", "That line is not on this transfer.");
    public static readonly Error ProductNotFound = new("transfer.product_not_found", "No such item at the sending location.");
    public static readonly Error LocationNotFound = new("transfer.location_not_found", "No such location.");
    public static readonly Error InsufficientStock = new(
        "transfer.insufficient_stock",
        "There is not enough on hand at the sending location to ship that.");
    public static readonly Error NothingOutstanding = new(
        "transfer.nothing_outstanding",
        "Everything on this transfer has already been received.");
    public static readonly Error CannotCreateAtDestination = new(
        "transfer.cannot_create_at_destination",
        "This item does not exist at the receiving location, and you do not have permission to add items to the catalogue.");

    private readonly IApplicationDbContext _db;
    private readonly ISequenceGenerator _sequences;
    private readonly ICurrentUser _currentUser;
    private readonly IPosNotifier _notifier;
    private readonly IDateTime _clock;

    public TransferHandlers(
        IApplicationDbContext db,
        ISequenceGenerator sequences,
        ICurrentUser currentUser,
        IPosNotifier notifier,
        IDateTime clock)
    {
        _db = db;
        _sequences = sequences;
        _currentUser = currentUser;
        _notifier = notifier;
        _clock = clock;
    }

    public async Task<IReadOnlyList<TransferRowDto>> Handle(BrowseTransfersQuery request, CancellationToken ct)
    {
        var query = _db.StockTransfers.AsNoTracking().Where(t =>
            t.FromLocationId == request.LocationId || (request.IncludeInbound && t.ToLocationId == request.LocationId));

        if (request.Status is { } status)
        {
            query = query.Where(t => t.Status == status);
        }

        var transfers = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, 200))
            .ToListAsync(ct);

        var ids = transfers.Select(t => t.Id).ToList();

        var lineSummary = await _db.StockTransferLines.AsNoTracking()
            .Where(l => ids.Contains(l.StockTransferId))
            .GroupBy(l => l.StockTransferId)
            .Select(g => new { Id = g.Key, Count = g.Count(), Value = g.Sum(l => l.Quantity * l.UnitCost) })
            .ToDictionaryAsync(x => x.Id, x => x, ct);

        var names = await LocationNamesAsync(transfers.SelectMany(t => new[] { t.FromLocationId, t.ToLocationId }), ct);

        return transfers.Select(t =>
        {
            var summary = lineSummary.GetValueOrDefault(t.Id);

            return new TransferRowDto(
                t.Id,
                t.TransferNumber,
                names.GetValueOrDefault(t.FromLocationId, "—"),
                names.GetValueOrDefault(t.ToLocationId, "—"),
                t.Status,
                summary?.Count ?? 0,
                summary?.Value ?? 0m,
                t.ShippedAt,
                t.CreatedAt);
        }).ToList();
    }

    public async Task<Result<TransferDto>> Handle(GetTransferQuery request, CancellationToken ct)
    {
        var transfer = await _db.StockTransfers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == request.TransferId, ct);

        return transfer is null
            ? Result.Failure<TransferDto>(TransferNotFound)
            : Result.Success(await ToDtoAsync(transfer, ct));
    }

    public async Task<IReadOnlyList<TransferDestinationDto>> Handle(
        ListTransferDestinationsQuery request, CancellationToken ct)
        => await _db.Locations.AsNoTracking()
            .Where(l => l.Id != request.ExcludeLocationId)
            .OrderBy(l => l.Name)
            .Select(l => new TransferDestinationDto(l.Id, l.LegacyCode, l.Name))
            .ToListAsync(ct);

    public async Task<Result<TransferDto>> Handle(CreateTransferCommand request, CancellationToken ct)
    {
        var locations = await _db.Locations.AsNoTracking()
            .Where(l => l.Id == request.FromLocationId || l.Id == request.ToLocationId)
            .Select(l => l.Id)
            .ToListAsync(ct);

        if (!locations.Contains(request.FromLocationId) || !locations.Contains(request.ToLocationId))
        {
            return Result.Failure<TransferDto>(LocationNotFound);
        }

        var number = await _sequences.NextAsync(SequenceKind.Transfer, request.FromLocationId, ct);

        var created = StockTransfer.Create(request.FromLocationId, request.ToLocationId, number, request.Notes);

        if (created.IsFailure)
        {
            return Result.Failure<TransferDto>(created.Error);
        }

        _db.StockTransfers.Add(created.Value);
        await _db.SaveChangesAsync(ct);

        return Result.Success(await ToDtoAsync(created.Value, ct));
    }

    public async Task<Result<TransferDto>> Handle(UpsertTransferLineCommand request, CancellationToken ct)
    {
        var transfer = await _db.StockTransfers.FirstOrDefaultAsync(t => t.Id == request.TransferId, ct);

        if (transfer is null)
        {
            return Result.Failure<TransferDto>(TransferNotFound);
        }

        var editable = transfer.EnsureEditable();

        if (editable.IsFailure)
        {
            return Result.Failure<TransferDto>(editable.Error);
        }

        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(
            p => p.Id == request.ProductId && p.LocationId == transfer.FromLocationId && !p.IsDeleted, ct);

        if (product is null)
        {
            return Result.Failure<TransferDto>(ProductNotFound.With("productId", request.ProductId));
        }

        var line = await _db.StockTransferLines.FirstOrDefaultAsync(
            l => l.StockTransferId == transfer.Id && l.ProductId == request.ProductId, ct);

        if (request.Quantity <= 0m)
        {
            return Result.Failure<TransferDto>(StockTransferLine.QuantityRequired);
        }

        if (line is null)
        {
            var created = StockTransferLine.Create(
                transfer.Id, product.Id, product.StockCode, product.Name, request.Quantity, product.AvgCost);

            if (created.IsFailure)
            {
                return Result.Failure<TransferDto>(created.Error);
            }

            _db.StockTransferLines.Add(created.Value);
        }
        else
        {
            line.Quantity = request.Quantity;

            // Re-read while the transfer is still a draft: nothing has moved, so the frozen cost
            // should be current until the moment it ships.
            line.UnitCost = product.AvgCost;
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success(await ToDtoAsync(transfer, ct));
    }

    public async Task<Result<TransferDto>> Handle(RemoveTransferLineCommand request, CancellationToken ct)
    {
        var transfer = await _db.StockTransfers.FirstOrDefaultAsync(t => t.Id == request.TransferId, ct);

        if (transfer is null)
        {
            return Result.Failure<TransferDto>(TransferNotFound);
        }

        var editable = transfer.EnsureEditable();

        if (editable.IsFailure)
        {
            return Result.Failure<TransferDto>(editable.Error);
        }

        var line = await _db.StockTransferLines.FirstOrDefaultAsync(
            l => l.Id == request.LineId && l.StockTransferId == transfer.Id, ct);

        if (line is null)
        {
            return Result.Failure<TransferDto>(LineNotFound);
        }

        _db.StockTransferLines.Remove(line);
        await _db.SaveChangesAsync(ct);

        return Result.Success(await ToDtoAsync(transfer, ct));
    }

    public async Task<Result<TransferDto>> Handle(ShipTransferCommand request, CancellationToken ct)
    {
        var transfer = await _db.StockTransfers.FirstOrDefaultAsync(t => t.Id == request.TransferId, ct);

        if (transfer is null)
        {
            return Result.Failure<TransferDto>(TransferNotFound);
        }

        var lines = await _db.StockTransferLines.Where(l => l.StockTransferId == transfer.Id).ToListAsync(ct);

        var shipped = transfer.Ship(_clock.Now, lines.Count > 0);

        if (shipped.IsFailure)
        {
            return Result.Failure<TransferDto>(shipped.Error);
        }

        var productIds = lines.Select(l => l.ProductId).ToList();

        var products = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p, ct);

        // Every line is checked before any stock moves. Shipping half a transfer because line seven
        // was short leaves the van loaded with goods the system says are still on the shelf.
        foreach (var line in lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                return Result.Failure<TransferDto>(ProductNotFound.With("productId", line.ProductId));
            }

            if (product.OnHand < line.Quantity)
            {
                return Result.Failure<TransferDto>(InsufficientStock
                    .With("stockCode", product.StockCode)
                    .With("onHand", product.OnHand)
                    .With("requested", line.Quantity));
            }
        }

        foreach (var line in lines)
        {
            var product = products[line.ProductId];

            // Frozen now, not at receipt: a sale at the source between shipping and receiving would
            // otherwise change what the goods already in the van are worth.
            line.UnitCost = product.AvgCost;

            product.UpdateStockLevels(product.OnHand - line.Quantity, product.OnOrder);

            await MoveStockAsync(
                product.Id, transfer.FromLocationId, MovementType.TransferOut, -line.Quantity, line.UnitCost,
                $"Transfer {transfer.TransferNumber}", transfer.Id, ct);

            await _notifier.StockLevelChangedAsync(transfer.FromLocationId, product.Id, product.OnHand, ct);
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success(await ToDtoAsync(transfer, ct));
    }

    public async Task<Result<TransferDto>> Handle(ReceiveTransferCommand request, CancellationToken ct)
    {
        var transfer = await _db.StockTransfers.FirstOrDefaultAsync(t => t.Id == request.TransferId, ct);

        if (transfer is null)
        {
            return Result.Failure<TransferDto>(TransferNotFound);
        }

        if (transfer.Status != TransferStatus.InTransit)
        {
            return Result.Failure<TransferDto>(StockTransfer.NotInTransit);
        }

        var lines = await _db.StockTransferLines.Where(l => l.StockTransferId == transfer.Id).ToListAsync(ct);

        // No explicit list means "all of it", which is what the receiving clerk does almost every
        // time — the exception is a short delivery, and that is when they type figures in.
        var requested = request.Lines?.ToDictionary(l => l.LineId, l => l.Quantity)
            ?? lines.Where(l => l.Outstanding > 0m).ToDictionary(l => l.Id, l => l.Outstanding);

        if (requested.Count == 0 || requested.Values.All(q => q <= 0m))
        {
            return Result.Failure<TransferDto>(NothingOutstanding);
        }

        var canWriteCatalogue = _currentUser.HasPermission(PermissionKeys.Catalog.Write);

        foreach (var (lineId, quantity) in requested)
        {
            if (quantity <= 0m)
            {
                continue;
            }

            var line = lines.FirstOrDefault(l => l.Id == lineId);

            if (line is null)
            {
                return Result.Failure<TransferDto>(LineNotFound.With("lineId", lineId));
            }

            var received = line.ReceiveQuantity(quantity);

            if (received.IsFailure)
            {
                return Result.Failure<TransferDto>(received.Error.With("stockCode", line.StockCode));
            }

            var destination = await FindOrCreateDestinationAsync(transfer, line, canWriteCatalogue, ct);

            if (destination.IsFailure)
            {
                return Result.Failure<TransferDto>(destination.Error);
            }

            var product = destination.Value;

            // The destination's average cost absorbs the goods at what they cost the business. Not
            // ReceiveStock: nothing was on order here, and working the quantity off OnOrder would
            // cancel a purchase order this store has genuinely open with a supplier.
            product.ReceiveTransfer(quantity, line.UnitCost);

            await MoveStockAsync(
                product.Id, transfer.ToLocationId, MovementType.TransferIn, quantity, line.UnitCost,
                $"Transfer {transfer.TransferNumber}", transfer.Id, ct);

            await _notifier.StockLevelChangedAsync(transfer.ToLocationId, product.Id, product.OnHand, ct);
        }

        transfer.Receive(_clock.Now, lines.All(l => l.Outstanding <= 0m));

        await _db.SaveChangesAsync(ct);

        return Result.Success(await ToDtoAsync(transfer, ct));
    }

    public async Task<Result<TransferDto>> Handle(CancelTransferCommand request, CancellationToken ct)
    {
        var transfer = await _db.StockTransfers.FirstOrDefaultAsync(t => t.Id == request.TransferId, ct);

        if (transfer is null)
        {
            return Result.Failure<TransferDto>(TransferNotFound);
        }

        var cancelled = transfer.Cancel();

        if (cancelled.IsFailure)
        {
            return Result.Failure<TransferDto>(cancelled.Error);
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success(await ToDtoAsync(transfer, ct));
    }

    /// <summary>
    /// The destination's own product row, created from the source's if the item has never been
    /// stocked there.
    /// <para>
    /// Creating it is a catalogue write, so it is gated on <c>Catalog.Write</c> even though the
    /// caller already holds <c>Inventory.Transfer</c> — otherwise transferring an item would be a
    /// way to add items to a catalogue you cannot edit. Stock figures and the barcode are
    /// deliberately not copied: on-hand arrives from this transfer, and a UPC is a fact about the
    /// item that the shared code already carries.
    /// </para>
    /// </summary>
    private async Task<Result<Product>> FindOrCreateDestinationAsync(
        StockTransfer transfer, StockTransferLine line, bool canWriteCatalogue, CancellationToken ct)
    {
        var existing = await _db.Products.FirstOrDefaultAsync(
            p => p.LocationId == transfer.ToLocationId && p.StockCode == line.StockCode, ct);

        if (existing is not null)
        {
            return Result.Success(existing);
        }

        if (!canWriteCatalogue)
        {
            return Result.Failure<Product>(CannotCreateAtDestination.With("stockCode", line.StockCode));
        }

        var source = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == line.ProductId, ct);

        if (source is null)
        {
            return Result.Failure<Product>(ProductNotFound.With("productId", line.ProductId));
        }

        var created = Product.Create(
            transfer.ToLocationId, source.StockCode, source.Name, source.Type,
            source.RegularPrice, source.Tax1Applies, source.Tax2Applies);

        if (created.IsFailure)
        {
            return Result.Failure<Product>(created.Error);
        }

        var product = created.Value;

        product.UpdateDetails(source.Name, source.Description, source.Upc, source.BinLocation, source.Notes);
        product.UpdateOrdering(source.BaseStock, source.ReorderPoint, source.ReorderQty, source.CaseQty, source.ShipWeight);
        product.SetDepartment(source.DepartmentId);
        product.SetCategory(source.CategoryId);

        _db.Products.Add(product);

        return Result.Success(product);
    }

    private async Task MoveStockAsync(
        Guid productId, Guid locationId, MovementType type, decimal signedQuantity,
        decimal unitCost, string reason, Guid referenceId, CancellationToken ct)
    {
        _db.StockLedgerEntries.Add(new StockLedgerEntry
        {
            ProductId = productId,
            LocationId = locationId,
            MovementType = type,
            Quantity = signedQuantity,
            UnitCost = unitCost,
            Reason = reason,
            ReferenceType = nameof(StockTransfer),
            ReferenceId = referenceId,
            OccurredAt = _clock.Now,
            StaffId = _currentUser.StaffId,
        });

        var level = await _db.StockLevels.FirstOrDefaultAsync(
            s => s.ProductId == productId && s.VariantId == null && s.LocationId == locationId, ct);

        if (level is null)
        {
            level = StockLevel.Create(productId, null, locationId);
            _db.StockLevels.Add(level);
        }

        level.OnHand += signedQuantity;
    }

    private async Task<Dictionary<Guid, string>> LocationNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();

        return await _db.Locations.AsNoTracking()
            .Where(l => distinct.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name, ct);
    }

    private async Task<TransferDto> ToDtoAsync(StockTransfer transfer, CancellationToken ct)
    {
        var lines = await _db.StockTransferLines.AsNoTracking()
            .Where(l => l.StockTransferId == transfer.Id)
            .OrderBy(l => l.StockCode)
            .ToListAsync(ct);

        var productIds = lines.Select(l => l.ProductId).ToList();

        var onHand = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.OnHand, ct);

        var names = await LocationNamesAsync([transfer.FromLocationId, transfer.ToLocationId], ct);

        return new TransferDto(
            transfer.Id,
            transfer.TransferNumber,
            transfer.FromLocationId,
            names.GetValueOrDefault(transfer.FromLocationId, "—"),
            transfer.ToLocationId,
            names.GetValueOrDefault(transfer.ToLocationId, "—"),
            transfer.Status,
            transfer.Notes,
            transfer.ShippedAt,
            transfer.ReceivedAt,
            lines.Sum(l => l.Quantity * l.UnitCost),
            lines.Select(l => new TransferLineDto(
                l.Id,
                l.ProductId,
                l.StockCode,
                l.ProductName,
                l.Quantity,
                l.QuantityReceived,
                l.Outstanding,
                l.UnitCost,
                onHand.GetValueOrDefault(l.ProductId))).ToList());
    }
}
