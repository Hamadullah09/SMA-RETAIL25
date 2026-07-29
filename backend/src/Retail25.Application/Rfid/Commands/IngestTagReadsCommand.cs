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
public sealed record IngestTagReadsCommand(Guid StationId, IReadOnlyList<TagRead> Tags) : IRequest<Result<RfidBatchResult>>;

public sealed class IngestTagReadsHandler : IRequestHandler<IngestTagReadsCommand, Result<RfidBatchResult>>
{
    public static readonly Error NoActiveCart = new("cart.none_active", "No sale is open at this till, so the tags were not applied.");

    private readonly ICartStore _store;
    private readonly ISender _sender;
    private readonly IPosNotifier _notifier;

    public IngestTagReadsHandler(ICartStore store, ISender sender, IPosNotifier notifier)
    {
        _store = store;
        _sender = sender;
        _notifier = notifier;
    }

    public async Task<Result<RfidBatchResult>> Handle(IngestTagReadsCommand request, CancellationToken ct)
    {
        var snapshot = await _store.GetByStationAsync(request.StationId, ct);

        if (snapshot is not { Cart.IsActive: true })
        {
            // Session gating (doc 06 §2 control 4): with no sale open, reads are noise. They are
            // still surfaced so a cashier can see the reader is working.
            foreach (var tag in request.Tags)
            {
                await _notifier.CartLineRejectedAsync(request.StationId, tag.Epc, NoActiveCart.Code, NoActiveCart.Message, ct);
            }

            return Result.Success(new RfidBatchResult(
                null,
                [],
                request.Tags.Select(t => new RejectedTag(t.Epc, NoActiveCart.Code, NoActiveCart.Message)).ToList(),
                request.Tags.Count));
        }

        return await _sender.Send(new AddRfidBatchCommand(snapshot.Cart.Id, request.Tags), ct);
    }
}

/// <summary>
/// Associates an unmapped tag with an item — the supervisor's answer to an <c>epc.unknown</c> row in
/// the live feed, and the same operation goods-in uses to commission stock (doc 06 §1).
/// </summary>
[RequiresPermission(PermissionKeys.Inventory.CommissionTags)]
public sealed record CommissionTagCommand(
    string Epc,
    Guid ProductId,
    Guid LocationId,
    Guid? VariantId = null,
    string? SerialNumber = null) : IRequest<Result<Guid>>;

public sealed class CommissionTagHandler : IRequestHandler<CommissionTagCommand, Result<Guid>>
{
    public static readonly Error AlreadyMapped = new("epc.already_mapped", "That tag is already associated with an item.");
    public static readonly Error ProductNotFound = new("product.not_found", "No such item.");

    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public CommissionTagHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<Guid>> Handle(CommissionTagCommand request, CancellationToken ct)
    {
        var epc = request.Epc.Trim().ToUpperInvariant();

        if (await _db.SerializedUnits.AnyAsync(u => u.Epc == epc, ct))
        {
            return Result.Failure<Guid>(AlreadyMapped.With("epc", epc));
        }

        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.ProductId && !p.IsDeleted, ct);

        if (product is null)
        {
            return Result.Failure<Guid>(ProductNotFound.With("productId", request.ProductId));
        }

        var created = SerializedUnit.Create(request.ProductId, request.LocationId, request.SerialNumber, epc, _clock.Now);
        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        var unit = created.Value;

        // Commissioning is the transition that makes a tag sellable; a provisioned tag is not.
        var commissioned = unit.Commission();
        if (commissioned.IsFailure)
        {
            return Result.Failure<Guid>(commissioned.Error);
        }

        _db.SerializedUnits.Add(unit);
        await _db.SaveChangesAsync(ct);

        return Result.Success(unit.Id);
    }
}
