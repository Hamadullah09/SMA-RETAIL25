using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Terminals;

namespace Retail25.Application.Settings;

/// <summary>
/// Stations and the peripheral profiles they point at (guide p.77–81).
/// <para>
/// Everything here is a row, including the escape sequences. Epson cuts with <c>27,105</c> and Star
/// with <c>27,100,48</c>; the drawer kick differs between two tills in the same shop. Hard-coding any
/// of it would mean a release every time a store replaces a printer, which is precisely what the
/// legacy system avoided by making them editable.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Settings.Hardware)]
public sealed record SaveStationCommand(
    Guid LocationId,
    Guid? Id,
    string StationCode,
    string? Name,
    bool? FastScanMode,
    bool? AutoSaveSales,
    bool? ConfirmBeforeSaving,
    bool? ScanRandomWeightBarcodes,
    Guid? DefaultTenderTypeId,
    Guid? PrinterProfileId,
    Guid? ReaderProfileId,
    Guid? ScaleProfileId,
    Guid? PoleDisplayProfileId,
    ReaderMode ReaderMode,
    bool IsActive) : IRequest<Result<StationSettingsDto>>;

/// <summary>
/// Retires a station. Deactivation, not deletion: sale history names the station a sale was rung on,
/// and a drawer session is reconciled against it.
/// </summary>
[RequiresPermission(PermissionKeys.Settings.Hardware)]
public sealed record DeactivateStationCommand(Guid StationId) : IRequest<Result>;

[RequiresPermission(PermissionKeys.Settings.Hardware)]
public sealed record SavePrinterProfileCommand(Guid LocationId, PrinterSettingsDto Profile) : IRequest<Result<PrinterSettingsDto>>;

[RequiresPermission(PermissionKeys.Settings.Hardware)]
public sealed record SaveScaleProfileCommand(Guid LocationId, ScaleSettingsDto Profile) : IRequest<Result<ScaleSettingsDto>>;

[RequiresPermission(PermissionKeys.Settings.Hardware)]
public sealed record SavePoleDisplayProfileCommand(Guid LocationId, PoleDisplaySettingsDto Profile) : IRequest<Result<PoleDisplaySettingsDto>>;

[RequiresPermission(PermissionKeys.Settings.Hardware)]
public sealed record SaveReaderProfileCommand(Guid LocationId, ReaderSettingsDto Profile) : IRequest<Result<ReaderSettingsDto>>;

public sealed class HardwareSettingsHandlers
    : IRequestHandler<SaveStationCommand, Result<StationSettingsDto>>,
      IRequestHandler<DeactivateStationCommand, Result>,
      IRequestHandler<SavePrinterProfileCommand, Result<PrinterSettingsDto>>,
      IRequestHandler<SaveScaleProfileCommand, Result<ScaleSettingsDto>>,
      IRequestHandler<SavePoleDisplayProfileCommand, Result<PoleDisplaySettingsDto>>,
      IRequestHandler<SaveReaderProfileCommand, Result<ReaderSettingsDto>>
{
    public static readonly Error StationNotFound = new("station.not_found", "No such station.");
    public static readonly Error DuplicateStationCode = new("station.duplicate_code", "A station with this code already exists at this location.");
    public static readonly Error ProfileNotFound = new("profile.not_found", "No such hardware profile.");
    public static readonly Error DrawerStillOpen = new("station.drawer_open", "This station has an open drawer session. Close it before retiring the station.");

    private readonly IApplicationDbContext _db;
    private readonly IPosNotifier _notifier;
    private readonly ITerminalNotifier _terminals;
    private readonly IDateTime _clock;

    public HardwareSettingsHandlers(
        IApplicationDbContext db,
        IPosNotifier notifier,
        ITerminalNotifier terminals,
        IDateTime clock)
    {
        _db = db;
        _notifier = notifier;
        _terminals = terminals;
        _clock = clock;
    }

    public async Task<Result<StationSettingsDto>> Handle(SaveStationCommand request, CancellationToken ct)
    {
        Station? station;

        if (request.Id is { } id)
        {
            station = await _db.Stations.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (station is null)
            {
                return Result.Failure<StationSettingsDto>(StationNotFound.With("stationId", id));
            }
        }
        else
        {
            var created = Station.Create(request.LocationId, request.StationCode, request.Name);
            if (created.IsFailure)
            {
                return Result.Failure<StationSettingsDto>(created.Error);
            }

            station = created.Value;
            _db.Stations.Add(station);
        }

        var normalized = request.StationCode.Trim().PadLeft(3, '0');

        if (await _db.Stations.AsNoTracking().AnyAsync(
                s => s.LocationId == request.LocationId && s.StationCode == normalized && s.Id != station.Id, ct))
        {
            return Result.Failure<StationSettingsDto>(DuplicateStationCode.With("stationCode", normalized));
        }

        station.StationCode = normalized;
        station.Name = request.Name?.Trim();
        station.FastScanMode = request.FastScanMode;
        station.AutoSaveSales = request.AutoSaveSales;
        station.ConfirmBeforeSaving = request.ConfirmBeforeSaving;
        station.ScanRandomWeightBarcodes = request.ScanRandomWeightBarcodes;
        station.DefaultTenderTypeId = request.DefaultTenderTypeId;
        station.IsActive = request.IsActive;
        station.AssignPeripherals(
            request.PrinterProfileId,
            request.ReaderProfileId,
            request.ScaleProfileId,
            request.PoleDisplayProfileId);
        station.SetReaderMode(request.ReaderMode);

        await _db.SaveChangesAsync(ct);

        var dto = SettingsQueryHandler.ToDto(station, _clock.Now);
        await _notifier.RowChangedAsync(request.LocationId, GridKeys.Station, station.Id, dto, ct);

        // The agent on that till is told immediately. Waiting for its next profile poll would leave a
        // cashier pressing a drawer key that does nothing, with no indication why.
        await _terminals.UpdateProfileAsync(station.Id, dto, ct);

        return Result.Success(dto);
    }

    public async Task<Result> Handle(DeactivateStationCommand request, CancellationToken ct)
    {
        var station = await _db.Stations.FirstOrDefaultAsync(s => s.Id == request.StationId, ct);
        if (station is null)
        {
            return Result.Failure(StationNotFound.With("stationId", request.StationId));
        }

        if (await _db.DrawerSessions.AsNoTracking().AnyAsync(d => d.StationId == station.Id && d.ClosedAt == null, ct))
        {
            // An open drawer belongs to a shift that has to be counted. Retiring the station under it
            // would strand a float nobody can reconcile.
            return Result.Failure(DrawerStillOpen.With("stationCode", station.StationCode));
        }

        station.IsActive = false;
        await _db.SaveChangesAsync(ct);

        var dto = SettingsQueryHandler.ToDto(station, _clock.Now);
        await _notifier.RowChangedAsync(station.LocationId, GridKeys.Station, station.Id, dto, ct);

        return Result.Success();
    }

    public async Task<Result<PrinterSettingsDto>> Handle(SavePrinterProfileCommand request, CancellationToken ct)
    {
        var input = request.Profile;
        var profile = await FindOrAddAsync(_db.PrinterProfiles, input.Id, () => PrinterProfile.CreateDefault(request.LocationId, input.Name), ct);

        profile.LocationId = request.LocationId;
        profile.StationId = input.StationId;
        profile.Name = input.Name.Trim();
        profile.SetupCommand = input.SetupCommand;
        profile.CutterCommand = input.CutterCommand;
        profile.RedCommand = input.RedCommand;
        profile.BlackCommand = input.BlackCommand;
        profile.Port = input.Port;
        profile.DefaultCopies = Math.Clamp(input.DefaultCopies, 1, 9);
        profile.PageEject = input.PageEject;
        profile.ExtraCopyOnCard = input.ExtraCopyOnCard;
        profile.InitializeSerial = input.InitializeSerial;
        profile.Output = input.Output;
        profile.Columns = Math.Clamp(input.Columns, 20, 132);
        profile.DrawerTrigger = input.DrawerTrigger;
        profile.DrawerRepeat = Math.Clamp(input.DrawerRepeat, 1, 5);
        profile.OpenDrawerOnPrint = input.OpenDrawerOnPrint;
        profile.IsActive = input.IsActive;

        await _db.SaveChangesAsync(ct);
        await PushToStationsAsync(request.LocationId, s => s.PrinterProfileId == profile.Id, SettingsQueryHandler.ToDto(profile), ct);

        return Result.Success(SettingsQueryHandler.ToDto(profile));
    }

    public async Task<Result<ScaleSettingsDto>> Handle(SaveScaleProfileCommand request, CancellationToken ct)
    {
        var input = request.Profile;
        var profile = await FindOrAddAsync(_db.ScaleProfiles, input.Id, () => ScaleProfile.CreateDefault(request.LocationId), ct);

        profile.LocationId = request.LocationId;
        profile.StationId = input.StationId;
        profile.Name = input.Name.Trim();
        profile.Port = input.Port;
        profile.BaudRate = input.BaudRate;
        profile.DataBits = input.DataBits;
        profile.Parity = input.Parity;
        profile.StopBits = input.StopBits;
        profile.GetWeightCommand = input.GetWeightCommand;
        profile.ZeroCommand = input.ZeroCommand;
        profile.Unit = input.Unit;
        profile.TimeoutMs = Math.Clamp(input.TimeoutMs, 100, 10_000);
        profile.IsActive = input.IsActive;

        await _db.SaveChangesAsync(ct);
        await PushToStationsAsync(request.LocationId, s => s.ScaleProfileId == profile.Id, SettingsQueryHandler.ToDto(profile), ct);

        return Result.Success(SettingsQueryHandler.ToDto(profile));
    }

    public async Task<Result<PoleDisplaySettingsDto>> Handle(SavePoleDisplayProfileCommand request, CancellationToken ct)
    {
        var input = request.Profile;
        var profile = await FindOrAddAsync(_db.PoleDisplayProfiles, input.Id, () => PoleDisplayProfile.CreateDefault(request.LocationId), ct);

        profile.LocationId = request.LocationId;
        profile.StationId = input.StationId;
        profile.Name = input.Name.Trim();
        profile.Port = input.Port;
        profile.BaudRate = input.BaudRate;
        profile.Line1Width = Math.Clamp(input.Line1Width, 1, 80);
        profile.Line2Width = Math.Clamp(input.Line2Width, 1, 80);
        profile.IdleLine1 = input.IdleLine1;
        profile.IdleLine2 = input.IdleLine2;
        profile.ClearCommand = input.ClearCommand;
        profile.Line1Command = input.Line1Command;
        profile.Line2Command = input.Line2Command;
        profile.IsActive = input.IsActive;

        await _db.SaveChangesAsync(ct);
        await PushToStationsAsync(request.LocationId, s => s.PoleDisplayProfileId == profile.Id, SettingsQueryHandler.ToDto(profile), ct);

        return Result.Success(SettingsQueryHandler.ToDto(profile));
    }

    public async Task<Result<ReaderSettingsDto>> Handle(SaveReaderProfileCommand request, CancellationToken ct)
    {
        var input = request.Profile;
        var profile = await FindOrAddAsync(_db.ReaderProfiles, input.Id, () => ReaderProfile.CreateDefault(request.LocationId, input.Name), ct);

        profile.LocationId = request.LocationId;
        profile.StationId = input.StationId;
        profile.Name = input.Name.Trim();
        profile.Host = input.Host;
        profile.Port = input.Port;
        profile.Protocol = input.Protocol;
        profile.AntennaZones = input.AntennaZones;
        profile.RssiThresholdDbm = input.RssiThresholdDbm;
        profile.MinimumReadCount = Math.Max(1, input.MinimumReadCount);
        profile.DebounceMs = Math.Clamp(input.DebounceMs, 0, 30_000);
        profile.CoalesceMs = Math.Clamp(input.CoalesceMs, 0, 5_000);
        profile.FlushIntervalMs = Math.Clamp(input.FlushIntervalMs, 50, 5_000);
        profile.MaxBatchSize = Math.Clamp(input.MaxBatchSize, 1, 500);
        profile.AutoAcceptBatches = input.AutoAcceptBatches;
        profile.ContinuousMode = input.ContinuousMode;
        profile.IsActive = input.IsActive;

        await _db.SaveChangesAsync(ct);
        await PushToStationsAsync(request.LocationId, s => s.ReaderProfileId == profile.Id, SettingsQueryHandler.ToDto(profile), ct);

        return Result.Success(SettingsQueryHandler.ToDto(profile));
    }

    /// <summary>
    /// Loads a profile by id, or creates one when the caller supplied an empty id. Returning a fresh
    /// entity for an unknown id would silently create a duplicate instead of reporting the mistake,
    /// so an id that does not resolve is only tolerated when it is empty.
    /// </summary>
    private static async Task<TProfile> FindOrAddAsync<TProfile>(
        DbSet<TProfile> set,
        Guid id,
        Func<TProfile> create,
        CancellationToken ct)
        where TProfile : Entity
    {
        if (id != Guid.Empty)
        {
            var existing = await set.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (existing is not null)
            {
                return existing;
            }
        }

        var profile = create();
        set.Add(profile);
        return profile;
    }

    /// <summary>
    /// Tells every station using a profile that it changed, so an agent picks up the new escape codes
    /// without waiting for its refresh interval or a restart.
    /// </summary>
    private async Task PushToStationsAsync(
        Guid locationId,
        Func<Station, bool> uses,
        object profile,
        CancellationToken ct)
    {
        await _notifier.SettingsChangedAsync(locationId, SettingsSections.Hardware, ct);

        var stations = await _db.Stations.AsNoTracking()
            .Where(s => s.LocationId == locationId && s.IsActive)
            .ToListAsync(ct);

        foreach (var station in stations.Where(uses))
        {
            await _terminals.UpdateProfileAsync(station.Id, profile, ct);
        }
    }
}
