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

    public Guid ProductId { get; private set; }

    public Guid? VariantId { get; private set; }

    /// <summary>Legacy serial number (guide p.42). May be null for RFID-only items.</summary>
    public string? SerialNumber { get; private set; }

    /// <summary>RFID Electronic Product Code, 24–96 hex chars. Null for non-RFID items.</summary>
    public string? Epc { get; private set; }

    public SerializedUnitState State { get; private set; } = SerializedUnitState.Provisioned;

    public Guid LocationId { get; private set; }

    /// <summary>When this unit was first received into the system.</summary>
    public DateTimeOffset ReceivedOn { get; private set; }

    /// <summary>Last time this tag was read by an RFID reader.</summary>
    public DateTimeOffset? LastSeenAt { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    public static Result<SerializedUnit> Create(
        Guid productId,
        Guid locationId,
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

    public Result Sell()
    {
        if (State != SerializedUnitState.InCart)
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
    public void AssignVariant(Guid? variantId) => VariantId = variantId;
}
