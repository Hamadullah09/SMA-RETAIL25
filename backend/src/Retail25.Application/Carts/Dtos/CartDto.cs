using Retail25.Domain.Sales;

namespace Retail25.Application.Carts.Dtos;

/// <summary>
/// One line as the POS list renders it. <see cref="PriceOrigin"/> travels to the client so the UI
/// can badge a line that did not ring at the regular price — a cashier should be able to see why
/// without opening the detail drawer.
/// </summary>
public sealed record CartLineDto(
    Guid Id,
    int Sequence,
    Guid ProductId,
    Guid? VariantId,
    string StockCode,
    string Name,
    string? VariantLabel,
    string? Epc,
    string? SerialNumber,
    LineSource Source,
    LineType LineType,
    decimal Quantity,
    decimal ChargeableQuantity,
    decimal UnitPrice,
    PriceOrigin PriceOrigin,
    decimal DiscountPct,
    decimal ExtendedNet,
    bool Tax1Applies,
    bool Tax2Applies,
    decimal Tax1Amount,
    decimal Tax2Amount,
    int? RequestedPriceLevel,
    bool HasManualPrice,
    string? Note);

public sealed record CartAdjustmentDto(Guid Id, AdjustmentType Type, string Label, decimal Amount, string? Serial);

/// <summary>
/// The totals panel. Tax names come from configuration rather than the UI, so a Canadian store shows
/// "GST"/"PST" and a UK store shows "VAT" with no front-end change.
/// </summary>
public sealed record CartTotalsDto(
    decimal Subtotal,
    decimal DiscountTotal,
    string Tax1Name,
    decimal Tax1Total,
    string Tax2Name,
    decimal Tax2Total,
    string AddOnChargeName,
    decimal AddOnCharge,
    decimal GrandTotal,
    bool TaxInclusive,
    int LoyaltyPointsEarned,
    int LoyaltyPointsRedeemed,
    int ItemCount);

/// <summary>The customer context panel (region ④ of the POS screen).</summary>
public sealed record CartCustomerDto(
    Guid Id,
    long CustomerNumber,
    string Name,
    int PriceLevel,
    decimal UsualDiscountPct,
    bool ExemptTax1,
    bool ExemptTax2,
    int RewardPoints,
    decimal AccountBalance,
    decimal CreditLimit);

/// <summary>
/// The station's effective behaviour after the per-station overrides have been folded over the
/// store policy. The client needs this to know whether to open the item-detail drawer at all.
/// </summary>
public sealed record StationPolicyDto(
    Guid StationId,
    string StationCode,
    bool FastScanMode,
    bool AutoSaveSales,
    bool ConfirmBeforeSaving,
    bool ScanRandomWeightBarcodes,
    bool AllowTaxOverride,
    bool StaffMayDiscount,
    bool AllowItemListEdit,
    bool RequireSupervisorToVoid,
    Guid? DefaultTenderTypeId,
    decimal MinimumTender,
    string CurrencyCode,
    string CurrencySymbol);

public sealed record CartDto(
    Guid Id,
    Guid StationId,
    Guid LocationId,
    Guid StaffId,
    CartStatus Status,
    int Revision,
    string? HeldName,
    CartCustomerDto? Customer,
    IReadOnlyList<CartLineDto> Lines,
    IReadOnlyList<CartAdjustmentDto> Adjustments,
    CartTotalsDto Totals,
    bool? TaxOverride1,
    bool? TaxOverride2);

/// <summary>A suspended cart as it appears in the recall list (guide p.11).</summary>
public sealed record SuspendedCartDto(
    Guid Id,
    string? Label,
    Guid StaffId,
    string? CustomerName,
    int LineCount,
    decimal GrandTotal,
    DateTimeOffset SuspendedAt);
