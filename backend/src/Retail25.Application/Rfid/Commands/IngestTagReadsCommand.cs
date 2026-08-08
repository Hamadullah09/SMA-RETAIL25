using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Contracts.Terminals;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;
using Retail25.Domain.Sales;

namespace Retail25.Application.Rfid.Commands;

/// <summary>
/// The agent's entry point: a batch of tags arriving from a station, with no cart id attached.
/// <para>
/// The agent does not know or care which cart is open — that is server state. This command finds the
/// station's active cart and forwards to <see cref="AddRfidBatchCommand"/>, or reports the reads as
/// out-of-session so the feed can still show that the reader is alive.
/// </para>
/// </summary>
public sealed record IngestTagReadsCommand(long StationId, IReadOnlyList<TagRead> Tags) : IRequest<Result<RfidBatchResult>>;

public sealed class IngestTagReadsHandler : IRequestHandler<IngestTagReadsCommand, Result<RfidBatchResult>>
{
    public static readonly Error NoActiveCart = new("cart.none_active", "No sale is open at this till, so the tags were not applied.");

    private readonly ICartStore _store;
    private readonly ISender _sender;
    private readonly IPosNotifier _notifier;
    private readonly TagObservationPublisher _feed;

    public IngestTagReadsHandler(
        ICartStore store,
        ISender sender,
        IPosNotifier notifier,
        TagObservationPublisher feed)
    {
        _store = store;
        _sender = sender;
        _notifier = notifier;
        _feed = feed;
    }

    public async Task<Result<RfidBatchResult>> Handle(IngestTagReadsCommand request, CancellationToken ct)
    {
        // The read feed first, and unconditionally. What is in front of the antenna is worth showing
        // whether or not a sale is open — a goods-in bench and a stock count have no cart at all —
        // and this is also where the batch is debounced, so everything below sees distinct tags.
        var distinct = await _feed.PublishAsync(request.StationId, request.Tags, ct);

        if (distinct.Count == 0)
        {
            // Every read folded into a window already in flight. Nothing new happened.
            return Result.Success(new RfidBatchResult(null, [], [], request.Tags.Count));
        }

        var snapshot = await _store.GetByStationAsync(request.StationId, ct);

        if (snapshot is not { Cart.IsActive: true })
        {
            // Session gating (doc 06 §2 control 4): with no sale open, reads are noise. They are
            // still surfaced so a cashier can see the reader is working.
            //
            // Over the debounced list, not the raw one. A reader looking at a full rail publishes the
            // same tags many times a second, and one rejection frame per raw read was a self-inflicted
            // flood on the busiest possible path.
            foreach (var tag in distinct)
            {
                await _notifier.CartLineRejectedAsync(request.StationId, tag.Epc, NoActiveCart.Code, NoActiveCart.Message, ct);
            }

            return Result.Success(new RfidBatchResult(
                null,
                [],
                distinct.Select(t => new RejectedTag(t.Epc, NoActiveCart.Code, NoActiveCart.Message)).ToList(),
                request.Tags.Count));
        }

        return await _sender.Send(new AddRfidBatchCommand(snapshot.Cart.Id, distinct), ct);
    }
}

/// <summary>
/// Associates an unmapped tag with an item — the supervisor's answer to an <c>epc.unknown</c> row in
/// the live feed, and the same operation goods-in uses to commission stock (doc 06 §1).
/// </summary>
[RequiresPermission(PermissionKeys.Inventory.CommissionTags)]
public sealed record CommissionTagCommand(
    string Epc,
    long ProductId,
    long LocationId,
    long? VariantId = null,
    string? SerialNumber = null) : IRequest<Result<long>>;

public sealed class CommissionTagHandler : IRequestHandler<CommissionTagCommand, Result<long>>
{
    public static readonly Error AlreadyMapped = new("epc.already_mapped", "That tag is already associated with an item.");
    public static readonly Error ProductNotFound = new("product.not_found", "No such item.");

    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;
    private readonly TagStreamRegistry _tagStreams;

    public CommissionTagHandler(IApplicationDbContext db, IDateTime clock, TagStreamRegistry tagStreams)
    {
        _db = db;
        _clock = clock;
        _tagStreams = tagStreams;
    }

    public async Task<Result<long>> Handle(CommissionTagCommand request, CancellationToken ct)
    {
        var epc = request.Epc.Trim().ToUpperInvariant();

        if (await _db.SerializedUnits.AnyAsync(u => u.Epc == epc, ct))
        {
            return Result.Failure<long>(AlreadyMapped.With("epc", epc));
        }

        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.ProductId && !p.IsDeleted, ct);

        if (product is null)
        {
            return Result.Failure<long>(ProductNotFound.With("productId", request.ProductId));
        }

        var created = SerializedUnit.Create(request.ProductId, request.LocationId, request.SerialNumber, epc, _clock.Now);
        if (created.IsFailure)
        {
            return Result.Failure<long>(created.Error);
        }

        var unit = created.Value;

        // Commissioning is the transition that makes a tag sellable; a provisioned tag is not.
        var commissioned = unit.Commission();
        if (commissioned.IsFailure)
        {
            return Result.Failure<long>(commissioned.Error);
        }

        _db.SerializedUnits.Add(unit);
        await _db.SaveChangesAsync(ct);

        // The read feed caches misses as well as hits, so it has to be told the moment a tag stops
        // being unknown — otherwise every till keeps showing "Not recognised" for a tag a supervisor
        // has just mapped, indefinitely.
        _tagStreams.ForgetCatalogue(epc);

        return Result.Success(unit.Id);
    }
}

/// <summary>
/// Points an already-commissioned tag at a different item.
/// <para>
/// Tags get applied to the wrong thing at goods-in, and pre-encoded label rolls get reused when a
/// line is discontinued. Without this the remedy is binning the tag, which for a shop holding a few
/// hundred of them is a real cost — and the same permission as commissioning covers it, because it
/// is the same decision made twice.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Catalog.Write)]
public sealed record ReassignTagCommand(string Epc, long ProductId, long? VariantId = null)
    : IRequest<Result<long>>;

public sealed class ReassignTagHandler : IRequestHandler<ReassignTagCommand, Result<long>>
{
    public static readonly Error NotFound = new("epc.unknown", "That tag is not associated with anything yet.");

    private readonly IApplicationDbContext _db;
    private readonly TagStreamRegistry _tagStreams;

    public ReassignTagHandler(IApplicationDbContext db, TagStreamRegistry tagStreams)
    {
        _db = db;
        _tagStreams = tagStreams;
    }

    public async Task<Result<long>> Handle(ReassignTagCommand request, CancellationToken ct)
    {
        var epc = request.Epc.Trim().ToUpperInvariant();

        var unit = await _db.SerializedUnits.FirstOrDefaultAsync(u => u.Epc == epc, ct);

        if (unit is null)
        {
            return Result.Failure<long>(NotFound.With("epc", epc));
        }

        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.ProductId && !p.IsDeleted, ct);

        if (product is null)
        {
            return Result.Failure<long>(CommissionTagHandler.ProductNotFound.With("productId", request.ProductId));
        }

        var moved = unit.ReassignTo(request.ProductId, request.VariantId);
        if (moved.IsFailure)
        {
            return Result.Failure<long>(moved.Error);
        }

        await _db.SaveChangesAsync(ct);

        // The feed caches what a tag resolved to, so a till would otherwise keep announcing the old
        // item — by name, on screen, to a cashier holding the new one.
        _tagStreams.ForgetCatalogue(epc);

        return Result.Success(unit.Id);
    }
}
