using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Contracts.Terminals;
using Retail25.Domain.Common;
using Retail25.Domain.Terminals;

namespace Retail25.Application.Terminals;

public sealed record PeripheralStatusDto(
    long StationId,
    string? AgentVersion,
    bool ReaderOnline,
    bool PrinterOnline,
    bool ScaleOnline,
    bool DrawerOnline,
    bool PoleDisplayOnline,
    int ReadRate,
    DateTimeOffset ReportedAt);

/// <summary>An agent checking in. Also how the UI learns that a station's hardware is alive.</summary>
public sealed record ReportAgentStatusCommand(
    long StationId,
    string? AgentVersion,
    bool ReaderOnline,
    bool PrinterOnline,
    bool ScaleOnline,
    bool DrawerOnline,
    bool PoleDisplayOnline,
    int ReadRate) : IRequest<Result>;

/// <summary>The device profile bundle an agent pulls on connect (doc 06 §7).</summary>
[RequiresPermission(PermissionKeys.Terminals.Read)]
public sealed record GetTerminalProfileQuery(long StationId) : IRequest<Result<TerminalProfileContract>>;

/// <summary>Changes how hard the reader is working — off, on demand, or continuous (doc 06 §5).</summary>
[RequiresPermission(PermissionKeys.Terminals.Operate)]
public sealed record SetReaderModeCommand(long StationId, Domain.Terminals.ReaderMode Mode) : IRequest<Result>;

/// <summary>
/// Pops the drawer from the back office or the till UI. Routed through the server rather than the
/// browser's loopback call so the pop is permission-checked and lands in the drawer ledger.
/// </summary>
[RequiresPermission(PermissionKeys.Drawer.Pop)]
public sealed record OpenStationDrawerCommand(long StationId) : IRequest<Result>;

[RequiresPermission(PermissionKeys.Terminals.Operate)]
public sealed record RequestWeightCommand(long StationId) : IRequest<Result>;

[RequiresPermission(PermissionKeys.Terminals.Operate)]
public sealed record ZeroScaleCommand(long StationId) : IRequest<Result>;

/// <summary>Line 1 and line 2 of the customer-facing display (guide p.80â€“81).</summary>
[RequiresPermission(PermissionKeys.Terminals.Operate)]
public sealed record DisplayOnPoleCommand(long StationId, string Line1, string Line2) : IRequest<Result>;

/// <summary>A weight the agent read back, forwarded to whichever browser is driving the till.</summary>
public sealed record ReportWeightCommand(long StationId, decimal Value, string Unit, bool Stable) : IRequest<Result>;

public sealed class TerminalHandlers
    : IRequestHandler<ReportAgentStatusCommand, Result>,
      IRequestHandler<GetTerminalProfileQuery, Result<TerminalProfileContract>>,
      IRequestHandler<SetReaderModeCommand, Result>,
      IRequestHandler<OpenStationDrawerCommand, Result>,
      IRequestHandler<RequestWeightCommand, Result>,
      IRequestHandler<ZeroScaleCommand, Result>,
      IRequestHandler<DisplayOnPoleCommand, Result>,
      IRequestHandler<ReportWeightCommand, Result>
{
    private readonly IApplicationDbContext _db;
    private readonly IPosNotifier _notifier;
    private readonly ITerminalNotifier _terminals;
    private readonly IDateTime _clock;
    private readonly Rfid.TagObservationPublisher _readerFeed;

    public TerminalHandlers(
        IApplicationDbContext db,
        IPosNotifier notifier,
        ITerminalNotifier terminals,
        IDateTime clock,
        Rfid.TagObservationPublisher readerFeed)
    {
        _db = db;
        _notifier = notifier;
        _terminals = terminals;
        _clock = clock;
        _readerFeed = readerFeed;
    }

    public async Task<Result> Handle(ReportAgentStatusCommand request, CancellationToken ct)
    {
        var station = await _db.Stations.FirstOrDefaultAsync(s => s.Id == request.StationId, ct);
        if (station is null)
        {
            return Result.Failure(PosContextLoaderErrors.StationNotFound.With("stationId", request.StationId));
        }

        station.Heartbeat(request.AgentVersion, _clock.Now);
        await _db.SaveChangesAsync(ct);

        var status = new PeripheralStatusDto(
            request.StationId,
            request.AgentVersion,
            request.ReaderOnline,
            request.PrinterOnline,
            request.ScaleOnline,
            request.DrawerOnline,
            request.PoleDisplayOnline,
            request.ReadRate,
            _clock.Now);

        await _notifier.PeripheralStatusAsync(request.StationId, status, ct);
        await _notifier.TagStreamStatusAsync(request.StationId, request.ReaderOnline, request.ReadRate, ct);

        // The reader feed's own status, which until now nothing published at all.
        //
        // PublishStatusAsync existed and was never called, so the tag reader panel's status was
        // permanently null. Everything it displays degraded to a default: the read rate showed an
        // em dash forever, and the panel reported "Reading" whenever its hub was up — including
        // when the antenna was switched off. A reader that stops mid-shift is exactly what that
        // panel is for, and it could not have told anyone.
        //
        // The heartbeat is the right place because it is the only moment the server hears from the
        // agent about hardware, it already carries both facts, and it arrives every few seconds, so
        // a panel opened later is correct within one beat rather than waiting for a tag to be read.
        await _readerFeed.PublishStatusAsync(
            request.StationId,
            request.ReaderOnline,
            request.ReadRate,
            station.ReaderMode.ToString(),
            ct: ct);

        return Result.Success();
    }

    public async Task<Result<TerminalProfileContract>> Handle(GetTerminalProfileQuery request, CancellationToken ct)
    {
        var station = await _db.Stations.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.StationId, ct);
        if (station is null)
        {
            return Result.Failure<TerminalProfileContract>(
                PosContextLoaderErrors.StationNotFound.With("stationId", request.StationId));
        }

        var reader = await ResolveAsync(_db.ReaderProfiles, station.ReaderProfileId, station, ct);
        var printer = await ResolveAsync(_db.PrinterProfiles, station.PrinterProfileId, station, ct);
        var scale = await ResolveAsync(_db.ScaleProfiles, station.ScaleProfileId, station, ct);
        var pole = await ResolveAsync(_db.PoleDisplayProfiles, station.PoleDisplayProfileId, station, ct);

        return Result.Success(new TerminalProfileContract(
            station.Id,
            station.StationCode,
            (Contracts.Terminals.ReaderMode)station.ReaderMode,
            ToContract(reader),
            ToContract(printer),
            ToContract(scale),
            ToContract(pole)));
    }

    public async Task<Result> Handle(SetReaderModeCommand request, CancellationToken ct)
    {
        var station = await _db.Stations.FirstOrDefaultAsync(s => s.Id == request.StationId, ct);
        if (station is null)
        {
            return Result.Failure(PosContextLoaderErrors.StationNotFound.With("stationId", request.StationId));
        }

        station.SetReaderMode(request.Mode);
        await _db.SaveChangesAsync(ct);
        await _terminals.SetReaderModeAsync(request.StationId, request.Mode.ToString(), ct);

        return Result.Success();
    }

    public async Task<Result> Handle(OpenStationDrawerCommand request, CancellationToken ct)
    {
        await _terminals.OpenDrawerAsync(request.StationId, ct);
        return Result.Success();
    }

    public async Task<Result> Handle(RequestWeightCommand request, CancellationToken ct)
    {
        await _terminals.RequestWeightAsync(request.StationId, ct);
        return Result.Success();
    }

    public async Task<Result> Handle(ZeroScaleCommand request, CancellationToken ct)
    {
        await _terminals.ZeroScaleAsync(request.StationId, ct);
        return Result.Success();
    }

    public async Task<Result> Handle(DisplayOnPoleCommand request, CancellationToken ct)
    {
        await _terminals.DisplayPoleAsync(request.StationId, request.Line1, request.Line2, ct);
        return Result.Success();
    }

    public async Task<Result> Handle(ReportWeightCommand request, CancellationToken ct)
    {
        await _notifier.WeightReportedAsync(request.StationId, request.Value, request.Unit, request.Stable, ct);
        return Result.Success();
    }

    /// <summary>
    /// Explicit assignment first, then a profile bound to this station, then the location default.
    /// Same resolution order for every peripheral, so a store overrides only what actually differs.
    /// </summary>
    private static async Task<TProfile?> ResolveAsync<TProfile>(
        IQueryable<TProfile> source,
        long? assignedId,
        Station station,
        CancellationToken ct)
        where TProfile : class, IStationScopedProfile
    {
        if (assignedId is { } id)
        {
            var assigned = await source.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
            if (assigned is not null)
            {
                return assigned;
            }
        }

        return await source.AsNoTracking().FirstOrDefaultAsync(p => p.StationId == station.Id && p.IsActive, ct)
               ?? await source.AsNoTracking()
                   .FirstOrDefaultAsync(p => p.LocationId == station.LocationId && p.StationId == null && p.IsActive, ct);
    }

    // Delegated to the shared mapper: the API's own reader host builds the same contract from
    // the same row, and one copy is what keeps the two hosts configuring a device identically.
    private static ReaderProfileContract? ToContract(ReaderProfile? profile) =>
        ReaderProfileMapper.ToContract(profile);

    private static PrinterProfileContract? ToContract(PrinterProfile? profile) => profile is null
        ? null
        : new PrinterProfileContract(
            profile.Id,
            profile.Name,
            profile.Port,
            profile.SetupCommand,
            profile.CutterCommand,
            profile.RedCommand,
            profile.BlackCommand,
            profile.DefaultCopies,
            profile.PageEject,
            profile.ExtraCopyOnCard,
            profile.InitializeSerial,
            profile.Output.ToString(),
            profile.Columns,
            profile.DrawerTrigger,
            profile.DrawerRepeat,
            profile.OpenDrawerOnPrint);

    private static ScaleProfileContract? ToContract(ScaleProfile? profile) => profile is null
        ? null
        : new ScaleProfileContract(
            profile.Id,
            profile.Name,
            profile.Port,
            profile.BaudRate,
            profile.DataBits,
            profile.Parity,
            profile.StopBits,
            profile.GetWeightCommand,
            profile.ZeroCommand,
            profile.Unit,
            profile.TimeoutMs);

    private static PoleDisplayProfileContract? ToContract(PoleDisplayProfile? profile) => profile is null
        ? null
        : new PoleDisplayProfileContract(
            profile.Id,
            profile.Name,
            profile.Port,
            profile.BaudRate,
            profile.Line1Width,
            profile.Line2Width,
            profile.IdleLine1,
            profile.IdleLine2,
            profile.ClearCommand,
            profile.Line1Command,
            profile.Line2Command);
}

/// <summary>Errors shared by handlers that resolve a station outside the POS context loader.</summary>
public static class PosContextLoaderErrors
{
    public static readonly Error StationNotFound = new("station.not_found", "That station is not registered.");
}
