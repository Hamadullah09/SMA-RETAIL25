namespace Retail25.Domain.Terminals;

/// <summary>
/// Shared shape of every peripheral profile: it belongs to a location, may be pinned to one station,
/// and can be switched off without being deleted.
/// <para>
/// The interface exists so profile resolution — explicit assignment, then station-specific, then
/// location default — is written once instead of four times, one per device type.
/// </para>
/// </summary>
public interface IStationScopedProfile
{
    long Id { get; }

    long LocationId { get; }

    long? StationId { get; }

    bool IsActive { get; }
}
