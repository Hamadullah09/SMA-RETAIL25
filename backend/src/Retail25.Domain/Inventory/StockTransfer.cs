using Retail25.Domain.Common;

namespace Retail25.Domain.Inventory;

public enum TransferStatus
{
    Draft = 0,
    InTransit = 1,
    Received = 2,
    Cancelled = 3,
}

/// <summary>
/// Stock transfer between locations (guide p.20–21). Draft → InTransit → Received.
/// Replaces the legacy file-exchange FTP transfer mechanism.
/// <para>
/// The two ends move at different times on purpose. Stock leaves the source when the van does and
/// arrives at the destination when someone opens the box — anything in between is genuinely in
/// neither place, and pretending otherwise is how a store ends up selling a thing that is on a
/// motorway.
/// </para>
/// </summary>
public sealed class StockTransfer : AggregateRoot, IAuditable
{
    public static readonly Error SameLocation = new(
        "transfer.same_location",
        "A transfer has to go somewhere else.");

    public static readonly Error NotDraft = new(
        "transfer.not_draft",
        "Only a draft transfer can be changed.");

    public static readonly Error NotInTransit = new(
        "transfer.not_in_transit",
        "Only a transfer that has been shipped can be received.");

    public static readonly Error AlreadyFinished = new(
        "transfer.already_finished",
        "This transfer has already been received or cancelled.");

    public static readonly Error NothingToShip = new(
        "transfer.nothing_to_ship",
        "Add at least one line before shipping.");

    private StockTransfer()
    {
    }

    public long TransferNumber { get; set; }

    public Guid FromLocationId { get; set; }

    public Guid ToLocationId { get; set; }

    public TransferStatus Status { get; set; } = TransferStatus.Draft;

    public string? Notes { get; set; }

    /// <summary>When the stock left the source location. Null until shipped.</summary>
    public DateTimeOffset? ShippedAt { get; set; }

    /// <summary>When the last outstanding line arrived. Null until fully received.</summary>
    public DateTimeOffset? ReceivedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    public static Result<StockTransfer> Create(Guid fromLocationId, Guid toLocationId, long transferNumber, string? notes = null)
    {
        if (fromLocationId == toLocationId)
        {
            return Result.Failure<StockTransfer>(SameLocation);
        }

        return Result.Success(new StockTransfer
        {
            TransferNumber = transferNumber,
            FromLocationId = fromLocationId,
            ToLocationId = toLocationId,
            Status = TransferStatus.Draft,
            Notes = notes?.Trim(),
        });
    }

    /// <summary>Editable only while nothing has moved.</summary>
    public Result EnsureEditable() => Status == TransferStatus.Draft ? Result.Success() : Result.Failure(NotDraft);

    public Result Ship(DateTimeOffset at, bool hasLines)
    {
        if (Status != TransferStatus.Draft)
        {
            return Result.Failure(NotDraft);
        }

        if (!hasLines)
        {
            return Result.Failure(NothingToShip);
        }

        Status = TransferStatus.InTransit;
        ShippedAt = at;
        return Result.Success();
    }

    /// <summary>
    /// Books an arrival. A partial delivery leaves the transfer InTransit — same as a purchase
    /// order, because the rest of it is still real stock that is still somewhere.
    /// </summary>
    public Result Receive(DateTimeOffset at, bool fullyReceived)
    {
        if (Status != TransferStatus.InTransit)
        {
            return Result.Failure(NotInTransit);
        }

        if (fullyReceived)
        {
            Status = TransferStatus.Received;
            ReceivedAt = at;
        }

        return Result.Success();
    }

    /// <summary>
    /// Cancelling only makes sense before the stock leaves. Once it is in transit the goods are
    /// physically somewhere and the paperwork has to follow them, not be torn up.
    /// </summary>
    public Result Cancel()
    {
        if (Status != TransferStatus.Draft)
        {
            return Result.Failure(Status == TransferStatus.InTransit ? NotDraft : AlreadyFinished);
        }

        Status = TransferStatus.Cancelled;
        return Result.Success();
    }
}

/// <summary>
/// One item on a transfer. <see cref="ProductId"/> is the row at the <em>source</em> location —
/// products are one row per (location, stock code), so the destination's row is found or created
/// when the goods are received, not when the transfer is written.
/// </summary>
public sealed class StockTransferLine : Entity, IAuditable
{
    public static readonly Error QuantityRequired = new(
        "transfer.quantity_required",
        "A transfer line has to move at least some of something.");

    public static readonly Error OverReceipt = new(
        "transfer.over_receipt",
        "More was received than was shipped.");

    public StockTransferLine()
    {
    }

    public Guid StockTransferId { get; set; }

    public Guid ProductId { get; set; }

    public Guid? VariantId { get; set; }

    /// <summary>Copied from the source product so the destination can be created if it is new there.</summary>
    public string StockCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal QuantityReceived { get; set; }

    /// <summary>
    /// The source's average cost at the moment of shipping, frozen here.
    /// <para>
    /// The destination receives at this cost rather than at whatever the source's average happens
    /// to be by the time the box is opened — otherwise a sale at the source between shipping and
    /// receiving quietly changes what the goods in the van are worth.
    /// </para>
    /// </summary>
    public decimal UnitCost { get; set; }

    public decimal Outstanding => Quantity - QuantityReceived;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    public static Result<StockTransferLine> Create(
        Guid transferId, Guid productId, string stockCode, string productName, decimal quantity, decimal unitCost)
    {
        if (quantity <= 0m)
        {
            return Result.Failure<StockTransferLine>(QuantityRequired);
        }

        return Result.Success(new StockTransferLine
        {
            StockTransferId = transferId,
            ProductId = productId,
            StockCode = stockCode,
            ProductName = productName,
            Quantity = quantity,
            UnitCost = unitCost,
        });
    }

    public Result ReceiveQuantity(decimal quantity)
    {
        if (quantity <= 0m)
        {
            return Result.Failure(QuantityRequired);
        }

        if (QuantityReceived + quantity > Quantity)
        {
            return Result.Failure(OverReceipt
                .With("shipped", Quantity)
                .With("alreadyReceived", QuantityReceived)
                .With("requested", quantity));
        }

        QuantityReceived += quantity;
        return Result.Success();
    }
}
