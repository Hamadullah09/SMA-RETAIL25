using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Trolleys;

namespace Retail25.Application.Trolleys.Queries;

/// <summary>One self-checkout trolley, as the settings screen shows it.</summary>
public sealed record TrolleyRow(
    long Id,
    string Code,
    string? Label,
    long StationId,
    bool IsActive,
    decimal? TareWeightKg);

/// <summary>
/// The trolleys at a location, in code order.
/// <para>
/// Read under settings rather than catalogue permissions: a trolley is a fixture of the shop, like a
/// till or a printer, and the people who weigh them are the people who set the shop up.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Settings.Read)]
public sealed record ListTrolleysQuery(long LocationId) : IRequest<Result<IReadOnlyList<TrolleyRow>>>;

/// <summary>
/// Records what one trolley weighs empty, or clears it back to unknown.
/// <para>
/// Null clears deliberately: a trolley whose wheels have been replaced no longer weighs what the
/// sticker said, and an unknown tare is safer than a stale one for anything doing arithmetic with it.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Settings.Write)]
public sealed record SetTrolleyTareCommand(long TrolleyId, decimal? TareWeightKg) : IRequest<Result>;

public sealed class TrolleyAdminHandlers
    : IRequestHandler<ListTrolleysQuery, Result<IReadOnlyList<TrolleyRow>>>,
      IRequestHandler<SetTrolleyTareCommand, Result>
{
    public static readonly Error NotFound = new("trolley.not_found", "No such trolley.");

    private readonly IApplicationDbContext _db;

    public TrolleyAdminHandlers(IApplicationDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<TrolleyRow>>> Handle(ListTrolleysQuery request, CancellationToken ct)
    {
        var rows = await _db.Trolleys
            .AsNoTracking()
            .Where(t => t.LocationId == request.LocationId)
            .OrderBy(t => t.Code)
            .Select(t => new TrolleyRow(t.Id, t.Code, t.Label, t.StationId, t.IsActive, t.TareWeightKg))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<TrolleyRow>>(rows);
    }

    public async Task<Result> Handle(SetTrolleyTareCommand request, CancellationToken ct)
    {
        var trolley = await _db.Trolleys.FirstOrDefaultAsync(t => t.Id == request.TrolleyId, ct);

        if (trolley is null)
        {
            return Result.Failure(NotFound.With("trolleyId", request.TrolleyId));
        }

        var applied = trolley.SetTareWeight(request.TareWeightKg);

        if (applied.IsFailure)
        {
            return applied;
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
