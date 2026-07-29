using Retail25.Application.Abstractions;
using Retail25.Domain.Sales;

namespace Retail25.Infrastructure.Caching;

/// <summary>
/// The wire shape of a cart in Redis.
/// <para>
/// The entities are mapped field by field rather than serialized directly. That is not ceremony:
/// <c>Entity.Id</c> has an internal setter, which a JSON serializer would silently skip, and every
/// parked cart would come back with fresh identifiers. Writing the mapping out also means a domain
/// change that would invalidate carts in flight fails to compile instead of failing at a till.
/// </para>
/// </summary>
internal sealed record PersistedCart(
    Guid Id,
    Guid StationId,
    Guid LocationId,
    Guid StaffId,
    Guid? CustomerId,
    CartStatus Status,
    string? HeldName,
    DateTimeOffset? SuspendedAt,
    Guid? SuspendedByStaffId,
    int NextLineSequence,
    int Revision,
    Guid? CompletedTransactionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt,
    DateTimeOffset? ExpiresAt,
    List<PersistedCartLine> Lines,
    List<PersistedCartAdjustment> Adjustments,
    PersistedTaxOverride? TaxOverride)
{
    public static PersistedCart From(CartSnapshot snapshot)
    {
        var cart = snapshot.Cart;
        return new PersistedCart(
            cart.Id,
            cart.StationId,
            cart.LocationId,
            cart.StaffId,
            cart.CustomerId,
            cart.Status,
            cart.HeldName,
            cart.SuspendedAt,
            cart.SuspendedByStaffId,
            cart.NextLineSequence,
            cart.Revision,
            cart.CompletedTransactionId,
            cart.CreatedAt,
            cart.ModifiedAt,
            cart.ExpiresAt,
            snapshot.Lines.Select(PersistedCartLine.From).ToList(),
            snapshot.Adjustments.Select(PersistedCartAdjustment.From).ToList(),
            snapshot.TaxOverride is null ? null : PersistedTaxOverride.From(snapshot.TaxOverride));
    }

    public CartSnapshot ToSnapshot()
    {
        var cart = new Cart
        {
            Id = Id,
            StationId = StationId,
            LocationId = LocationId,
            StaffId = StaffId,
            CustomerId = CustomerId,
            Status = Status,
            HeldName = HeldName,
            SuspendedAt = SuspendedAt,
            SuspendedByStaffId = SuspendedByStaffId,
            NextLineSequence = NextLineSequence,
            Revision = Revision,
            CompletedTransactionId = CompletedTransactionId,
            CreatedAt = CreatedAt,
            ModifiedAt = ModifiedAt,
            ExpiresAt = ExpiresAt,
        };

        var snapshot = new CartSnapshot(cart)
        {
            TaxOverride = TaxOverride?.ToEntity(),
        };

        snapshot.Lines.AddRange((Lines ?? []).Select(l => l.ToEntity()));
        snapshot.Adjustments.AddRange((Adjustments ?? []).Select(a => a.ToEntity()));

        return snapshot;
    }
}

internal sealed record PersistedCartLine(
    Guid Id,
    Guid CartId,
    Guid ProductId,
    Guid? VariantId,
    Guid? SerializedUnitId,
    string? Epc,
    LineSource Source,
    decimal Quantity,
    decimal? ManualUnitPrice,
    decimal? ManualDiscountPct,
    int? RequestedPriceLevel,
    bool? Tax1Override,
    bool? Tax2Override,
    decimal? EmbeddedPrice,
    LineType LineType,
    bool ReturnToStock,
    string? Note,
    int Sequence,
    decimal UnitPrice,
    PriceOrigin PriceOrigin,
    decimal LineDiscountPct,
    bool Tax1Applies,
    bool Tax2Applies,
    decimal ExtendedNet,
    decimal Tax1Amount,
    decimal Tax2Amount,
    string? StockCodeSnapshot,
    string? NameSnapshot,
    decimal UnitCostSnapshot)
{
    public static PersistedCartLine From(CartLine line) => new(
        line.Id,
        line.CartId,
        line.ProductId,
        line.VariantId,
        line.SerializedUnitId,
        line.Epc,
        line.Source,
        line.Quantity,
        line.ManualUnitPrice,
        line.ManualDiscountPct,
        line.RequestedPriceLevel,
        line.Tax1Override,
        line.Tax2Override,
        line.EmbeddedPrice,
        line.LineType,
        line.ReturnToStock,
        line.Note,
        line.Sequence,
        line.UnitPrice,
        line.PriceOrigin,
        line.LineDiscountPct,
        line.Tax1Applies,
        line.Tax2Applies,
        line.ExtendedNet,
        line.Tax1Amount,
        line.Tax2Amount,
        line.StockCodeSnapshot,
        line.NameSnapshot,
        line.UnitCostSnapshot);

    public CartLine ToEntity() => new()
    {
        Id = Id,
        CartId = CartId,
        ProductId = ProductId,
        VariantId = VariantId,
        SerializedUnitId = SerializedUnitId,
        Epc = Epc,
        Source = Source,
        Quantity = Quantity,
        ManualUnitPrice = ManualUnitPrice,
        ManualDiscountPct = ManualDiscountPct,
        RequestedPriceLevel = RequestedPriceLevel,
        Tax1Override = Tax1Override,
        Tax2Override = Tax2Override,
        EmbeddedPrice = EmbeddedPrice,
        LineType = LineType,
        ReturnToStock = ReturnToStock,
        Note = Note,
        Sequence = Sequence,
        UnitPrice = UnitPrice,
        PriceOrigin = PriceOrigin,
        LineDiscountPct = LineDiscountPct,
        Tax1Applies = Tax1Applies,
        Tax2Applies = Tax2Applies,
        ExtendedNet = ExtendedNet,
        Tax1Amount = Tax1Amount,
        Tax2Amount = Tax2Amount,
        StockCodeSnapshot = StockCodeSnapshot,
        NameSnapshot = NameSnapshot,
        UnitCostSnapshot = UnitCostSnapshot,
    };
}

internal sealed record PersistedCartAdjustment(
    Guid Id,
    Guid CartId,
    AdjustmentType Type,
    string Label,
    decimal Amount,
    decimal Percent,
    string? Serial,
    Guid AppliedByStaffId,
    DateTimeOffset AppliedAt)
{
    public static PersistedCartAdjustment From(CartAdjustment adjustment) => new(
        adjustment.Id,
        adjustment.CartId,
        adjustment.Type,
        adjustment.Label,
        adjustment.Amount,
        adjustment.Percent,
        adjustment.Serial,
        adjustment.AppliedByStaffId,
        adjustment.AppliedAt);

    public CartAdjustment ToEntity() => new()
    {
        Id = Id,
        CartId = CartId,
        Type = Type,
        Label = Label,
        Amount = Amount,
        Percent = Percent,
        Serial = Serial,
        AppliedByStaffId = AppliedByStaffId,
        AppliedAt = AppliedAt,
    };
}

internal sealed record PersistedTaxOverride(
    Guid Id,
    Guid CartId,
    bool? Tax1,
    bool? Tax2,
    int AppliesFromSequence,
    Guid AppliedByStaffId,
    DateTimeOffset AppliedAt)
{
    public static PersistedTaxOverride From(CartTaxOverride taxOverride) => new(
        taxOverride.Id,
        taxOverride.CartId,
        taxOverride.Tax1,
        taxOverride.Tax2,
        taxOverride.AppliesFromSequence,
        taxOverride.AppliedByStaffId,
        taxOverride.AppliedAt);

    public CartTaxOverride ToEntity() => new()
    {
        Id = Id,
        CartId = CartId,
        Tax1 = Tax1,
        Tax2 = Tax2,
        AppliesFromSequence = AppliesFromSequence,
        AppliedByStaffId = AppliedByStaffId,
        AppliedAt = AppliedAt,
    };
}
