using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Application.Inventory;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;
using Retail25.Domain.Inventory;

namespace Retail25.Application.Rfid.Commands;

public sealed record RetireTagsResult(int Retired, int LeftAlone, int StockAdjusted);

/// <summary>
/// Withdraws every tag at a location from service, keeping the units and their history.
/// <para>
/// For re-tagging a shop: the physical stock is unchanged and the labels on it are being replaced, so
/// the old EPCs must stop reading as sellable without the record of what they did being destroyed.
/// That is why this exists rather than a delete — a sold unit is what a receipt points at, and
/// removing it would leave the receipt referring to nothing.
/// </para>
/// <para>
/// Sold and transferred units are left exactly as they are. They already left the shop; rewriting
/// their state would rewrite history rather than end it.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Inventory.CommissionTags)]
public sealed record RetireTagsCommand(long LocationId, bool DryRun = false) : IRequest<Result<RetireTagsResult>>;

public sealed class RetireTagsHandler : IRequestHandler<RetireTagsCommand, Result<RetireTagsResult>>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public RetireTagsHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<RetireTagsResult>> Handle(RetireTagsCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var units = await _db.SerializedUnits
            .Where(u => u.LocationId == request.LocationId)
            .ToListAsync(ct);

        var retired = 0;
        var leftAlone = 0;
        var adjusted = 0;

        foreach (var unit in units)
        {
            // Whether this unit was counted as stock, asked before the state changes.
            var wasOnHand = unit.State is SerializedUnitState.InStock
                or SerializedUnitState.InCart
                or SerializedUnitState.Returned;

            if (unit.Retire().IsFailure)
            {
                leftAlone++;
                continue;
            }

            retired++;

            if (!wasOnHand || request.DryRun)
            {
                continue;
            }

            // A retired tag cannot be sold, so the unit it named is no longer sellable stock and the
            // ledger has to say so. Leaving it would make on-hand claim items that nothing can ring
            // up — the same untruth as the negative stock this system had before, in the other
            // direction. Written as a movement rather than by editing the snapshot, because on-hand
            // is derived and this is a thing that happened on a day.
            await StockMovements.ApplyAsync(
                _db,
                unit.ProductId,
                unit.VariantId,
                unit.LocationId,
                quantity: -1m,
                unitCost: 0m,
                MovementType.Adjustment,
                reason: "Tag retired for re-tagging",
                occurredAt: _clock.Now,
                staffId: null,
                ct: ct);

            adjusted++;
        }

        if (request.DryRun)
        {
            return Result.Success(new RetireTagsResult(retired, leftAlone, adjusted));
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success(new RetireTagsResult(retired, leftAlone, adjusted));
    }
}
