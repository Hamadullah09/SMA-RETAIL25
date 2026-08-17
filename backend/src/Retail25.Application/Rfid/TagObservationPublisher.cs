using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Retail25.Application.Abstractions;
using Retail25.Contracts.Terminals;

namespace Retail25.Application.Rfid;

/// <summary>
/// The gate between the antenna and the screen.
/// <para>
/// Every read the agent publishes passes through here on its way to <c>/hubs/rfid</c>. Its job is to
/// make sure a reader running flat out cannot drown the broadcast: five thousand raw reads a second
/// off four antennas is perhaps thirty distinct tags, and only those thirty are worth a frame.
/// </para>
/// <para>
/// Deliberately fire-and-forget from the caller's perspective and free of database work on the hot
/// path. EPC resolution — turning a tag into an item name — is a cache lookup, and a miss resolves
/// once and is remembered, because a stock count sweeping a rail would otherwise issue a query per
/// read.
/// </para>
/// </summary>
public sealed class TagObservationPublisher
{
    private readonly TagStreamRegistry _registry;
    private readonly IRfidNotifier _notifier;
    private readonly IApplicationDbContext _db;
    private readonly ILogger<TagObservationPublisher> _logger;

    public TagObservationPublisher(
        TagStreamRegistry registry,
        IRfidNotifier notifier,
        IApplicationDbContext db,
        ILogger<TagObservationPublisher> logger)
    {
        _registry = registry;
        _notifier = notifier;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Debounces a batch and broadcasts what survives.
    /// </summary>
    /// <returns>
    /// The tags that opened a window, in the order they arrived. Callers use it to suppress their own
    /// per-tag chatter — a rejection notice per raw read is the same flood in a different pipe.
    /// </returns>
    public async Task<IReadOnlyList<TagRead>> PublishAsync(
        long stationId,
        IReadOnlyList<TagRead> tags,
        CancellationToken ct = default)
    {
        if (tags.Count == 0)
        {
            return [];
        }

        var locationId = await LocationOfAsync(stationId, ct);
        var debouncer = _registry.For(stationId);

        List<TagRead>? admitted = null;

        foreach (var tag in tags)
        {
            if (debouncer.TryAdmit(tag.Epc))
            {
                (admitted ??= new List<TagRead>(tags.Count)).Add(tag);
            }
        }

        if (admitted is null)
        {
            // Everything folded into a window already in flight. The overwhelmingly common outcome
            // for a reader pointed at stock that is not moving.
            return [];
        }

        var resolved = await ResolveAsync(admitted, ct);

        var observations = new List<ObservedTag>(admitted.Count);

        foreach (var tag in admitted)
        {
            // The count the agent sent is what its own buffer coalesced; the debouncer's count is
            // what this process folded on top. The screen wants the total.
            var folded = debouncer.TryDescribe(tag.Epc, out var count, out _) ? count : 1;

            resolved.TryGetValue(tag.Epc, out var item);

            observations.Add(new ObservedTag(
                tag.Epc,
                tag.Antenna,
                tag.HasRssi ? tag.Rssi : null,
                Math.Max(tag.ReadCount, folded),
                tag.FirstSeen,
                tag.LastSeen,
                item.ProductId == 0L ? null : item.ProductId,
                item.StockCode,
                item.Name));
        }

        await _notifier.TagsObservedAsync(locationId, stationId, observations, ct);

        return admitted;
    }

    /// <summary>Reports the reader's own health alongside what the debouncer currently holds.</summary>
    public async Task PublishStatusAsync(
        long stationId,
        bool connected,
        int readsPerSecond,
        string mode,
        string? detail = null,
        CancellationToken ct = default)
        => await _notifier.ReaderStatusAsync(
            await LocationOfAsync(stationId, ct),
            stationId,
            new RfidReaderStatus(connected, readsPerSecond, _registry.For(stationId).TagsInField, mode, detail),
            ct);

    /// <summary>
    /// Which store a till belongs to, cached for the process lifetime.
    /// <para>
    /// Cached because it does not change — a till is bolted to a counter — and because looking it up
    /// per batch would put a database round trip on the path this class exists to keep clear.
    /// </para>
    /// </summary>
    private async Task<long> LocationOfAsync(long stationId, CancellationToken ct)
    {
        if (_registry.StationLocations.TryGetValue(stationId, out var known))
        {
            return known;
        }

        var locationId = await _db.Stations
            .AsNoTracking()
            .Where(s => s.Id == stationId)
            .Select(s => s.LocationId)
            .FirstOrDefaultAsync(ct);

        if (locationId != 0L)
        {
            _registry.StationLocations[stationId] = locationId;
        }

        return locationId;
    }

    /// <summary>
    /// Turns EPCs into item names, one query for everything not already known.
    /// <para>
    /// Unknown tags are cached as misses too. A shop always has some — a supplier's tag, a customer's
    /// own coat — and without a negative cache those are the tags that query the database hardest,
    /// because they are the ones that never resolve.
    /// </para>
    /// </summary>
    private async Task<Dictionary<string, (long ProductId, string? StockCode, string? Name)>> ResolveAsync(
        List<TagRead> tags,
        CancellationToken ct)
    {
        var result = new Dictionary<string, (long, string?, string?)>(tags.Count, StringComparer.Ordinal);
        List<string>? unknown = null;

        foreach (var tag in tags)
        {
            if (_registry.Catalogue.TryGetValue(tag.Epc, out var cached))
            {
                result[tag.Epc] = cached;
            }
            else
            {
                (unknown ??= new List<string>()).Add(tag.Epc);
            }
        }

        if (unknown is null)
        {
            return result;
        }

        try
        {
            var rows = await _db.SerializedUnits
                .AsNoTracking()
                .Where(u => u.Epc != null && unknown.Contains(u.Epc))
                .Join(
                    _db.Products.AsNoTracking(),
                    unit => unit.ProductId,
                    product => product.Id,
                    (unit, product) => new { unit.Epc, product.Id, product.StockCode, product.Name })
                .ToListAsync(ct);

            foreach (var row in rows)
            {
                var entry = (row.Id, (string?)row.StockCode, (string?)row.Name);
                _registry.Catalogue[row.Epc!] = entry;
                result[row.Epc!] = entry;
            }

            foreach (var epc in unknown.Where(e => !result.ContainsKey(e)))
            {
                var miss = (0L, (string?)null, (string?)null);
                _registry.Catalogue[epc] = miss;
                result[epc] = miss;
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // A resolution failure costs the item names, not the feed. An operator watching raw EPCs
            // still knows the reader is alive, which is more than a swallowed broadcast would tell them.
            _logger.LogWarning(error, "Could not resolve {Count} EPCs for the read feed.", unknown.Count);
        }

        return result;
    }
}

/// <summary>
/// One debouncer per station, plus the shared EPC→item cache.
/// <para>
/// Per station rather than one for the whole process: the same garment carried past two tills should
/// produce an observation at each, because each screen is showing its own antenna field. Deciding
/// which till gets to <em>sell</em> it is a different question, answered by <c>ITagDebouncer</c> in
/// Redis, across machines.
/// </para>
/// <para>
/// A singleton, so it outlives the scoped handlers that use it — a debounce window that resets with
/// every request would debounce nothing at all.
/// </para>
/// </summary>
public sealed class TagStreamRegistry
{
    private readonly ConcurrentDictionary<long, TagStreamDebouncer> _byStation = new();

    /// <summary>
    /// EPC → item, shared across stations because the mapping is a property of the tag, not the till.
    /// <see cref="0L"/> marks a tag known not to resolve.
    /// </summary>
    public ConcurrentDictionary<string, (long ProductId, string? StockCode, string? Name)> Catalogue { get; }
        = new(StringComparer.Ordinal);

    /// <summary>Station → store. Fixed for the life of the process; a till does not move between shops.</summary>
    public ConcurrentDictionary<long, long> StationLocations { get; } = new();

    public TagStreamDebouncer For(long stationId)
        => _byStation.GetOrAdd(stationId, static _ => new TagStreamDebouncer());

    /// <summary>Forgets a station's field. Called when its agent reconnects and the field is unknown.</summary>
    public void Reset(long stationId)
    {
        if (_byStation.TryGetValue(stationId, out var debouncer))
        {
            debouncer.Clear();
        }
    }

    /// <summary>
    /// Drops the EPC cache. Commissioning a tag changes what an EPC resolves to, and a cache that
    /// never forgets would keep showing "unknown" for a tag someone has just mapped.
    /// </summary>
    public void ForgetCatalogue(string epc) => Catalogue.TryRemove(epc, out _);
}
