using System.Collections.Concurrent;
using RetailPlus.Core.Model;

namespace RetailPlus.Core.Rules;

/// <summary>
/// The flag a tagged piece shows at the till: 1 in stock and ready to sell, 2 sold, 3 not sold but read at the exit
/// gate (the alarm went off). Lost has no flag number.
/// </summary>
public static class TagFlags
{
    /// <summary>The flag number of a status, or null for Lost.</summary>
    public static int? Number(ItemTagStatus status) => status switch
    {
        ItemTagStatus.InStock => 1,
        ItemTagStatus.Sold => 2,
        ItemTagStatus.Unsold => 3,
        _ => null,
    };

    public static string Name(ItemTagStatus status) => status switch
    {
        ItemTagStatus.InStock => "In stock, ready to sell",
        ItemTagStatus.Sold => "Sold",
        ItemTagStatus.Unsold => "Unsold at the gate",
        ItemTagStatus.Lost => "Lost",
        _ => status.ToString(),
    };

    /// <summary>"1 · In stock, ready to sell", "3 · Unsold at the gate", "Lost".</summary>
    public static string Text(ItemTagStatus status) => Number(status) is int n ? $"{n} · {Name(status)}" : Name(status);

    /// <summary>
    /// True when the piece is still the shop's stock: in stock, or unsold at the gate (it was not sold, so it still
    /// counts for the tagged-item sale limit).
    /// </summary>
    public static bool IsShopStock(ItemTagStatus status) => status is ItemTagStatus.InStock or ItemTagStatus.Unsold;
}

/// <summary>What a read at an exit-gate antenna leads to.</summary>
public enum GateOutcome
{
    /// <summary>The same tag was read moments ago: the same pass through the gate, nothing to do.</summary>
    Repeated,
    /// <summary>No registered tag has this code: no alarm (it cannot be told whether it was sold).</summary>
    Unknown,
    /// <summary>The piece was sold: no alarm.</summary>
    Sold,
    /// <summary>The piece is already written off as lost: no alarm.</summary>
    Lost,
    /// <summary>Not sold: a new alarm, and the tag becomes flag 3.</summary>
    NewAlarm,
    /// <summary>The piece already has an open alarm: the alarm window comes back and sounds again, without a new record.</summary>
    OpenAlarm,
}

/// <summary>The rule of the exit gate, apart from the database and the screen.</summary>
public static class GateRules
{
    /// <summary>How long a tag must be out of the gate's field before a read counts as a new pass.</summary>
    public static readonly TimeSpan DefaultQuietTime = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The outcome of a gate read of a registered tag (<paramref name="status"/> null: no tag has the code). A sold or
    /// lost piece never sounds the alarm; any other piece does, once: while its alarm is open, it only brings the alarm back.
    /// </summary>
    public static GateOutcome Decide(ItemTagStatus? status, bool hasOpenAlarm) => status switch
    {
        null => GateOutcome.Unknown,
        ItemTagStatus.Sold => GateOutcome.Sold,
        ItemTagStatus.Lost => GateOutcome.Lost,
        _ when hasOpenAlarm => GateOutcome.OpenAlarm,
        _ => GateOutcome.NewAlarm,
    };

    /// <summary>True when the outcome sounds the alarm at the till.</summary>
    public static bool Sounds(GateOutcome outcome) => outcome is GateOutcome.NewAlarm or GateOutcome.OpenAlarm;

    /// <summary>The flag a resolution leaves on the tag.</summary>
    public static ItemTagStatus StatusAfter(GateAlarmResolution resolution) => resolution switch
    {
        GateAlarmResolution.BackInStock => ItemTagStatus.InStock,
        GateAlarmResolution.LeftStore => ItemTagStatus.Lost,
        GateAlarmResolution.SoldAfterwards => ItemTagStatus.Sold,
        _ => ItemTagStatus.Unsold,
    };

    public static string Describe(GateAlarmResolution resolution) => resolution switch
    {
        GateAlarmResolution.Open => "Open",
        GateAlarmResolution.BackInStock => "Back in stock",
        GateAlarmResolution.LeftStore => "Left the store",
        GateAlarmResolution.SoldAfterwards => "Sold afterwards",
        _ => resolution.ToString(),
    };
}

/// <summary>
/// A gate antenna reads a tag many times while it passes (every round, and the hub reports it again every few seconds
/// while it stays in the field). A read counts as a new pass only when the tag was not read for
/// <see cref="QuietTime"/>; every read, counted or not, starts the quiet time again. Safe to use from several threads.
/// </summary>
public sealed class GateDebouncer(TimeSpan? quietTime = null)
{
    private readonly ConcurrentDictionary<string, DateTime> _lastRead = new(StringComparer.Ordinal);

    public TimeSpan QuietTime { get; } = quietTime ?? GateRules.DefaultQuietTime;

    /// <summary>True when the read at <paramref name="at"/> is a new pass of <paramref name="epc"/> through the gate.</summary>
    public bool IsNewPass(string epc, DateTime at)
    {
        bool fresh = true;
        _lastRead.AddOrUpdate(epc, at, (_, last) =>
        {
            fresh = at - last >= QuietTime;
            return at > last ? at : last;
        });
        if (_lastRead.Count > 10_000) Prune(at);
        return fresh;
    }

    private void Prune(DateTime now)
    {
        foreach (var (epc, at) in _lastRead)
            if (now - at > QuietTime + QuietTime) _lastRead.TryRemove(epc, out _);
    }
}
