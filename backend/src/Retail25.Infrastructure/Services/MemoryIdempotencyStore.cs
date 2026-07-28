using System.Collections.Concurrent;
using Retail25.Application.Behaviors;

namespace Retail25.Infrastructure.Services;

/// <summary>
/// Remembers the response to a command so a retry returns the original answer instead of doing the
/// work twice.
/// <para>
/// This matters most at the till. A cashier whose network drops mid-payment will press the button
/// again; without this, the second press commits a second sale. The client sends the same
/// idempotency key both times and gets the first result back.
/// </para>
/// <para>
/// Entries are held in memory and expire after a short window — long enough to cover a retry, short
/// enough that the store cannot grow without bound. With more than one API instance this must move
/// to Redis, since a retry may land on a different instance; that is the same decision as the cart
/// store and follows the same connection string.
/// </para>
/// </summary>
public sealed class MemoryIdempotencyStore : IIdempotencyStore
{
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public Task<T?> GetResponseAsync<T>(string key, CancellationToken ct = default)
    {
        Prune();

        if (_entries.TryGetValue(key, out var entry) && entry.Response is T typed)
        {
            return Task.FromResult<T?>(typed);
        }

        return Task.FromResult<T?>(default);
    }

    public Task StoreResponseAsync<T>(string key, T response, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(key) && response is not null)
        {
            _entries[key] = new Entry(response, DateTimeOffset.UtcNow.Add(Retention));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Drops expired entries opportunistically rather than on a timer: the store is only consulted
    /// during a request, so there is no value in a background thread waking to tidy an empty
    /// dictionary.
    /// </summary>
    private void Prune()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var pair in _entries)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                _entries.TryRemove(pair.Key, out _);
            }
        }
    }

    private sealed record Entry(object Response, DateTimeOffset ExpiresAt);
}
