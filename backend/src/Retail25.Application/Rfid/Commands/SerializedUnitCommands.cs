using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;

namespace Retail25.Application.Rfid.Commands;

public sealed record SerializedUnitDto(
    Guid Id,
    string? SerialNumber,
    string? Epc,
    SerializedUnitState State,
    Guid? VariantId,
    string? VariantLabel,
    DateTimeOffset ReceivedOn,
    DateTimeOffset? LastSeenAt);

/// <summary>
/// The units a cashier can pick from when a serialized item is rung by stock code (guide p.42).
/// <para>
/// A serialized product is not one item, it is N distinct physical things, and which one leaves the
/// shop matters for warranty, recall and theft. So ringing the parent code opens a picker rather than
/// guessing — and the picker only ever offers units that are actually in stock at this location.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Pos.Sell)]
public sealed record ListAvailableUnitsQuery(Guid ProductId, Guid LocationId, int Take = 50)
    : IRequest<IReadOnlyList<SerializedUnitDto>>;

/// <summary>
/// Commissions a batch of tags at goods receipt (doc 06 §1).
/// <para>
/// This is where most EPCs enter the system. Doing it one call per tag would make receiving a
/// thousand-unit delivery a thousand round trips, so the batch is the unit of work — and each tag
/// reports its own outcome, because a delivery where three labels were already used elsewhere should
/// commission the other nine hundred and ninety-seven rather than fail wholesale.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Inventory.CommissionTags)]
public sealed record CommissionTagBatchCommand(
    Guid ProductId,
    Guid LocationId,
    IReadOnlyList<string> Epcs,
    Guid? VariantId = null) : IRequest<Result<CommissionBatchResult>>;

public sealed record CommissionedTag(string Epc, bool Succeeded, string? Reason);

public sealed record CommissionBatchResult(int Commissioned, IReadOnlyList<CommissionedTag> Results);

public sealed class SerializedUnitHandlers
    : IRequestHandler<ListAvailableUnitsQuery, IReadOnlyList<SerializedUnitDto>>,
      IRequestHandler<CommissionTagBatchCommand, Result<CommissionBatchResult>>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;
    private readonly TagStreamRegistry _tagStreams;

    public SerializedUnitHandlers(IApplicationDbContext db, IDateTime clock, TagStreamRegistry tagStreams)
    {
        _db = db;
        _clock = clock;
        _tagStreams = tagStreams;
    }

    public async Task<IReadOnlyList<SerializedUnitDto>> Handle(ListAvailableUnitsQuery request, CancellationToken ct)
    {
        var units = await _db.SerializedUnits.AsNoTracking()
            .Where(u => u.ProductId == request.ProductId
                        && u.LocationId == request.LocationId
                        && u.State == SerializedUnitState.InStock)
            .OrderBy(u => u.ReceivedOn)
            .Take(Math.Clamp(request.Take, 1, 500))
            .ToListAsync(ct);

        if (units.Count == 0)
        {
            return [];
        }

        var variantIds = units.Where(u => u.VariantId.HasValue).Select(u => u.VariantId!.Value).Distinct().ToList();
        var variants = variantIds.Count == 0
            ? []
            : await _db.ProductVariants.AsNoTracking()
                .Where(v => variantIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, ct);

        return units.Select(u => new SerializedUnitDto(
            u.Id,
            u.SerialNumber,
            u.Epc,
            u.State,
            u.VariantId,
            u.VariantId is { } id && variants.TryGetValue(id, out var variant) ? Describe(variant) : null,
            u.ReceivedOn,
            u.LastSeenAt)).ToList();
    }

    public async Task<Result<CommissionBatchResult>> Handle(CommissionTagBatchCommand request, CancellationToken ct)
    {
        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.ProductId && !p.IsDeleted, ct);

        if (product is null)
        {
            return Result.Failure<CommissionBatchResult>(
                CommissionTagHandler.ProductNotFound.With("productId", request.ProductId));
        }

        var epcs = (request.Epcs ?? [])
            .Select(e => e.Trim().ToUpperInvariant())
            .Where(e => e.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (epcs.Count == 0)
        {
            return Result.Success(new CommissionBatchResult(0, []));
        }

        // One query for every tag in the batch rather than one per tag: at a thousand labels the
        // difference is the gap between a receipt that takes a second and one that takes a minute.
        var taken = await _db.SerializedUnits.AsNoTracking()
            .Where(u => u.Epc != null && epcs.Contains(u.Epc))
            .Select(u => u.Epc!)
            .ToListAsync(ct);

        var alreadyMapped = new HashSet<string>(taken, StringComparer.Ordinal);
        var results = new List<CommissionedTag>(epcs.Count);
        var commissioned = 0;

        foreach (var epc in epcs)
        {
            if (alreadyMapped.Contains(epc))
            {
                results.Add(new CommissionedTag(epc, false, CommissionTagHandler.AlreadyMapped.Code));
                continue;
            }

            var created = SerializedUnit.Create(request.ProductId, request.LocationId, null, epc, _clock.Now);
            if (created.IsFailure)
            {
                results.Add(new CommissionedTag(epc, false, created.Error.Code));
                continue;
            }

            var unit = created.Value;

            // Provisioned is not sellable; commissioning is the transition that makes it so.
            var transition = unit.Commission();
            if (transition.IsFailure)
            {
                results.Add(new CommissionedTag(epc, false, transition.Error.Code));
                continue;
            }

            if (request.VariantId is { } variantId)
            {
                unit.AssignVariant(variantId);
            }

            _db.SerializedUnits.Add(unit);
            results.Add(new CommissionedTag(epc, true, null));
            commissioned++;
        }

        await _db.SaveChangesAsync(ct);

        // The read feed caches what an EPC resolves to, misses included — a shop always has tags that
        // will never resolve, and those are the ones that would otherwise query the database hardest.
        // Commissioning is precisely the moment a miss stops being true, so the cache has to be told.
        //
        // Without this the tags a supervisor has just mapped keep reading "Not recognised" on every
        // till, indefinitely, while the database says otherwise.
        foreach (var tag in results.Where(r => r.Succeeded))
        {
            _tagStreams.ForgetCatalogue(tag.Epc);
        }

        return Result.Success(new CommissionBatchResult(commissioned, results));
    }

    private static string Describe(ProductVariant variant)
        => string.Join(" / ", new[] { variant.Dim1Value, variant.Dim2Value, variant.Dim3Value }
            .Where(v => !string.IsNullOrWhiteSpace(v)));
}
