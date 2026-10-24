namespace Retail25.Application.Carts.Services;

/// <summary>
/// One writer at a time per cart.
/// <para>
/// Every change to a cart is read, change, save: the snapshot comes out of the cart store, lines are
/// added to the copy, and the copy goes back. Two of those running at once on the same cart both read
/// the same starting point, and whichever saves second silently discards the other's change.
/// </para>
/// <para>
/// A till with a running reader does exactly that all day. The agent posts tag batches on its own
/// schedule; the cashier scans a barcode on theirs. <c>RfidCheckout</c> holds its copy across a dozen
/// awaits per tag — claiming it, resolving the EPC, building the line — which is a wide window for a
/// barcode to land in and be overwritten. Both saves also bump the revision to the same number, so the
/// till then ignores one of the two pushes as "already seen", and the screen and the server disagree
/// about what is in the sale. The cashier's report of it was "the reader works but the barcode does
/// not".
/// </para>
/// <para>
/// Keyed per cart rather than striped, so two different carts never wait on each other and a nested
/// acquire for a different cart cannot deadlock on a shared stripe. Entries are reference-counted and
/// dropped when the last holder leaves, so a day's worth of carts does not accumulate.
/// </para>
/// <para>
/// In-process, and that is the deployed topology: the SQL Server cache provider has no SignalR
/// backplane, which already confines the API to one instance. Running more than one instance behind
/// Redis would need this replaced by a distributed lock — the same constraint, made visible here
/// rather than discovered later.
/// </para>
/// </summary>
public sealed class CartLocks
{
    private readonly Dictionary<long, Entry> _entries = new();
    private readonly object _gate = new();

    /// <summary>
    /// Waits for the cart and returns a handle that releases it. Callers must read the cart
    /// <em>after</em> this returns: a snapshot read before acquiring is exactly the stale copy this
    /// exists to prevent.
    /// </summary>
    public async Task<IDisposable> AcquireAsync(long cartId, CancellationToken ct)
    {
        Entry entry;

        lock (_gate)
        {
            if (!_entries.TryGetValue(cartId, out var existing))
            {
                existing = new Entry();
                _entries[cartId] = existing;
            }

            existing.Users++;
            entry = existing;
        }

        try
        {
            await entry.Semaphore.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // Cancelled while queued: this caller never held the cart, so it must not release it,
            // only stop counting itself as a user.
            Forget(cartId, entry);
            throw;
        }

        return new Releaser(this, cartId, entry);
    }

    /// <summary>
    /// How many carts currently have a holder or a waiter. Exists so a test can prove entries are
    /// dropped — the difference between a lock and a slow leak of one semaphore per sale.
    /// </summary>
    public int TrackedCount
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    private void Release(long cartId, Entry entry)
    {
        entry.Semaphore.Release();
        Forget(cartId, entry);
    }

    private void Forget(long cartId, Entry entry)
    {
        lock (_gate)
        {
            entry.Users--;

            if (entry.Users == 0)
            {
                _entries.Remove(cartId);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        /// <summary>The holder plus everyone queued. The entry is only removed when this reaches zero.</summary>
        public int Users { get; set; }
    }

    private sealed class Releaser : IDisposable
    {
        private readonly CartLocks _owner;
        private readonly long _cartId;
        private readonly Entry _entry;
        private int _released;

        public Releaser(CartLocks owner, long cartId, Entry entry)
        {
            _owner = owner;
            _cartId = cartId;
            _entry = entry;
        }

        public void Dispose()
        {
            // Idempotent: a double dispose must not release a semaphore somebody else now holds.
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _owner.Release(_cartId, _entry);
            }
        }
    }
}
