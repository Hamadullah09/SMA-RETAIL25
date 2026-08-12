using Retail25.Domain.Common;
using Retail25.Domain.ValueObjects;

namespace Retail25.Domain.Catalog;

/// <summary>
/// EPC state machine (guide p.42, doc 06 §1):
/// Provisioned → InStock → InCart → Sold → Returned → InStock
/// Any → Transferred, Any → Lost
/// </summary>
public enum SerializedUnitState
{
    Provisioned = 0,
    InStock = 1,
    InCart = 2,
    Reserved = 3,
    Sold = 4,
    Returned = 5,
    Transferred = 6,
    Void = 7,
    Lost = 8,
}

/// <summary>
/// Unified entity for serial numbers and RFID EPCs (guide p.42, doc 06 §1). One unit = one
/// physical item. Both serial number and EPC may be set; stores without RFID use serial only.
/// </summary>
public sealed class SerializedUnit : Entity, IAuditable
{
    public static readonly Error InvalidStateTransition = new(
        "serialized.invalid_transition",
        "The requested state transition is not allowed.");

    private SerializedUnit()
    {
    }

    public long ProductId { get; private set; }

    public long? VariantId { get; private set; }

    /// <summary>Legacy serial number (guide p.42). May be null for RFID-only items.</summary>
    public string? SerialNumber { get; private set; }

    /// <summary>RFID Electronic Product Code, 24–96 hex chars. Null for non-RFID items.</summary>
    public string? Epc { get; private set; }

    public SerializedUnitState State { get; private set; } = SerializedUnitState.Provisioned;

    public long LocationId { get; private set; }

    /// <summary>When this unit was first received into the system.</summary>
    public DateTimeOffset ReceivedOn { get; private set; }

    /// <summary>Last time this tag was read by an RFID reader.</summary>
    public DateTimeOffset? LastSeenAt { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public static Result<SerializedUnit> Create(
        long productId,
        long locationId,
        string? serialNumber,
        string? epc,
        DateTimeOffset receivedOn)
    {
        if (string.IsNullOrWhiteSpace(serialNumber) && string.IsNullOrWhiteSpace(epc))
            return Result.Failure<SerializedUnit>(new Error("serialized.identifier_required", "A serial number or EPC is required."));

        if (!string.IsNullOrWhiteSpace(epc))
        {
            var epcResult = ValueObjects.Epc.Create(epc);
            if (epcResult.IsFailure)
                return Result.Failure<SerializedUnit>(epcResult.Error);
        }

        return Result.Success(new SerializedUnit
        {
            ProductId = productId,
            LocationId = locationId,
            SerialNumber = serialNumber?.Trim(),
            Epc = epc?.Trim().ToUpperInvariant(),
            State = SerializedUnitState.Provisioned,
            ReceivedOn = receivedOn,
        });
    }

    /// <summary>
    /// Commission the unit into stock. Called when goods are received or labels are printed.
    /// </summary>
    public Result Commission()
    {
        if (State != SerializedUnitState.Provisioned)
            return Result.Failure(InvalidStateTransition.With("from", State).With("to", SerializedUnitState.InStock));

        State = SerializedUnitState.InStock;
        return Result.Success();
    }

    /// <summary>
    /// Claim for a cart. Compare-and-swap: a second station cannot claim the same unit.
    /// </summary>
    public Result ClaimForCart()
    {
        if (State != SerializedUnitState.InStock)
            return Result.Failure(InvalidStateTransition.With("from", State).With("to", SerializedUnitState.InCart));

        State = SerializedUnitState.InCart;
        return Result.Success();
    }

    /// <summary>
    /// Sold, from either the shelf or a cart.
    /// <para>
    /// <see cref="ClaimForCart"/> is the documented step between the two, and nothing calls it: a
    /// unit scanned at the till gets a <c>CartLine</c> and a debouncer claim on its EPC, and its own
    /// state is left alone. So this insisted on <c>InCart</c>, was handed <c>InStock</c> every time,
    /// returned a failure that the caller discarded, and the unit stayed on the shelf while the sale
    /// completed and the stock level went down. Nine units on completed sale lines were still
    /// <c>InStock</c>, two products had reached an on-hand of −1, and the same tag could be rung
    /// again and again.
    /// </para>
    /// <para>
    /// Accepting <c>InStock</c> is the honest fix rather than claiming on add, which would leave a
    /// unit stranded in <c>InCart</c> every time a cart was abandoned — and carts here expire on a
    /// twelve-hour TTL that nothing reconciles against. What must never happen is selling a unit
    /// twice, and that is still refused: <c>Sold</c>, <c>Returned</c> and <c>Void</c> all fall
    /// through to the failure below. Two tills racing the same row are separated by the row version,
    /// which is a concurrency token on every entity here.
    /// </para>
    /// </summary>
    public Result Sell()
    {
        if (State is not (SerializedUnitState.InCart or SerializedUnitState.InStock))
            return Result.Failure(InvalidStateTransition.With("from", State).With("to", SerializedUnitState.Sold));

        State = SerializedUnitState.Sold;
        return Result.Success();
    }

    public Result Return()
    {
        if (State != SerializedUnitState.Sold)
            return Result.Failure(InvalidStateTransition.With("from", State).With("to", SerializedUnitState.Returned));

        State = SerializedUnitState.Returned;
        return Result.Success();
    }

    public Result ReleaseFromCart()
    {
        if (State != SerializedUnitState.InCart)
            return Result.Failure(InvalidStateTransition.With("from", State).With("to", SerializedUnitState.InStock));

        State = SerializedUnitState.InStock;
        return Result.Success();
    }

    public Result MarkLost()
    {
        State = SerializedUnitState.Lost;
        return Result.Success();
    }

    public Result Transfer()
    {
        if (State != SerializedUnitState.InStock)
            return Result.Failure(InvalidStateTransition.With("from", State).With("to", SerializedUnitState.Transferred));

        State = SerializedUnitState.Transferred;
        return Result.Success();
    }

    public void UpdateLastSeen(DateTimeOffset timestamp) => LastSeenAt = timestamp;

    /// <summary>
    /// Binds the unit to a matrix variant. A tagged shirt is a specific colour and size, and the tag
    /// has to say which — otherwise a bulk read tells you a shirt left the shop but not which one to
    /// deduct from stock.
    /// </summary>
    public void AssignVariant(long? variantId) => VariantId = variantId;

    public static readonly Error CannotReassign = new(
        "unit.cannot_reassign",
        "Only a tag that is in stock can be moved to a different item.");

    /// <summary>
    /// Moves this tag to a different product.
    /// <para>
    /// A real operation, not a repair: tags get applied to the wrong item during goods-in, and a roll
    /// of pre-encoded labels gets reused when a line is discontinued. Without it the only remedy is
    /// throwing the tag away, which for a shop with a few hundred of them is a real cost.
    /// </para>
    /// <para>
    /// Only from <see cref="SerializedUnitState.InStock"/>. A tag on somebody's cart would move
    /// under them mid-sale, and one already sold is part of a receipt and a stock movement that
    /// happened — changing what it refers to would rewrite both.
    /// </para>
    /// </summary>
    public Result ReassignTo(long productId, long? variantId = null)
    {
        if (State != SerializedUnitState.InStock)
        {
            return Result.Failure(CannotReassign.With("state", State.ToString()));
        }

        ProductId = productId;
        VariantId = variantId;

        return Result.Success();
    }
}
