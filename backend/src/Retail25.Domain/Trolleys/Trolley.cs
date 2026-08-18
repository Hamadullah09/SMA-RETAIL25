using Retail25.Domain.Common;

namespace Retail25.Domain.Trolleys;

/// <summary>
/// A physical shopping trolley with a reader on it and a short code printed on the handle.
/// <para>
/// <b>Every trolley is backed by a <see cref="Terminals.Station"/> row</b>, and that is the load-bearing
/// decision in this feature. The cart aggregate, the pricing pipeline, the tag-ingest handler and the
/// realtime notifier are all written in terms of a station: <c>Cart.Open</c> takes one, the pricing
/// context is loaded from one, the Redis claim key is scoped to one, and the tax and price-level
/// resolution hangs off the location that one belongs to. Giving a trolley a station of its own means
/// every line of that — the most heavily tested code in the system, and the part where a mistake
/// charges somebody the wrong amount — runs completely unchanged.
/// </para>
/// <para>
/// The alternative was to widen the cart to accept "a station <em>or</em> a trolley", which buys
/// nothing a shopper can see and costs an edit to every one of those paths. What a trolley genuinely
/// needs beyond a station is a code a person can read off a handle and a notion of being claimed by
/// somebody; both live here, and neither belongs on a till.
/// </para>
/// <para>
/// The backing station is not a lane and never opens a drawer. It exists so that a trolley is, to the
/// rest of the system, just another place a sale can be rung.
/// </para>
/// </summary>
public sealed class Trolley : AggregateRoot, IAuditable
{
    // The wording says "counter" because that is what a shopper is standing at and what the app
    // calls it on screen. The error *codes* keep the trolley prefix: they are a stable contract that
    // clients match on, and renaming them to follow a change of vocabulary would break every caller
    // for no gain.
    public static readonly Error CodeInvalid =
        new("trolley.code_invalid", "A counter number must be 3–6 digits.");

    public static readonly Error TareInvalid =
        new("trolley.tare_invalid", "A trolley's empty weight must be more than zero.");

    public static readonly Error NotFound =
        new("trolley.not_found", "No counter has that number. Check the number shown at the counter.");

    public static readonly Error OutOfService =
        new("trolley.out_of_service", "That counter is out of service. Please use another one.");

    public static readonly Error AlreadyClaimed =
        new("trolley.already_claimed", "Someone else is already using that counter.");

    private Trolley()
    {
    }

    /// <summary>What is printed on the handle. Digits only, so it survives being read aloud.</summary>
    public string Code { get; private set; } = string.Empty;

    public long LocationId { get; private set; }

    /// <summary>
    /// The station row this trolley rings its sales through. See the type remarks — this is what lets
    /// the entire existing cart and pricing path work with no changes.
    /// </summary>
    public long StationId { get; private set; }

    /// <summary>Free text for staff — "bay 3", "the one with the wobbly wheel".</summary>
    public string? Label { get; private set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// What the trolley itself weighs, empty, in kilograms.
    /// <para>
    /// Recorded per trolley rather than as one number for the fleet because they are not identical:
    /// this shop's run between about 2.2 and 2.5 kg, and a scale that verifies a basket by weight has
    /// to subtract the right one. Treating 2.35 as universal would put every trolley up to 150 g out
    /// before a single item was in it, which is more than plenty of items weigh.
    /// </para>
    /// <para>
    /// Nullable on purpose: unknown and "weighs nothing" are different claims, and a trolley nobody
    /// has weighed yet must not silently report a tare of zero to whatever later does the arithmetic.
    /// </para>
    /// </summary>
    public decimal? TareWeightKg { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public static Result<Trolley> Create(
        long locationId,
        long stationId,
        string? code,
        string? label = null,
        decimal? tareWeightKg = null)
    {
        var trimmed = (code ?? string.Empty).Trim();

        if (trimmed.Length is < 3 or > 6 || !trimmed.All(char.IsAsciiDigit))
        {
            return Result.Failure<Trolley>(CodeInvalid.With("value", code));
        }

        if (tareWeightKg is { } tare && tare <= 0m)
        {
            return Result.Failure<Trolley>(TareInvalid.With("value", tare));
        }

        return Result.Success(new Trolley
        {
            LocationId = locationId,
            StationId = stationId,
            Code = trimmed,
            Label = label?.Trim(),
            TareWeightKg = tareWeightKg,
        });
    }

    /// <summary>
    /// Records what this trolley actually weighs, once somebody has put it on a scale.
    /// <para>
    /// Null clears it back to unknown, which is a real thing to want: a trolley whose wheels have
    /// been replaced no longer weighs what the sticker said, and an unknown tare is safer than a
    /// stale one.
    /// </para>
    /// </summary>
    public Result SetTareWeight(decimal? tareWeightKg)
    {
        if (tareWeightKg is { } tare && tare <= 0m)
        {
            return Result.Failure(TareInvalid.With("value", tare));
        }

        TareWeightKg = tareWeightKg;
        return Result.Success();
    }

    /// <summary>
    /// The comparison form for a code a shopper typed. Leading zeros are kept, because the handle
    /// says "0482" and a shopper who types what they see must be right.
    /// </summary>
    public static string NormalizeCode(string? code) => (code ?? string.Empty).Trim();

    public void SetLabel(string? label) => Label = label?.Trim();

    public void TakeOutOfService() => IsActive = false;

    public void ReturnToService() => IsActive = true;
}
