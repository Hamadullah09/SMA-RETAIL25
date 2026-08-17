using Retail25.Domain.Common;

namespace Retail25.Domain.Trolleys;

/// <summary>Where a shopping trip has got to.</summary>
public enum TrolleySessionState
{
    /// <summary>Claimed and filling. Exactly one session per trolley may be in this state.</summary>
    Shopping = 0,

    /// <summary>Paid for. The cart is closed but the shopper has not yet passed the exit gate.</summary>
    Paid = 1,

    /// <summary>Finished — trolley returned to the pool and free to be claimed again.</summary>
    Released = 2,

    /// <summary>Timed out or given up without paying. Staff recover the goods.</summary>
    Abandoned = 3,
}

/// <summary>
/// One shopping trip: this shopper, this trolley, this cart, from the moment the code was typed to
/// the moment the trolley is free again.
/// <para>
/// This row is the <b>authorisation record</b> for the whole shopper API, and that is its real job.
/// A shopper's token carries no permissions, so "may I see this cart?" cannot be answered by the
/// permission behaviour the way it is for staff. It is answered here instead: a cart belongs to you
/// if a live session says it does. Every shopper-facing handler starts by loading this row, and
/// there is no other route to a cart id.
/// </para>
/// <para>
/// A trolley may hold only one <see cref="TrolleySessionState.Shopping"/> session at a time, enforced
/// by a filtered unique index rather than by a check-then-insert in a handler — two phones typing the
/// same code in the same second is exactly the race a handler-level check loses, and the loser would
/// otherwise silently join someone else's shopping trip.
/// </para>
/// </summary>
public sealed class TrolleySession : AggregateRoot, IAuditable
{
    public static readonly Error NotYours =
        new("trolley_session.not_yours", "That basket belongs to a different shopper.");

    public static readonly Error NotShopping =
        new("trolley_session.not_shopping", "This shopping trip has already been closed.");

    public static readonly Error AlreadyShopping =
        new("trolley_session.already_shopping", "You are already connected to another counter.");

    private TrolleySession()
    {
    }

    public long TrolleyId { get; private set; }

    public long ShopperId { get; private set; }

    /// <summary>The cart being filled. Set once, at claim time, and never reassigned.</summary>
    public long CartId { get; private set; }

    public long LocationId { get; private set; }

    public TrolleySessionState State { get; private set; } = TrolleySessionState.Shopping;

    public DateTimeOffset ClaimedAt { get; private set; }

    /// <summary>
    /// Bumped on every tag read and every screen the shopper opens. What the abandonment sweep reads:
    /// a trolley parked in an aisle for an hour has to return to the pool, and time since the trolley
    /// was <em>claimed</em> is the wrong clock for that — a genuine big shop is long.
    /// </summary>
    public DateTimeOffset LastActivityAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    /// <summary>The completed sale, once there is one. Null until payment succeeds.</summary>
    public long? SaleId { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public bool IsLive => State == TrolleySessionState.Shopping;

    public static TrolleySession Claim(
        long trolleyId,
        long shopperId,
        long cartId,
        long locationId,
        DateTimeOffset now)
    {
        return new TrolleySession
        {
            TrolleyId = trolleyId,
            ShopperId = shopperId,
            CartId = cartId,
            LocationId = locationId,
            State = TrolleySessionState.Shopping,
            ClaimedAt = now,
            LastActivityAt = now,
        };
    }

    /// <summary>
    /// Whether this session may be acted on by the given shopper. Both halves matter: a session that
    /// belongs to someone else is a different failure from one that is simply over, and the shopper
    /// deserves to be told which.
    /// </summary>
    public Result AuthorizeFor(long shopperId)
    {
        if (ShopperId != shopperId)
        {
            return Result.Failure(NotYours);
        }

        return State == TrolleySessionState.Shopping
            ? Result.Success()
            : Result.Failure(NotShopping.With("state", State.ToString()));
    }

    public void Touch(DateTimeOffset now) => LastActivityAt = now;

    /// <summary>
    /// Points the session at a different cart, because the one it held no longer exists.
    /// <para>
    /// A cart lives in the cart store, which is a cache: it is evicted on a TTL, and with the
    /// in-memory provider it is lost outright when the API restarts. The session row is in the
    /// database and survives all of that, so a shopper who reconnects after a restart holds a
    /// session pointing at a cart id nobody can serve.
    /// </para>
    /// <para>
    /// Silently keeping the stale id is the worse failure, and a subtle one: reconnecting opens a
    /// fresh cart at the station and everything looks fine, while the live feed is still subscribed
    /// to the dead one. Items are scanned, the total moves on the till, and the phone shows nothing
    /// for ever. Re-pointing the session is what keeps "my basket" meaning one thing.
    /// </para>
    /// </summary>
    public void AdoptCart(long cartId) => CartId = cartId;

    public Result MarkPaid(long saleId, DateTimeOffset now)
    {
        if (State != TrolleySessionState.Shopping)
        {
            return Result.Failure(NotShopping.With("state", State.ToString()));
        }

        State = TrolleySessionState.Paid;
        SaleId = saleId;
        LastActivityAt = now;
        return Result.Success();
    }

    /// <summary>Trolley handed back. Valid from paid (the normal path) and from shopping (gave up).</summary>
    public void Release(DateTimeOffset now)
    {
        if (State is TrolleySessionState.Released or TrolleySessionState.Abandoned)
        {
            return;
        }

        State = TrolleySessionState.Released;
        EndedAt = now;
        LastActivityAt = now;
    }

    public void Abandon(DateTimeOffset now)
    {
        if (State != TrolleySessionState.Shopping)
        {
            return;
        }

        State = TrolleySessionState.Abandoned;
        EndedAt = now;
        LastActivityAt = now;
    }
}
