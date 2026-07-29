using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.SignalR.Client;
using Retail25.Contracts.Terminals;

// A reader that does not exist (decision Q3).
//
// It connects to the server's TerminalHub exactly as a real agent does and publishes tag batches, so
// everything downstream — Redis arbitration, the EPC state machine, rejection reasons, the live feed,
// the cart — is exercised on the real path. That matters more than it sounds: RFID readers cannot go
// in CI and are awkward to keep on a desk, so without this the most intricate code in the system
// would only ever be tested by carrying a basket past an antenna.

var options = SimulatorOptions.Parse(args);

if (options is null)
{
    SimulatorOptions.PrintUsage();
    return 1;
}

Console.WriteLine($"Connecting to {options.ApiUrl} as station {options.StationId}…");

await using var connection = new HubConnectionBuilder()
    .WithUrl($"{options.ApiUrl.TrimEnd('/')}/hubs/terminal", http =>
    {
        if (!string.IsNullOrWhiteSpace(options.Token))
        {
            http.AccessTokenProvider = () => Task.FromResult<string?>(options.Token);
        }
    })
    .WithAutomaticReconnect()
    .Build();

try
{
    await connection.StartAsync();
    await connection.InvokeAsync(TerminalHubMethods.ToServer.RegisterStation, options.StationId, "simulator");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Could not reach the server: {ex.Message}");
    return 2;
}

Console.WriteLine("Connected.");

var epcs = options.Epcs.Count > 0 ? options.Epcs : GenerateEpcs(options.Count);

switch (options.Mode)
{
    case SimulatorMode.Burst:
        await BurstAsync(connection, options, epcs);
        break;

    case SimulatorMode.Stray:
        await StrayAsync(connection, options, epcs);
        break;

    case SimulatorMode.Stream:
        await StreamAsync(connection, options, epcs);
        break;

    default:
        break;
}

await connection.StopAsync();
return 0;

/// <summary>
/// A basket presented all at once — the scenario the 2-second, 300-tag exit criterion describes.
/// Each tag is reported several times, as a real reader would, so the coalescing and the read-count
/// floor are both exercised rather than bypassed.
/// </summary>
static async Task BurstAsync(HubConnection connection, SimulatorOptions options, IReadOnlyList<string> epcs)
{
    var now = DateTimeOffset.UtcNow;

    var tags = epcs
        .Select(epc => new TagRead(epc, options.Antenna, options.Rssi, options.Reads, now, now.AddMilliseconds(40)))
        .ToList();

    Console.WriteLine($"Publishing {tags.Count} tags in batches of {options.BatchSize}…");

    var stopwatch = Stopwatch.StartNew();

    foreach (var batch in tags.Chunk(options.BatchSize))
    {
        await connection.InvokeAsync(TerminalHubMethods.ToServer.PublishTags, options.StationId, batch);
    }

    stopwatch.Stop();

    Console.WriteLine(
        $"Published {tags.Count} tags in {stopwatch.ElapsedMilliseconds} ms "
        + $"({tags.Count / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds):F0} tags/s).");
}

/// <summary>
/// Reads that should be refused: too weak, and on an antenna pointed away from the till. If these end
/// up on the cart, the anti-false-positive controls are not doing their job.
/// </summary>
static async Task StrayAsync(HubConnection connection, SimulatorOptions options, IReadOnlyList<string> epcs)
{
    var now = DateTimeOffset.UtcNow;

    var strays = epcs
        .Select(epc => new TagRead(epc, (ushort)(options.Antenna + 100), -85, 1, now, now))
        .ToList();

    Console.WriteLine($"Publishing {strays.Count} stray reads (weak signal, non-checkout antenna)…");
    await connection.InvokeAsync(TerminalHubMethods.ToServer.PublishTags, options.StationId, strays);
    Console.WriteLine("Expect every one of these to be rejected in the live feed.");
}

/// <summary>A continuous dribble, for watching the feed and the read-rate indicator behave.</summary>
static async Task StreamAsync(HubConnection connection, SimulatorOptions options, IReadOnlyList<string> epcs)
{
    Console.WriteLine("Streaming. Press Ctrl+C to stop.");

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cts.Cancel();
    };

    var index = 0;

    while (!cts.IsCancellationRequested)
    {
        var now = DateTimeOffset.UtcNow;
        var epc = epcs[index++ % epcs.Count];

        try
        {
            await connection.InvokeAsync(
                TerminalHubMethods.ToServer.PublishTags,
                options.StationId,
                new[] { new TagRead(epc, options.Antenna, options.Rssi, options.Reads, now, now) },
                cts.Token);

            await Task.Delay(options.IntervalMs, cts.Token);
        }
        catch (OperationCanceledException)
        {
            break;
        }
    }
}

static IReadOnlyList<string> GenerateEpcs(int count)
{
    var epcs = new List<string>(count);
    var buffer = new byte[12];

    for (var i = 0; i < count; i++)
    {
        RandomNumberGenerator.Fill(buffer);

        // A fixed SGTIN-96 header keeps generated tags recognisable in logs and in the feed.
        buffer[0] = 0x30;
        epcs.Add(Convert.ToHexString(buffer));
    }

    return epcs;
}

internal enum SimulatorMode
{
    Burst,
    Stray,
    Stream,
}

internal sealed record SimulatorOptions(
    string ApiUrl,
    string StationId,
    string? Token,
    SimulatorMode Mode,
    int Count,
    int BatchSize,
    ushort Antenna,
    int Rssi,
    int Reads,
    int IntervalMs,
    IReadOnlyList<string> Epcs)
{
    public static SimulatorOptions? Parse(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = args[i][2..];
            var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++i]
                : "true";

            map[key] = value;
        }

        if (!map.TryGetValue("station", out var stationId) || string.IsNullOrWhiteSpace(stationId))
        {
            return null;
        }

        var mode = map.TryGetValue("mode", out var modeText) && Enum.TryParse<SimulatorMode>(modeText, true, out var parsed)
            ? parsed
            : SimulatorMode.Burst;

        return new SimulatorOptions(
            map.GetValueOrDefault("server", "http://localhost:5000"),
            stationId,
            map.GetValueOrDefault("token"),
            mode,
            Int(map, "count", 30),
            Int(map, "batch", 50),
            (ushort)Int(map, "antenna", 1),
            Int(map, "rssi", -55),
            Int(map, "reads", 3),
            Int(map, "interval", 500),
            map.TryGetValue("epcs", out var epcs)
                ? epcs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(e => e.ToUpperInvariant())
                    .ToList()
                : []);
    }

    private static int Int(IReadOnlyDictionary<string, string> map, string key, int fallback)
        => map.TryGetValue(key, out var text)
           && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    public static void PrintUsage()
    {
        Console.WriteLine("""
            Retail25 RFID simulator — a reader that does not exist.

            Usage:
              retail25-rfid --station <guid> [options]

            Options:
              --server    <url>    API base URL           (default http://localhost:5000)
              --token     <jwt>    Bearer token for the terminal hub
              --mode      <mode>   burst | stray | stream (default burst)
              --count     <n>      Tags to generate       (default 30)
              --epcs      <list>   Comma-separated EPCs, instead of generating them
              --batch     <n>      Tags per publish       (default 50)
              --antenna   <n>      Antenna number         (default 1)
              --rssi      <dbm>    Signal strength        (default -55)
              --reads     <n>      Reads per tag          (default 3)
              --interval  <ms>     Delay between stream publishes (default 500)

            Examples:
              # The 300-tag exit criterion
              retail25-rfid --station 1b8f… --mode burst --count 300

              # Reads that must be rejected: weak, and on a non-checkout antenna
              retail25-rfid --station 1b8f… --mode stray --count 5

              # Re-present a tag that has already been sold; expect epc.already_sold
              retail25-rfid --station 1b8f… --epcs 30ABC…
            """);
    }
}
