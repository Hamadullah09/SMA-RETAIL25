using Retail25.Contracts.Terminals;

namespace Retail25.TerminalAgent.Rfid;

/// <summary>
/// What this machine has been told to drive.
/// <para>
/// The counterpart to <see cref="ProfileStore"/>, which describes one till's hardware. This describes
/// the machine: every reader assigned to it and what each antenna stands for. A machine driving three
/// readers across twelve stations has no single till to ask about, so it asks about itself.
/// </para>
/// <para>
/// Null until the server has answered. That state matters and is deliberately visible: an agent with
/// no device configuration is one the server has not registered, and it falls back to the
/// per-station profile rather than pretending it has nothing to do.
/// </para>
/// </summary>
public sealed class DeviceConfigurationStore
{
    private DeviceConfigurationContract? _configuration;
    private string? _revision;

    public DeviceConfigurationContract? Current => Volatile.Read(ref _configuration);

    /// <summary>
    /// Raised when the configuration actually differs, never merely because it was fetched again.
    /// <para>
    /// The server answers this poll every few seconds with the same content; raising on each one
    /// would tear down and rebuild every reader session on this machine several times a minute, and
    /// tags would be dropped in every gap.
    /// </para>
    /// </summary>
    public event Action? Changed;

    public void Set(DeviceConfigurationContract configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var revision = configuration.Revision;

        if (string.Equals(_revision, revision, StringComparison.Ordinal))
        {
            return;
        }

        _revision = revision;
        Volatile.Write(ref _configuration, configuration);

        Changed?.Invoke();
    }

    /// <summary>
    /// Forgets the configuration, so the machine falls back to the per-station profile.
    /// <para>
    /// Used when the server says this device is unknown — which is a real answer, not an error to
    /// swallow: a machine whose registration was removed should stop driving readers it no longer
    /// owns rather than carry on with the last configuration it happened to hold.
    /// </para>
    /// </summary>
    public void Clear()
    {
        if (Volatile.Read(ref _configuration) is null)
        {
            return;
        }

        _revision = null;
        Volatile.Write(ref _configuration, null);

        Changed?.Invoke();
    }
}
