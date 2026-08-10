using Retail25.Contracts.Terminals;
using Retail25.Domain.Terminals;

namespace Retail25.Application.Terminals;

/// <summary>
/// Turns a stored reader profile into the contract a driver understands.
/// <para>
/// Public, and deliberately so: there are now two things that open a reader connection. The terminal
/// agent asks for this over HTTP, and the API's own reader host builds it in-process when the server
/// sits on the shop's network. Both must configure a device from the same row in the same way â€” a
/// second copy of this mapping is a second set of radio settings that can drift, and the symptom of
/// drift is a reader that behaves differently depending on which host happened to connect to it.
/// </para>
/// </summary>
public static class ReaderProfileMapper
{
    public static ReaderProfileContract? ToContract(ReaderProfile? profile) => profile is null
        ? null
        : new ReaderProfileContract(
            profile.Id,
            profile.Name,
            profile.Host,
            profile.Port,
            (Contracts.Terminals.ReaderProtocol)profile.Protocol,
            profile.AntennaZones,
            profile.RssiThresholdDbm,
            profile.MinimumReadCount,
            profile.DebounceMs,
            profile.CoalesceMs,
            profile.FlushIntervalMs,
            profile.MaxBatchSize,
            profile.AutoAcceptBatches,
            profile.ContinuousMode,

            // The reader's own hardware configuration travels with the rest of the profile, so the
            // caller applies it on connect without a second round trip. Casts rather than mappings:
            // both enums are the protocol's wire values, deliberately, so there is nothing to
            // translate and nothing to get out of step.
            profile.OutputPowerDbm,
            (Contracts.Terminals.RadioRegion)profile.Region,
            profile.FrequencyStartIndex,
            profile.FrequencyEndIndex,
            (Contracts.Terminals.RfLinkProfile)profile.LinkProfile,
            (Contracts.Terminals.BeeperMode)profile.Beeper,
            profile.AntennaReturnLossThresholdDb,
            profile.ImpinjFastTid,
            profile.DenseReaderMode,
            profile.DeviceAddress);
}
