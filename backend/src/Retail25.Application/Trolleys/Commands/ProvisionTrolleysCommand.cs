using System.Globalization;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Application.Trolleys.Services;
using Retail25.Domain.Common;

namespace Retail25.Application.Trolleys.Commands;

public sealed record ProvisionTrolleysResult(int Created, int AlreadyThere, int Weighed, int Failed);

/// <summary>
/// Lays down the whole self-checkout block at once.
/// <para>
/// Counters were only ever created on demand — the first shopper to be issued 317 brought 317 into
/// existence — which is fine for running a shop and useless for setting one up. A shop with two
/// hundred trolleys wants them all listed, with their weights, before the doors open, not after two
/// hundred shoppers have each created one.
/// </para>
/// <para>
/// Idempotent: run it twice and the second run creates nothing. That matters because the obvious
/// thing to do when a page looks wrong is press the button again.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Settings.Write)]
public sealed record ProvisionTrolleysCommand(long? LocationId = null) : IRequest<Result<ProvisionTrolleysResult>>;

public sealed class ProvisionTrolleysHandler
    : IRequestHandler<ProvisionTrolleysCommand, Result<ProvisionTrolleysResult>>
{
    private readonly TrolleyAllocator _allocator;
    private readonly TrolleyOptions _options;
    private readonly IApplicationDbContext _db;

    public ProvisionTrolleysHandler(
        TrolleyAllocator allocator,
        IOptions<TrolleyOptions> options,
        IApplicationDbContext db)
    {
        _allocator = allocator;
        _options = options.Value;
        _db = db;
    }

    public async Task<Result<ProvisionTrolleysResult>> Handle(
        ProvisionTrolleysCommand request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var first = _options.MinStationCode;
        var last = _options.MaxStationCode;

        var before = await _db.Trolleys.CountAsync(ct);
        var weighed = 0;
        var failed = 0;

        for (var code = first; code <= last; code++)
        {
            var spread = SpreadFor(code, first, last);

            // Handed in rather than applied afterwards.
            //
            // The allocator seeds a newly created counter with the fleet default, so by the time it
            // comes back its weight is never null and a "only set it if it is missing" guard never
            // fires. The first run of this proved it: two hundred counters created, every one of
            // them reading 2.35, and the summary honestly reporting that it had weighed none of
            // them. Passing the figure into the creation is what makes it land.
            var trolley = await _allocator.EnsureAsync(
                code.ToString(CultureInfo.InvariantCulture),
                request.LocationId,
                ct,
                spread);

            if (trolley.IsFailure)
            {
                failed++;
                continue;
            }

            // For counters that already existed: filled in only where nothing is recorded, and where
            // what is recorded is the fleet default — that is the stand-in this is meant to replace,
            // not a measurement. A figure somebody actually weighed is left alone, because
            // overwriting a measurement with an assumption is the one thing this must not do.
            var current = trolley.Value.TareWeightKg;

            if (current is null || current == _options.DefaultTareKg)
            {
                var applied = trolley.Value.SetTareWeight(spread);

                if (applied.IsSuccess)
                {
                    weighed++;
                }
            }

        }

        await _db.SaveChangesAsync(ct);

        var after = await _db.Trolleys.CountAsync(ct);

        return Result.Success(new ProvisionTrolleysResult(after - before, before, weighed, failed));
    }

    /// <summary>
    /// A weight for one counter, spread evenly across the fleet's band.
    /// <para>
    /// Assigned, not measured, and the screen says so — nobody has weighed two hundred trolleys.
    /// Spread rather than identical because the point of the exercise is load balancing, and two
    /// hundred trolleys all claiming exactly 2.35 kg would tell whatever balances them nothing at
    /// all.
    /// </para>
    /// <para>
    /// Deterministic rather than random: the same counter gets the same figure on every run, so a
    /// re-provision does not quietly reshuffle the fleet, and the numbers can be reasoned about. To
    /// the gram, since that is the precision the column keeps.
    /// </para>
    /// </summary>
    private decimal SpreadFor(int code, int first, int last)
    {
        if (last <= first)
        {
            return _options.DefaultTareKg;
        }

        var span = _options.MaxTareKg - _options.MinTareKg;
        var position = (decimal)(code - first) / (last - first);

        return decimal.Round(_options.MinTareKg + (span * position), 3, MidpointRounding.AwayFromZero);
    }
}
