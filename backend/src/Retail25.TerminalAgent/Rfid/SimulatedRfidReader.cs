using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Retail25.Contracts.Terminals;

namespace Retail25.TerminalAgent.Rfid;

/// <summary>
/// A reader with no hardware behind it (decision Q3).
/// <para>
/// It does two jobs. It backs the RFID simulator so a developer or a demo can push a basket of tags
/// through the real ingest path — debounce, EPC state checks, rejection reasons and all — and it
/// reproduces the behaviours that make bulk RFID hard: the same tag reported many times a second,
/// weak reads from a neighbouring shelf, and reads on antennas that are not pointed at the till.
/// Without those, the filters downstream would never be exercised until a store found the gaps.
/// </para>
/// </summary>
public sealed class SimulatedRfidReader : IRfidReader
{
    private readonly ILogger<SimulatedRfidReader> _logger;
    private readonly Channel<TagRead> _reads = Channel.CreateUnbounded<TagRead>();

    private ReaderProfileContract? _profile;
    private bool _running;

    public SimulatedRfidReader(ILogger<SimulatedRfidReader> logger) => _logger = logger;

    public string Description => "Simulated reader";

    public bool IsConnected { get; private set; }

    public Task ConnectAsync(ReaderProfileContract profile, CancellationToken ct)
    {
        _profile = profile;
        IsConnected = true;
        _logger.LogInformation("Simulated reader ready (no hardware attached)");
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _running = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _running = false;
        return Task.CompletedTask;
    }

    public IAsyncEnumerable<TagRead> ReadsAsync(CancellationToken ct) => _reads.Reader.ReadAllAsync(ct);

    /// <summary>
    /// There is no device, so there is nothing to report. Said plainly rather than fabricated: a
    /// diagnostics screen showing a plausible temperature for a reader that does not exist is how a
    /// shop spends an afternoon debugging the wrong till.
    /// </summary>
    public Task<ReaderDiagnostics> ReadDiagnosticsAsync(CancellationToken ct)
        => Task.FromResult(new ReaderDiagnostics
        {
            Unavailable = ["this station is running the simulator, not a reader"],
        });

    /// <summary>Accepts everything, because nothing is configured. No refusals to report.</summary>
    public Task<IReadOnlyList<string>> ApplySettingsAsync(ReaderProfileContract profile, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<string>>([]);

    /// <summary>
    /// Pushes a basket of tags as a reader would actually report it: each EPC seen several times
    /// across the window, on a checkout antenna, at a healthy signal strength.
    /// </summary>
    public async Task PresentAsync(IEnumerable<string> epcs, int repeatsPerTag = 3, CancellationToken ct = default)
    {
        if (!_running)
        {
            _logger.LogWarning("Tags presented while the simulated reader was stopped; they were ignored");
            return;
        }

        var antenna = FirstCheckoutAntenna();
        var now = DateTimeOffset.UtcNow;

        foreach (var epc in epcs)
        {
            var normalised = epc.Trim().ToUpperInvariant();

            for (var i = 0; i < Math.Max(1, repeatsPerTag); i++)
            {
                await _reads.Writer.WriteAsync(
                    new TagRead(normalised, antenna, -55, 1, now, now.AddMilliseconds(i * 20)),
                    ct);
            }
        }
    }

    /// <summary>
    /// A read that the reader profile should reject — too weak, or on an antenna pointed at the
    /// shelf behind the till. Exercising the rejection path is the point.
    /// </summary>
    public async Task PresentStrayAsync(string epc, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var strayAntenna = (ushort)(FirstCheckoutAntenna() + 100);

        await _reads.Writer.WriteAsync(new TagRead(epc.Trim().ToUpperInvariant(), strayAntenna, -85, 1, now, now), ct);
    }

    /// <summary>Generates plausible SGTIN-96 style EPCs for load and demo scenarios.</summary>
    public static IReadOnlyList<string> GenerateEpcs(int count)
    {
        var epcs = new List<string>(count);
        var buffer = new byte[12];

        for (var i = 0; i < count; i++)
        {
            RandomNumberGenerator.Fill(buffer);

            // A fixed SGTIN-96 header keeps generated tags recognisable in logs.
            buffer[0] = 0x30;
            epcs.Add(Convert.ToHexString(buffer));
        }

        return epcs;
    }

    private ushort FirstCheckoutAntenna()
    {
        var antennas = AntennaZoneMap.CheckoutAntennas(_profile?.AntennaZones);
        return antennas.Count > 0 ? antennas[0] : (ushort)1;
    }

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        _reads.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
