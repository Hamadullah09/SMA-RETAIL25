using Retail25.Contracts.Terminals;

namespace Retail25.TerminalAgent.Rfid;

/// <summary>
/// The agent-side coalescing window (doc 06 §2).
/// <para>
/// A reader reports the same tag twenty times a second. That is pure noise, and it must not cost a
/// round trip — a thirty-item basket would otherwise generate six hundred server calls a second. So
/// reads are folded here by EPC: the read count accumulates, the strongest RSSI wins, and the window
/// is what eventually leaves.
/// </para>
/// <para>
/// This layer deliberately does <b>not</b> arbitrate between tills. That needs a shared view and an
/// expiry, which is why the second debounce lives in Redis on the server.
/// </para>
/// </summary>
public sealed class TagBuffer
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private long _totalReads;

    /// <summary>Distinct tags currently held.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Total raw reads offered since the last <see cref="ResetRate"/>. Drives the read-rate display.</summary>
    public long TotalReads => Interlocked.Read(ref _totalReads);

    /// <summary>
    /// Folds a read into the window. The strongest signal is kept because it is the one most likely
    /// to have come from the basket rather than from the shelf behind it.
    /// </summary>
    public void Offer(TagRead read)
    {
        ArgumentNullException.ThrowIfNull(read);

        var epc = read.Epc.Trim().ToUpperInvariant();
        if (epc.Length == 0)
        {
            return;
        }

        Interlocked.Increment(ref _totalReads);

        lock (_gate)
        {
            if (_entries.TryGetValue(epc, out var existing))
            {
                // Strongest read wins the antenna, because the strongest is the one nearest the tag —
                // that is what makes "which zone is this item in" answerable on a multi-antenna
                // reader. When neither read carries a measurement, there is nothing to compare, so
                // the most recent antenna wins instead: a tag being carried past a gate should read
                // as where it is now, not where it first appeared.
                var betterSignal = read.HasRssi && read.Rssi > existing.Rssi;
                var noSignalEither = !read.HasRssi && existing.Rssi == TagRead.UnknownRssi;

                _entries[epc] = existing with
                {
                    ReadCount = existing.ReadCount + read.ReadCount,
                    Rssi = Math.Max(existing.Rssi, read.Rssi),
                    Antenna = betterSignal || noSignalEither ? read.Antenna : existing.Antenna,
                    FirstSeen = existing.FirstSeen < read.FirstSeen ? existing.FirstSeen : read.FirstSeen,
                    LastSeen = existing.LastSeen > read.LastSeen ? existing.LastSeen : read.LastSeen,
                };

                return;
            }

            _entries[epc] = new Entry(epc, read.Antenna, read.Rssi, read.ReadCount, read.FirstSeen, read.LastSeen);
        }
    }

    /// <summary>
    /// Takes up to <paramref name="maxBatchSize"/> tags and clears them. Capping the batch keeps a
    /// single flush from becoming a request large enough to time out on a slow shop network; the
    /// remainder simply goes out on the next tick.
    /// </summary>
    public IReadOnlyList<TagRead> Drain(int maxBatchSize)
    {
        lock (_gate)
        {
            if (_entries.Count == 0)
            {
                return [];
            }

            var take = maxBatchSize <= 0 ? _entries.Count : Math.Min(maxBatchSize, _entries.Count);

            // Oldest first, so a tag that has been sitting in the window is never starved by newer
            // arrivals when the batch is capped.
            var selected = _entries.Values
                .OrderBy(e => e.FirstSeen)
                .Take(take)
                .ToList();

            foreach (var entry in selected)
            {
                _entries.Remove(entry.Epc);
            }

            return selected
                .Select(e => new TagRead(e.Epc, e.Antenna, e.Rssi, e.ReadCount, e.FirstSeen, e.LastSeen))
                .ToList();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    public long ResetRate() => Interlocked.Exchange(ref _totalReads, 0);

    private sealed record Entry(
        string Epc,
        int Antenna,
        int Rssi,
        int ReadCount,
        DateTimeOffset FirstSeen,
        DateTimeOffset LastSeen);
}
