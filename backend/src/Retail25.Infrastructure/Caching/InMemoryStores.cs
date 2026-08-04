using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Retail25.Application.Abstractions;
using Retail25.Application.Behaviors;

namespace Retail25.Infrastructure.Caching;

/// <summary>
/// Single-process stand-ins for the four Redis-backed stores, for a machine with no Redis on it.
/// <para>
/// <b>What this gives up.</b> Redis is not a cache here — it is the shared memory that makes several
/// tills one system. Holding this state in a process instead means:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>No cross-till tag arbitration.</b> Two tills could put the same physical garment on two
///     carts and sell it twice. This is the serious one, and it is the reason this is opt-in.
///   </item>
///   <item><b>No suspended-cart recall at another till</b>, and no cart survives a restart.</item>
///   <item><b>Idempotency is per process</b>, so a retry that lands on a second instance re-runs.</item>
///   <item><b>Hub tickets are not shared</b>, so a ticket minted by one instance is not redeemable at another.</item>
/// </list>
/// <para>
/// All of which is fine for exactly one shape of deployment: a single API process serving a single
/// till, which is a development bench or a one-register shop. It is refused outright in Production —
/// see <c>DependencyInjection.AddCaching</c> — because the failure mode is silent, and "we sold that
/// coat twice" is discovered by the stock count weeks later.
/// </para>
/// </summary>
internal static class InMemoryStoreNotes
{
    public const string Caveat =
        "Cart state, tag claims, idempotency and hub tickets are held in this process only. "
        + "Cross-till tag arbitration is NOT active: two tills could sell the same tagged item. "
        + "Single-process deployments only.";
}

/// <summary>
/// The active cart, in process.
/// <para>
/// Two dictionaries rather than one, mirroring the two Redis keys: carts by id, and the one active
/// cart per station. They are written under a lock so a cart and its station pointer cannot
/// disagree — a station pointing at a cart that no longer exists is a till that cannot start a sale
/// and cannot say why.
/// </para>
/// </summary>
public sealed class InMemoryCartStore : ICartStore
{
    private readonly ConcurrentDictionary<long, CartSnapshot> _carts = new();
    private readonly ConcurrentDictionary<long, long> _byStation = new();
    private readonly object _gate = new();

    public Task<CartSnapshot?> GetAsync(long cartId, CancellationToken ct = default)
        => Task.FromResult(_carts.GetValueOrDefault(cartId));

    public Task<CartSnapshot?> GetByStationAsync(long stationId, CancellationToken ct = default)
    {
        if (_byStation.TryGetValue(stationId, out var cartId) && _carts.TryGetValue(cartId, out var snapshot))
        {
            return Task.FromResult<CartSnapshot?>(snapshot);
        }

        return Task.FromResult<CartSnapshot?>(null);
    }

    public Task SaveAsync(CartSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_gate)
        {
            _carts[snapshot.Cart.Id] = snapshot;
            _byStation[snapshot.Cart.StationId] = snapshot.Cart.Id;
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(long cartId, long stationId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _carts.TryRemove(cartId, out _);

            // Only if it still points at this cart. A station that has already opened its next sale
            // must not have that sale cleared by the previous one finishing.
            if (_byStation.TryGetValue(stationId, out var current) && current == cartId)
            {
                _byStation.TryRemove(stationId, out _);
            }
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// The tag claim, in process.
/// <para>
/// Honest about its limits: within one process this is a correct compare-and-swap, and two tills
/// served by the <em>same</em> API instance really are arbitrated. What it cannot do is arbitrate
/// across instances, because it has no shared medium to do it in.
/// </para>
/// <para>
/// Timing is monotonic — an NTP step against the wall clock would either release every claim at once
/// or hold them all for an hour.
/// </para>
/// </summary>
public sealed class InMemoryTagDebouncer : ITagDebouncer
{
    private readonly ConcurrentDictionary<string, Claim> _claims = new(StringComparer.Ordinal);

    public Task<bool> TryClaimAsync(string epc, long stationId, TimeSpan window, CancellationToken ct = default)
    {
        var now = Stopwatch.GetTimestamp();
        var expiry = now + (long)(window.TotalSeconds * Stopwatch.Frequency);

        while (true)
        {
            if (_claims.TryGetValue(epc, out var existing))
            {
                if (existing.ExpiresAt > now && existing.StationId != stationId)
                {
                    return Task.FromResult(false);
                }

                // Ours already, or lapsed. Either way we may take it — but only if nobody else did
                // between the read and the write.
                if (!_claims.TryUpdate(epc, new Claim(stationId, expiry), existing))
                {
                    continue;
                }

                return Task.FromResult(true);
            }

            if (_claims.TryAdd(epc, new Claim(stationId, expiry)))
            {
                return Task.FromResult(true);
            }
        }
    }

    public Task ReleaseAsync(string epc, long stationId, CancellationToken ct = default)
    {
        // Only the holder may release. Otherwise a second till could free a claim it does not own and
        // then immediately take it.
        if (_claims.TryGetValue(epc, out var existing) && existing.StationId == stationId)
        {
            _claims.TryRemove(new KeyValuePair<string, Claim>(epc, existing));
        }

        return Task.CompletedTask;
    }

    public Task<long?> GetHolderAsync(string epc, CancellationToken ct = default)
    {
        if (_claims.TryGetValue(epc, out var existing) && existing.ExpiresAt > Stopwatch.GetTimestamp())
        {
            return Task.FromResult<long?>(existing.StationId);
        }

        return Task.FromResult<long?>(null);
    }

    private sealed record Claim(long StationId, long ExpiresAt);
}

/// <summary>
/// Replayed command responses, in process.
/// <para>
/// Entries are swept on write rather than by a timer: this is only ever consulted seconds after a
/// command ran, and a process that has stopped taking commands does not need a thread waking up to
/// tidy a dictionary nobody is reading.
/// </para>
/// </summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    /// <summary>Long enough to cover a retry after a timeout; short enough that it is not a cache.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public Task<T?> GetResponseAsync<T>(string key, CancellationToken ct = default)
    {
        if (!_entries.TryGetValue(key, out var entry) || entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return Task.FromResult<T?>(default);
        }

        // Round-tripped through JSON exactly as the Redis store does, so a handler cannot be handed
        // back the very object it returned and mutate a "stored" response by accident.
        return Task.FromResult(JsonSerializer.Deserialize<T>(entry.Json));
    }

    public Task StoreResponseAsync<T>(string key, T response, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        _entries[key] = new Entry(JsonSerializer.Serialize(response), now.Add(Lifetime));

        if (_entries.Count > 512)
        {
            foreach (var stale in _entries.Where(e => e.Value.ExpiresAt <= now))
            {
                _entries.TryRemove(stale.Key, out _);
            }
        }

        return Task.CompletedTask;
    }

    private sealed record Entry(string Json, DateTimeOffset ExpiresAt);
}

/// <summary>
/// Single-use SignalR tickets, in process.
/// <para>
/// The security properties that matter are preserved in full: 32 bytes of CSPRNG output, consumed on
/// redemption, and expiring on time. What is lost is only that a ticket minted by one instance
/// cannot be redeemed at another.
/// </para>
/// </summary>
public sealed class InMemoryHubTicketStore : IHubTicketStore
{
    private readonly ConcurrentDictionary<string, Entry> _tickets = new(StringComparer.Ordinal);

    public Task<string> IssueAsync(HubTicket ticket, TimeSpan lifetime, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var value = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        _tickets[value] = new Entry(ticket, DateTimeOffset.UtcNow.Add(lifetime));

        Sweep();

        return Task.FromResult(value);
    }

    public Task<HubTicket?> RedeemAsync(string ticket, CancellationToken ct = default)
    {
        // Removed, not read: single-use is the whole point, and a ticket left behind after a
        // successful connection is a ticket a second connection could use.
        if (_tickets.TryRemove(ticket, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return Task.FromResult<HubTicket?>(entry.Ticket);
        }

        return Task.FromResult<HubTicket?>(null);
    }

    private void Sweep()
    {
        if (_tickets.Count < 256)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        foreach (var stale in _tickets.Where(t => t.Value.ExpiresAt <= now))
        {
            _tickets.TryRemove(stale.Key, out _);
        }
    }

    private sealed record Entry(HubTicket Ticket, DateTimeOffset ExpiresAt);
}

/// <summary>Says once, at startup, exactly what has been given up. Nobody should discover this later.</summary>
public sealed class InMemoryStoreWarning
{
    public InMemoryStoreWarning(ILogger<InMemoryStoreWarning> logger)
        => logger.LogWarning("Running without Redis. {Caveat}", InMemoryStoreNotes.Caveat);
}
