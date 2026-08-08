using System.Collections.Concurrent;
using System.Diagnostics;

namespace Retail25.Application.Rfid;

/// <summary>
/// Collapses a torrent of raw tag reads into one observation per tag per window.
/// <para>
/// A UHF reader running four antennas in fast polling reports the same tag over and over — a garment
/// sitting in the field is read tens of times a second, per antenna. Broadcasting that verbatim would
/// push thousands of SignalR frames a second for a rail of clothes that is not moving. This holds the
/// first read of each EPC, counts the rest, and lets exactly one through per window.
/// </para>
/// <para>
/// Why this exists next to <c>ITagDebouncer</c>: that one is a distributed <em>claim</em>, in Redis,
/// answering "which till owns this tag" across machines, and it costs a network round trip. This one
/// is a per-process <em>rate gate</em> in front of the broadcast, and it has to survive five thousand
/// calls a second on the reader's own thread. Different question, different budget, different tool.
/// </para>
/// <para>
/// Deliberately lock-free. The hot path is a single <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// lookup and, on a repeat read, one interlocked increment on a slot that already exists — no
/// allocation at all for the case that happens 99% of the time.
/// </para>
/// </summary>
public sealed class TagStreamDebouncer
{
    /// <summary>The brief's window: one observation per tag per second.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How many admissions pass before stale entries are swept out.
    /// <para>
    /// Sweeping is amortised rather than timed because a timer on a component that may see no traffic
    /// for hours is a thread that wakes up to do nothing. It is triggered by admissions, not by every
    /// read, so a field full of motionless stock — millions of reads, no new tags — never sweeps and
    /// never needs to.
    /// </para>
    /// </summary>
    private const int SweepEvery = 512;

    /// <summary>
    /// A slot is discarded once it is this many windows stale. Not one window: a tag read at the very
    /// end of its window would be evicted a moment later and re-admitted immediately, which is exactly
    /// the duplicate the debounce exists to stop.
    /// </summary>
    private const int StaleWindows = 4;

    private readonly ConcurrentDictionary<string, Slot> _slots;
    private readonly long _windowTicks;

    private int _sinceSweep;

    /// <summary>Raw reads seen, ever. The numerator of the read rate an operator is shown.</summary>
    private long _observed;

    /// <summary>Reads that opened a window and will be broadcast.</summary>
    private long _admitted;

    public TagStreamDebouncer(TimeSpan? window = null, int concurrencyHint = 0, int capacityHint = 4096)
    {
        var effective = window ?? DefaultWindow;

        if (effective <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), effective, "The debounce window must be positive.");
        }

        _windowTicks = (long)(effective.TotalSeconds * Stopwatch.Frequency);

        // Sized up front. Growing a ConcurrentDictionary means taking every lock and rehashing, and
        // doing that on the reader's thread at five thousand reads a second is a visible stall.
        _slots = new ConcurrentDictionary<string, Slot>(
            concurrencyLevel: concurrencyHint > 0 ? concurrencyHint : Environment.ProcessorCount,
            capacity: capacityHint,
            comparer: StringComparer.Ordinal);
    }

    /// <summary>Distinct tags currently held — near enough "how many tags are in the field".</summary>
    public int TagsInField => _slots.Count;

    public long ObservedReads => Interlocked.Read(ref _observed);

    public long AdmittedReads => Interlocked.Read(ref _admitted);

    /// <summary>
    /// Offers a raw read. Returns true exactly once per EPC per window; false means the read was
    /// folded into an observation already in flight.
    /// </summary>
    /// <remarks>
    /// Timing comes from <see cref="Stopwatch.GetTimestamp"/>, not the wall clock. A monotonic source
    /// is not optional here: an NTP correction or a daylight-saving step would otherwise either open
    /// the gate for every tag at once or wedge it shut for an hour.
    /// </remarks>
    public bool TryAdmit(string epc)
    {
        ArgumentNullException.ThrowIfNull(epc);

        Interlocked.Increment(ref _observed);

        var now = Stopwatch.GetTimestamp();

        while (true)
        {
            if (_slots.TryGetValue(epc, out var slot))
            {
                var opened = Interlocked.Read(ref slot.WindowOpenedAt);

                if (now - opened < _windowTicks)
                {
                    // Inside the window: count it and say nothing. This is the overwhelmingly common
                    // branch, and it allocates nothing.
                    Interlocked.Increment(ref slot.ReadCount);
                    Interlocked.Exchange(ref slot.LastSeenAt, now);
                    return false;
                }

                // The window has lapsed. Exactly one caller wins the right to open the next one;
                // everyone else loses the compare-and-swap and loops back to fold into it.
                if (Interlocked.CompareExchange(ref slot.WindowOpenedAt, now, opened) != opened)
                {
                    continue;
                }

                Interlocked.Exchange(ref slot.ReadCount, 1);
                Interlocked.Exchange(ref slot.LastSeenAt, now);

                Admit();
                return true;
            }

            if (_slots.TryAdd(epc, new Slot(now)))
            {
                Admit();
                return true;
            }

            // Another thread added the same EPC between the lookup and the add. Loop; the next pass
            // takes the existing-slot branch.
        }
    }

    /// <summary>
    /// How many raw reads a tag has accumulated in its current window, and when they started.
    /// Returns false once the tag has been swept.
    /// </summary>
    public bool TryDescribe(string epc, out int readCount, out TimeSpan age)
    {
        if (_slots.TryGetValue(epc, out var slot))
        {
            readCount = (int)Interlocked.Read(ref slot.ReadCount);
            age = TimeSpan.FromSeconds(
                (double)(Stopwatch.GetTimestamp() - Interlocked.Read(ref slot.WindowOpenedAt)) / Stopwatch.Frequency);

            return true;
        }

        readCount = 0;
        age = TimeSpan.Zero;
        return false;
    }

    /// <summary>Forgets a tag, so the next read of it is admitted immediately.</summary>
    public bool Forget(string epc) => _slots.TryRemove(epc, out _);

    /// <summary>Drops everything. Used when a reader reconnects and the field state is unknown.</summary>
    public void Clear()
    {
        _slots.Clear();
        Interlocked.Exchange(ref _sinceSweep, 0);
    }

    /// <summary>
    /// Removes tags no longer in the field.
    /// <para>
    /// This is the whole answer to "does it leak". Without it, a shop that sees a hundred thousand
    /// distinct tags across a day holds a hundred thousand slots at closing time, and the dictionary
    /// only ever grows. Normally amortised behind <see cref="TryAdmit"/>; exposed because a test that
    /// asserts eviction should not have to guess at the trigger count.
    /// </para>
    /// </summary>
    public int Sweep()
    {
        var cutoff = Stopwatch.GetTimestamp() - (_windowTicks * StaleWindows);
        var removed = 0;

        foreach (var pair in _slots)
        {
            if (Interlocked.Read(ref pair.Value.LastSeenAt) >= cutoff)
            {
                continue;
            }

            // Re-checked under TryRemove's own comparison would be ideal, but ConcurrentDictionary
            // offers no compare-and-remove on a reference value. The race is benign: losing it means
            // a tag that has just come back into the field gets re-admitted one window early.
            if (_slots.TryRemove(pair.Key, out _))
            {
                removed++;
            }
        }

        return removed;
    }

    private void Admit()
    {
        Interlocked.Increment(ref _admitted);

        if (Interlocked.Increment(ref _sinceSweep) >= SweepEvery)
        {
            Interlocked.Exchange(ref _sinceSweep, 0);
            Sweep();
        }
    }

    /// <summary>
    /// A class rather than a struct on purpose. Interlocked operations need a stable address, and a
    /// struct stored in a ConcurrentDictionary is copied on every read — the increments would land on
    /// a copy and quietly vanish. One allocation per distinct tag per sweep cycle is the price, and
    /// the repeat-read path — the one that runs thousands of times a second — allocates nothing.
    /// </summary>
    private sealed class Slot
    {
        public Slot(long now)
        {
            WindowOpenedAt = now;
            LastSeenAt = now;
            ReadCount = 1;
        }

        public long WindowOpenedAt;
        public long LastSeenAt;
        public long ReadCount;
    }
}
