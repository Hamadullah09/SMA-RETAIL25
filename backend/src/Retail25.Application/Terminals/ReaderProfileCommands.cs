using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Terminals;

namespace Retail25.Application.Terminals;

/// <summary>
/// A reader's settings, as a screen shows them.
/// <para>
/// The frequency range is carried as both channel indices and megahertz. Indices are what the device
/// accepts; megahertz is the only form in which the question that matters — is this shop transmitting
/// inside its licensed band — can be answered by looking.
/// </para>
/// </summary>
public sealed record ReaderProfileDto(
    long Id,
    long LocationId,
    long? StationId,
    string Name,
    string Host,
    int Port,
    ReaderProtocol Protocol,
    string AntennaZones,
    int RssiThresholdDbm,
    int MinimumReadCount,
    int DebounceMs,
    int CoalesceMs,
    int FlushIntervalMs,
    int MaxBatchSize,
    bool AutoAcceptBatches,
    bool ContinuousMode,
    string OutputPowerDbm,
    RadioRegion Region,
    int FrequencyStartIndex,
    int FrequencyEndIndex,
    double FrequencyStartMhz,
    double FrequencyEndMhz,

    // Both ends, because only the top was sent and the screen had to assume the bottom was zero.
    // FCC's window starts at 7 — channel numbering is shared across regions — so the form advertised
    // "Channels 0–57", the operator typed 0 as invited, and the save was refused for being below the
    // band. The client cannot infer this; it has to be told.
    int RegionMinChannel,
    int RegionMaxChannel,
    RfLinkProfile LinkProfile,
    BeeperMode Beeper,
    int AntennaReturnLossThresholdDb,
    bool ImpinjFastTid,
    bool DenseReaderMode,
    int DeviceAddress,
    bool IsActive);

[RequiresPermission(PermissionKeys.Terminals.Read)]
public sealed record ListReaderProfilesQuery(long LocationId) : IRequest<IReadOnlyList<ReaderProfileDto>>;

[RequiresPermission(PermissionKeys.Terminals.Read)]
public sealed record GetReaderProfileQuery(long Id) : IRequest<Result<ReaderProfileDto>>;

/// <summary>
/// Changes a reader's settings.
/// <para>
/// <c>terminals.register</c> rather than <c>terminals.operate</c>: a cashier may work a till, but
/// transmit power and frequency band are a licensing matter and belong with whoever commissions
/// hardware.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Terminals.Register)]
public sealed record UpdateReaderProfileCommand(
    long Id,
    string Name,
    string Host,
    int Port,
    ReaderProtocol Protocol,
    string AntennaZones,
    int RssiThresholdDbm,
    int MinimumReadCount,
    int DebounceMs,
    int CoalesceMs,
    int FlushIntervalMs,
    int MaxBatchSize,
    bool AutoAcceptBatches,
    bool ContinuousMode,
    string OutputPowerDbm,
    RadioRegion Region,
    int FrequencyStartIndex,
    int FrequencyEndIndex,
    RfLinkProfile LinkProfile,
    BeeperMode Beeper,
    int AntennaReturnLossThresholdDb,
    bool ImpinjFastTid,
    bool DenseReaderMode,
    int DeviceAddress) : IRequest<Result<ReaderProfileDto>>;

public sealed class ReaderProfileHandlers :
    IRequestHandler<ListReaderProfilesQuery, IReadOnlyList<ReaderProfileDto>>,
    IRequestHandler<GetReaderProfileQuery, Result<ReaderProfileDto>>,
    IRequestHandler<UpdateReaderProfileCommand, Result<ReaderProfileDto>>
{
    public static readonly Error NotFound = new("reader_profile.not_found", "No such reader.");

    public static readonly Error PowerOutOfRange = new(
        "reader_profile.power_out_of_range",
        "Transmit power must be between 0 and 33 dBm for each antenna.");

    public static readonly Error FrequencyOutOfRange = new(
        "reader_profile.frequency_out_of_range",
        "That frequency range is outside the selected region's band.");

    private readonly IApplicationDbContext _db;

    public ReaderProfileHandlers(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<ReaderProfileDto>> Handle(ListReaderProfilesQuery request, CancellationToken ct)
    {
        var profiles = await _db.ReaderProfiles.AsNoTracking()
            .Where(p => p.LocationId == request.LocationId)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        return profiles.Select(ToDto).ToList();
    }

    public async Task<Result<ReaderProfileDto>> Handle(GetReaderProfileQuery request, CancellationToken ct)
    {
        var profile = await _db.ReaderProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.Id, ct);

        return profile is null
            ? Result.Failure<ReaderProfileDto>(NotFound.With("id", request.Id))
            : Result.Success(ToDto(profile));
    }

    public async Task<Result<ReaderProfileDto>> Handle(UpdateReaderProfileCommand request, CancellationToken ct)
    {
        var profile = await _db.ReaderProfiles.FirstOrDefaultAsync(p => p.Id == request.Id, ct);

        if (profile is null)
        {
            return Result.Failure<ReaderProfileDto>(NotFound.With("id", request.Id));
        }

        // Validated here rather than left to the reader. The device would refuse a bad value, but the
        // refusal arrives at whichever till happens to reconnect first, minutes later, in a log
        // nobody is reading — while the person who typed it has long since closed the screen.
        if (!TryParsePowers(request.OutputPowerDbm, out var powers))
        {
            return Result.Failure<ReaderProfileDto>(PowerOutOfRange);
        }

        // Against the region's own window, not against zero. Channel numbering is shared across
        // regions, so FCC's first legal channel is 7 — a range starting at 0 would be below the band.
        var minChannel = RadioFrequencyPlan.MinChannel(request.Region);
        var maxChannel = RadioFrequencyPlan.MaxChannel(request.Region);

        if (request.FrequencyStartIndex < minChannel
            || request.FrequencyEndIndex > maxChannel
            || request.FrequencyStartIndex > request.FrequencyEndIndex)
        {
            return Result.Failure<ReaderProfileDto>(FrequencyOutOfRange
                .With("region", request.Region.ToString())
                .With("minChannel", minChannel)
                .With("maxChannel", maxChannel));
        }

        profile.Name = request.Name.Trim();
        profile.Host = request.Host.Trim();
        profile.Port = request.Port;
        profile.Protocol = request.Protocol;
        profile.AntennaZones = request.AntennaZones.Trim();
        profile.RssiThresholdDbm = request.RssiThresholdDbm;
        profile.MinimumReadCount = request.MinimumReadCount;
        profile.DebounceMs = request.DebounceMs;
        profile.CoalesceMs = request.CoalesceMs;
        profile.FlushIntervalMs = request.FlushIntervalMs;
        profile.MaxBatchSize = request.MaxBatchSize;
        profile.AutoAcceptBatches = request.AutoAcceptBatches;
        profile.ContinuousMode = request.ContinuousMode;

        // Normalised on the way in, so "30, 30 ,30" and "30,30,30" are the same row.
        profile.OutputPowerDbm = string.Join(',', powers);

        profile.Region = request.Region;
        profile.FrequencyStartIndex = request.FrequencyStartIndex;
        profile.FrequencyEndIndex = request.FrequencyEndIndex;
        profile.LinkProfile = request.LinkProfile;
        profile.Beeper = request.Beeper;
        profile.AntennaReturnLossThresholdDb = Math.Clamp(request.AntennaReturnLossThresholdDb, 0, 255);
        profile.ImpinjFastTid = request.ImpinjFastTid;
        profile.DenseReaderMode = request.DenseReaderMode;
        profile.DeviceAddress = Math.Clamp(request.DeviceAddress, 0, 255);

        await _db.SaveChangesAsync(ct);

        // The agent picks this up on its next profile refresh and pushes it into the device. Nothing
        // is sent from here: the server does not hold a connection to the reader, and inventing one
        // would make saving a form depend on a till being switched on.
        return Result.Success(ToDto(profile));
    }

    /// <summary>
    /// Accepts one figure for every port or one per port, and nothing else. An empty list is a
    /// failure rather than "leave it alone" — a form that silently ignored what was typed is worse
    /// than one that refuses it.
    /// </summary>
    private static bool TryParsePowers(string? setting, out int[] powers)
    {
        powers = [];

        if (string.IsNullOrWhiteSpace(setting))
        {
            return false;
        }

        var parts = setting.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parsed = new List<int>(parts.Length);

        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var dbm) || dbm is < 0 or > 33)
            {
                return false;
            }

            parsed.Add(dbm);
        }

        if (parsed.Count is 0 or > 4)
        {
            return false;
        }

        powers = parsed.ToArray();
        return true;
    }

    private static ReaderProfileDto ToDto(ReaderProfile p) => new(
        p.Id,
        p.LocationId,
        p.StationId,
        p.Name,
        p.Host,
        p.Port,
        p.Protocol,
        p.AntennaZones,
        p.RssiThresholdDbm,
        p.MinimumReadCount,
        p.DebounceMs,
        p.CoalesceMs,
        p.FlushIntervalMs,
        p.MaxBatchSize,
        p.AutoAcceptBatches,
        p.ContinuousMode,
        p.OutputPowerDbm,
        p.Region,
        p.FrequencyStartIndex,
        p.FrequencyEndIndex,
        RadioFrequencyPlan.ToMegahertz(p.Region, p.FrequencyStartIndex),
        RadioFrequencyPlan.ToMegahertz(p.Region, p.FrequencyEndIndex),
        RadioFrequencyPlan.MinChannel(p.Region),
        RadioFrequencyPlan.MaxChannel(p.Region),
        p.LinkProfile,
        p.Beeper,
        p.AntennaReturnLossThresholdDb,
        p.ImpinjFastTid,
        p.DenseReaderMode,
        p.DeviceAddress,
        p.IsActive);
}
