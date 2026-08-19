using Retail25.Contracts.Terminals;

namespace Retail25.TerminalAgent.Rfid;

/// <summary>
/// One reader's share of a drained window.
/// <para>
/// <c>ReaderId</c> is 0 for a reader the server has not registered — an agent still running on the
/// per-station profile. Those batches go out by station as they always did, which is what lets an
/// estate be upgraded one till at a time.
/// </para>
/// </summary>
public sealed record ReaderTagBatch(long ReaderId, IReadOnlyList<TagRead> Tags);

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
    private readonly Dictionary<(long ReaderId, string Epc), Entry> _entries = new();
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
    public void Offer(TagRead read) => Offer(0, read);

    /// <summary>
    /// The same, attributed to the reader that saw it.
    /// <para>
    /// Keyed on reader and tag together, never on the tag alone. One machine may drive several
    /// readers watching different doorways, and the same garment carried past two of them is two
    /// observations of two different places — folding them into one entry would lose whichever
    /// reader was second and send the read to the wrong station.
    /// </para>
    /// </summary>
    public void Offer(long readerId, TagRead read)
    {
        ArgumentNullException.ThrowIfNull(read);

        var epc = read.Epc.Trim().ToUpperInvariant();
        if (epc.Length == 0)
        {
            return;
        }

        var key = (readerId, epc);

        Interlocked.Increment(ref _totalReads);

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                // Strongest read wins the antenna, because the strongest is the one nearest the tag —
                // that is what makes "which zone is this item in" answerable on a multi-antenna
                // reader. When neither read carries a measurement, there is nothing to compare, so
                // the most recent antenna wins instead: a tag being carried past a gate should read
                // as where it is now, not where it first appeared.
                var betterSignal = read.HasRssi && read.Rssi > existing.Rssi;
                var noSignalEither = !read.HasRssi && existing.Rssi == TagRead.UnknownRssi;

                _entries[key] = existing with
                {
                    ReadCount = existing.ReadCount + read.ReadCount,
                    Rssi = Math.Max(existing.Rssi, read.Rssi),
                    Antenna = betterSignal || noSignalEither ? read.Antenna : existing.Antenna,
                    FirstSeen = existing.FirstSeen < read.FirstSeen ? existing.FirstSeen : read.FirstSeen,
                    LastSeen = existing.LastSeen > read.LastSeen ? existing.LastSeen : read.LastSeen,
                };

                return;
            }

            _entries[key] = new Entry(readerId, epc, read.Antenna, read.Rssi, read.ReadCount, read.FirstSeen, read.LastSeen);
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
                _entries.Remove((entry.ReaderId, entry.Epc));
            }

            return selected
                .Select(e => new TagRead(e.Epc, e.Antenna, e.Rssi, e.ReadCount, e.FirstSeen, e.LastSeen))
                .ToList();
        }
    }

    /// <summary>
    /// The same drain, split by the reader that saw each tag.
    /// <para>
    /// Needed because a batch now goes to the server addressed by reader, and the server resolves the
    /// station from the reader and antenna. A single flat list could not say which reader saw what,
    /// so on a machine driving three readers every read would have to be attributed to a guess.
    /// </para>
    /// <para>
    /// The cap applies to the drain as a whole rather than per reader: it exists to keep one request
    /// from growing large enough to time out, and three readers each sending a capped batch would
    /// defeat that.
    /// </para>
    /// </summary>
    public IReadOnlyList<ReaderTagBatch> DrainByReader(int maxBatchSize)
    {
        lock (_gate)
        {
            if (_entries.Count == 0)
            {
                return [];
            }

            var take = maxBatchSize <= 0 ? _entries.Count : Math.Min(maxBatchSize, _entries.Count);

            var selected = _entries.Values
                .OrderBy(e => e.FirstSeen)
                .Take(take)
                .ToList();

            foreach (var entry in selected)
            {
                _entries.Remove((entry.ReaderId, entry.Epc));
            }

            return selected
                .GroupBy(e => e.ReaderId)
                .Select(g => new ReaderTagBatch(
                    g.Key,
                    g.Select(e => new TagRead(e.Epc, e.Antenna, e.Rssi, e.ReadCount, e.FirstSeen, e.LastSeen))
                        .ToList()))
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
        long ReaderId,
        string Epc,
        int Antenna,
        int Rssi,
        int ReadCount,
        DateTimeOffset FirstSeen,
        DateTimeOffset LastSeen);
}
