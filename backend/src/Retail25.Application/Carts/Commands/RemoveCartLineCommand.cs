using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Carts.Dtos;
using Retail25.Application.Carts.Services;
using Retail25.Application.Common;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;

namespace Retail25.Application.Carts.Commands;

/// <summary>Deletes one line — the legacy F6 "delete last line" and any click on the cart list (guide p.10).</summary>
[RequiresPermission(PermissionKeys.Pos.Sell)]
public sealed record RemoveCartLineCommand(long CartId, long LineId) : IRequest<Result<CartDto>>;

/// <summary>Empties the cart without abandoning it, so the cashier keeps the same sale open.</summary>
[RequiresPermission(PermissionKeys.Pos.Sell)]
public sealed record ClearCartCommand(long CartId) : IRequest<Result<CartDto>>;

public sealed class RemoveCartLineHandler
    : IRequestHandler<RemoveCartLineCommand, Result<CartDto>>,
      IRequestHandler<ClearCartCommand, Result<CartDto>>
{
    private readonly CartWorkflow _workflow;
    private readonly IApplicationDbContext _db;
    private readonly ITagDebouncer _debouncer;

    public RemoveCartLineHandler(CartWorkflow workflow, IApplicationDbContext db, ITagDebouncer debouncer)
    {
        _workflow = workflow;
        _db = db;
        _debouncer = debouncer;
    }

    public Task<Result<CartDto>> Handle(RemoveCartLineCommand request, CancellationToken ct)
        => _workflow.MutateAsync(request.CartId, async (snapshot, _, token) =>
        {
            var line = snapshot.Lines.FirstOrDefault(l => l.Id == request.LineId);
            if (line is null)
            {
                return Result.Failure(UpdateCartLineHandler.LineNotFound.With("lineId", request.LineId));
            }

            await ReleaseUnitAsync(line.SerializedUnitId, line.Epc, snapshot.Cart.StationId, token);
            snapshot.Lines.Remove(line);
            return Result.Success();
        }, ct);

    public Task<Result<CartDto>> Handle(ClearCartCommand request, CancellationToken ct)
        => _workflow.MutateAsync(request.CartId, async (snapshot, _, token) =>
        {
            foreach (var line in snapshot.Lines)
            {
                await ReleaseUnitAsync(line.SerializedUnitId, line.Epc, snapshot.Cart.StationId, token);
            }

            snapshot.Lines.Clear();
            snapshot.Adjustments.Clear();
            return Result.Success();
        }, ct);

    /// <summary>
    /// A removed line hands its tag back: the unit returns to stock and the Redis claim is dropped so
    /// the next station to read it can sell it (doc 06 §1).
    /// </summary>
    private async Task ReleaseUnitAsync(long? unitId, string? epc, long stationId, CancellationToken ct)
    {
        if (unitId is { } id)
        {
            var unit = await _db.SerializedUnits.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (unit is { State: SerializedUnitState.InCart })
            {
                unit.ReleaseFromCart();
                await _db.SaveChangesAsync(ct);
            }
        }

        if (!string.IsNullOrWhiteSpace(epc))
        {
            await _debouncer.ReleaseAsync(epc, stationId, ct);
        }
    }
}
