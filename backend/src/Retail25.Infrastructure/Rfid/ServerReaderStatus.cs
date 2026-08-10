using System.Collections.Concurrent;
using Retail25.Application.Rfid;

namespace Retail25.Infrastructure.Rfid;

/// <summary>
/// The live view of the reader sessions this process holds.
/// <para>
/// A singleton written by the reader host and read by the status endpoint, rather than the host
/// being injected into a controller. The host is a background service with a lifetime of its own;
/// handing it to request code would tie a reader's retry loop to whatever an HTTP caller does next.
/// </para>
/// </summary>
internal sealed class ServerReaderStatus : IReaderConnectionStatus
{
    private readonly ConcurrentDictionary<long, ReaderConnectionState> _readers = new();

    /// <summary>Set once at startup from configuration. See <see cref="ReaderConnectionSnapshot"/>.</summary>
    public bool ServerHosted { get; set; }

    public ReaderConnectionSnapshot Current => new(
        ServerHosted,
        _readers.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList());

    public void Track(long profileId, string name, string endpoint, long stationId, bool connected) =>
        _readers[profileId] = new ReaderConnectionState(profileId, name, endpoint, stationId, connected);

    public void SetConnected(long profileId, bool connected)
    {
        if (_readers.TryGetValue(profileId, out var existing))
        {
            _readers[profileId] = existing with { Connected = connected };
        }
    }

    public void Forget(long profileId) => _readers.TryRemove(profileId, out _);
}
