using Retail25.Contracts.Terminals;
using Retail25.Devices.Rfid;

namespace Retail25.TerminalAgent.UnitTests.Rfid;

/// <summary>
/// Stands in for the protocol conversation, so a sweep can be tested without a reader on the desk.
///
/// <para>
/// The probe itself is deliberately not faked in <see cref="ReaderDiscoveryTests"/>'s original
/// tests — those use a real socket because what they test is which interfaces get searched. This
/// is for the other half: what the sweep <em>does</em> with what it finds. Whether a device that
/// answers the port but fails the handshake is excluded, whether two sightings of one serial
/// collapse, whether an address already in use is skipped — none of those are questions about
/// sockets, and answering them with real hardware would mean seven readers on a bench.
/// </para>
/// </summary>
internal sealed class FakeReaderIdentityProbe : IReaderIdentityProbe
{
    private readonly Dictionary<string, ReaderIdentity?> _answers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every address that was actually asked, in the order the sweep asked.</summary>
    public List<string> Probed { get; } = [];

    /// <summary>What an address that is not configured below answers. Null means "not a reader".</summary>
    public ReaderIdentity? Default { get; set; }

    public FakeReaderIdentityProbe Reader(string host, string? serial, int antennas = 4, bool reported = true)
    {
        _answers[host] = new ReaderIdentity(
            Host: host,
            Port: 0,
            Protocol: ReaderProtocol.UhfSerial,
            SerialNumber: serial,
            FirmwareVersion: "8.2",
            AntennaCount: antennas,
            AntennaCountReported: reported);

        return this;
    }

    /// <summary>
    /// Treats anything that answers the port as a reader.
    /// <para>
    /// For the tests that are about <em>which interfaces get searched</em> rather than about
    /// identification: they start a plain socket and need the sweep to accept it, so the protocol
    /// question has to answer yes for every address.
    /// </para>
    /// </summary>
    public FakeReaderIdentityProbe EverythingIsAReader()
    {
        Default = new ReaderIdentity(
            Host: string.Empty,
            Port: 0,
            Protocol: ReaderProtocol.UhfSerial,
            SerialNumber: null,
            FirmwareVersion: "8.2",
            AntennaCount: 4,
            AntennaCountReported: false);

        return this;
    }

    /// <summary>An address that accepts a connection but is not a reader — a printer, a bridge, a scale.</summary>
    public FakeReaderIdentityProbe NotAReader(string host)
    {
        _answers[host] = null;
        return this;
    }

    public Task<ReaderIdentity?> ProbeAsync(string host, int port, CancellationToken ct)
    {
        lock (Probed)
        {
            Probed.Add(host);
        }

        var answer = _answers.TryGetValue(host, out var configured) ? configured : Default;

        // The port is filled in here rather than by the caller, so a fake identity carries the port
        // the sweep actually used and a test can assert on it.
        return Task.FromResult(answer is null ? null : answer with { Port = port });
    }
}
