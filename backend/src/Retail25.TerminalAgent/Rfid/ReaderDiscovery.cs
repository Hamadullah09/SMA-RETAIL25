using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Retail25.Devices.Rfid;

namespace Retail25.TerminalAgent.Rfid;

/// <summary>
/// Finds readers on whatever network this till is actually on.
///
/// <para>
/// Two jobs, deliberately in one place because they share a sweep. <see cref="FindAsync"/> re-finds
/// a single configured reader that has moved, which is what a running session needs when its address
/// stops answering. <see cref="DiscoverAsync"/> enumerates every reader on the network and asks each
/// one what it is, which is what an administrator needs when a shop is being set up and nobody wants
/// to type seven IP addresses.
/// </para>
///
/// <para>
/// A reader's address is a fact about a shop's DHCP lease, not about the software, and writing one
/// into a profile makes two promises that do not hold: that the reader keeps that address, and that
/// every shop uses the same numbering. Neither survives contact — a router reboot hands out a
/// different lease, and the next shop is on 10.x or 172.16.x with nothing in common. A till that
/// stops reading until somebody edits a settings page is a till that stops selling.
/// </para>
/// <para>
/// So the configured address is treated as a hint rather than an answer. It is tried first, because
/// when it is right this costs one connection and no scan. Only when it does not answer does this
/// sweep the networks this machine is actually attached to, looking for something listening on the
/// same port.
/// </para>
/// <para>
/// An open port is weak evidence and is deliberately not treated as proof. Nothing here claims to
/// have found a reader — it proposes an address, and the caller then speaks the actual protocol to
/// it. A wrong guess fails the handshake, the session ends, and the search runs again; the cost of
/// being wrong is one failed connect rather than a bad state that persists.
/// </para>
/// </summary>
public sealed class ReaderDiscovery
{
    private readonly ILogger<ReaderDiscovery> _logger;

    /// <summary>
    /// Short, because this runs against 254 addresses of which at most one answers. A reader on the
    /// same switch replies in single-digit milliseconds; anything slower than this is a host that is
    /// not there, and waiting longer only delays the till.
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Enough to sweep a /24 in about two seconds, few enough not to look like a port scan to
    /// whatever else is on the shop's network.
    /// </summary>
    private const int Parallelism = 48;

    /// <summary>
    /// How many candidates are asked the protocol question at once.
    /// <para>
    /// Far lower than <see cref="Parallelism"/> because these are conversations rather than knocks.
    /// After the cheap sweep there are single digits of them on a real shop network, so a wide fan-out
    /// would buy nothing and each one holds a socket open on hardware that allows exactly one client.
    /// </para>
    /// </summary>
    private const int IdentifyParallelism = 8;

    private readonly IReaderIdentityProbe _probe;

    public ReaderDiscovery(ILogger<ReaderDiscovery> logger, IReaderIdentityProbe probe)
    {
        _logger = logger;
        _probe = probe;
    }

    /// <summary>
    /// Every reader on this till's own networks, identified rather than merely listening.
    ///
    /// <para>
    /// Two phases, and the split is what makes this affordable. A /24 is 254 addresses, and asking
    /// each one three protocol questions would take minutes. So the cheap knock runs first — a TCP
    /// connect with a short timeout, wide open in parallel — and only the handful that answer are
    /// then asked to prove what they are. On a shop network that is seven conversations instead of
    /// seven hundred and sixty-two.
    /// </para>
    /// <para>
    /// <paramref name="inUse"/> matters more than it looks. This reader family accepts exactly one
    /// client at a time, so probing an address that a running session already holds cannot succeed —
    /// and if it were treated as a failure, every rescan would report the readers that are working
    /// as missing. They are skipped and reported from what the session already knows instead.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<ReaderIdentity>> DiscoverAsync(
        int port,
        IReadOnlySet<string> inUse,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(inUse);

        var candidates = LocalCandidates(port)
            .Select(address => address.ToString())
            .Where(address => !inUse.Contains(address))
            .ToList();

        if (candidates.Count == 0)
        {
            _logger.LogWarning("No IPv4 network this till is attached to could be searched on port {Port}", port);
            return [];
        }

        var listening = await ListeningAsync(candidates, port, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Swept {Candidates} addresses on port {Port}: {Listening} answered, asking each whether it is a reader",
            candidates.Count,
            port,
            listening.Count);

        var identified = await IdentifyAsync(listening, port, ct).ConfigureAwait(false);

        // Deduplicated on the reader's own identifier, because one physical reader can answer on two
        // addresses — a unit with both interfaces up, or a stale ARP entry during a lease change.
        // Falling back to the address where a unit reports no identifier keeps those separate, which
        // is the safe direction: two rows an administrator can merge beat one reader silently
        // swallowing another's antennas.
        var distinct = identified
            .GroupBy(reader => reader.SerialNumber ?? $"@{reader.Host}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        _logger.LogInformation(
            "Discovery found {Count} reader(s) on port {Port}: {Readers}",
            distinct.Count,
            port,
            string.Join(", ", distinct.Select(r => $"{r.SerialNumber ?? "unidentified"}@{r.Host}")));

        return distinct;
    }

    /// <summary>The addresses that accept a connection, all of them rather than the first.</summary>
    private static async Task<IReadOnlyList<string>> ListeningAsync(
        IReadOnlyList<string> candidates,
        int port,
        CancellationToken ct)
    {
        using var slots = new SemaphoreSlim(Parallelism);
        var answered = new List<string>();

        var probes = candidates.Select(async address =>
        {
            await slots.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                if (await AnswersAsync(address, port, ct).ConfigureAwait(false))
                {
                    lock (answered)
                    {
                        answered.Add(address);
                    }
                }
            }
            finally
            {
                slots.Release();
            }
        });

        await Task.WhenAll(probes).ConfigureAwait(false);

        return answered;
    }

    /// <summary>Asks each listening address the protocol question, keeping only what answers as a reader.</summary>
    private async Task<IReadOnlyList<ReaderIdentity>> IdentifyAsync(
        IReadOnlyList<string> listening,
        int port,
        CancellationToken ct)
    {
        using var slots = new SemaphoreSlim(IdentifyParallelism);
        var found = new List<ReaderIdentity>();

        var probes = listening.Select(async address =>
        {
            await slots.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                var identity = await _probe.ProbeAsync(address, port, ct).ConfigureAwait(false);

                if (identity is not null)
                {
                    lock (found)
                    {
                        found.Add(identity);
                    }
                }
            }
            finally
            {
                slots.Release();
            }
        });

        await Task.WhenAll(probes).ConfigureAwait(false);

        return found;
    }

    /// <summary>
    /// Returns an address listening on <paramref name="port"/>, or null if nothing is.
    /// </summary>
    public async Task<string?> FindAsync(string? configuredHost, int port, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(configuredHost)
            && await AnswersAsync(configuredHost!, port, ct).ConfigureAwait(false))
        {
            return configuredHost;
        }

        var candidates = LocalCandidates(port).ToList();

        if (candidates.Count == 0)
        {
            _logger.LogWarning(
                "No IPv4 network this till is attached to could be searched for a reader on port {Port}", port);

            return null;
        }

        _logger.LogInformation(
            "Reader did not answer at {Configured}:{Port}; searching {Count} addresses on this till's own networks",
            string.IsNullOrWhiteSpace(configuredHost) ? "(unset)" : configuredHost,
            port,
            candidates.Count);

        var found = await FirstAnswerAsync(candidates, port, ct).ConfigureAwait(false);

        if (found is not null)
        {
            _logger.LogInformation("Found something listening on {Host}:{Port}; trying it as the reader", found, port);
        }
        else
        {
            _logger.LogWarning("Nothing on this till's networks is listening on port {Port}", port);
        }

        return found;
    }

    /// <summary>
    /// Probes in bounded parallel and stops at the first answer, so a reader early in the range is
    /// found in milliseconds rather than after the whole sweep.
    /// </summary>
    private static async Task<string?> FirstAnswerAsync(IReadOnlyList<IPAddress> candidates, int port, CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var slots = new SemaphoreSlim(Parallelism);

        string? winner = null;

        var probes = candidates.Select(async address =>
        {
            await slots.WaitAsync(stop.Token).ConfigureAwait(false);

            try
            {
                if (await AnswersAsync(address.ToString(), port, stop.Token).ConfigureAwait(false))
                {
                    // First writer wins; the rest are cancelled rather than left running.
                    if (Interlocked.CompareExchange(ref winner, address.ToString(), null) is null)
                    {
                        await stop.CancelAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                try
                {
                    slots.Release();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        });

        try
        {
            await Task.WhenAll(probes).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        ct.ThrowIfCancellationRequested();

        return Volatile.Read(ref winner);
    }

    private static async Task<bool> AnswersAsync(string host, int port, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);

            timeout.CancelAfter(ProbeTimeout);

            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);

            return client.Connected;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Refused, unreachable, timed out, unresolvable — all the same answer: not here.
            return false;
        }
    }

    /// <summary>
    /// The addresses on this machine's own IPv4 networks, excluding its own.
    /// <para>
    /// Bounded to a /24 around each interface deliberately. A mask wider than that — a /16 is common
    /// on a badly configured network — is 65,000 probes, which is both far too slow to run while a
    /// cashier waits and indistinguishable from scanning somebody's network. The reader is on the
    /// same switch as the till in every real installation, so the narrow sweep is the useful one.
    /// </para>
    /// </summary>
    private static IEnumerable<IPAddress> LocalCandidates(int port)
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                var octets = unicast.Address.GetAddressBytes();

                // 169.254.x is a link-local address handed out when DHCP failed. There is no reader
                // on it, and sweeping it wastes the seconds before the real interface is tried.
                if (octets[0] == 169 && octets[1] == 254)
                {
                    continue;
                }

                // This machine's own address is included rather than skipped. Skipping it saves one
                // probe in 254 and costs a real case: a serial-to-Ethernet bridge, or a vendor's
                // reader service, running on the till itself answers on the till's own LAN address.
                for (var host = 1; host <= 254; host++)
                {
                    yield return new IPAddress([octets[0], octets[1], octets[2], (byte)host]);
                }
            }
        }
    }
}
