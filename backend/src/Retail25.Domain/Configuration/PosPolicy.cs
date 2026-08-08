using Retail25.Domain.Common;

namespace Retail25.Domain.Configuration;

/// <summary>
/// Store-wide point-of-sale behaviour. Every switch on the legacy Setup / POS tab
/// (user guide p.77–78) survives here as a row, so behaviour is administered rather than deployed.
/// Station-specific overrides live on <c>Station</c>; this is the fallback.
/// </summary>
public sealed class PosPolicy : AggregateRoot, IAuditable
{
    private PosPolicy()
    {
    }

    public long LocationId { get; private set; }

    // --- Taxes and charges -------------------------------------------------------------------

    /// <summary>Charge tax 1 by default. A tax is applied only if enabled here AND on the item.</summary>
    public bool ApplyTax1 { get; private set; } = true;

    public bool ApplyTax2 { get; private set; } = true;

    /// <summary>
    /// Permit staff to exempt an item from a tax it normally attracts, or apply one it normally
    /// does not. When false the till hides the keys and the server rejects the field.
    /// </summary>
    public bool AllowTaxOverride { get; private set; } = true;

    public bool ApplyAddOnCharge { get; private set; }

    // --- Selling behaviour -------------------------------------------------------------------

    /// <summary>
    /// Suppress the item-detail window so barcode scanning is uninterrupted (user guide p.77).
    /// Bulk RFID reading always behaves as if this is on.
    /// </summary>
    public bool FastScanMode { get; private set; }

    /// <summary>Post the sale automatically when the slip prints, saving keystrokes (p.77).</summary>
    public bool AutoSaveSales { get; private set; } = true;

    /// <summary>Ask the cashier to confirm before the sale is committed (p.78).</summary>
    public bool ConfirmBeforeSavingSales { get; private set; }

    /// <summary>Enable Type 2 embedded-price barcode handling (p.78, p.98).</summary>
    public bool ScanRandomWeightBarcodes { get; private set; }

    /// <summary>Allow staff below supervisor level to give discounts (p.77).</summary>
    public bool StaffMayDiscount { get; private set; }

    /// <summary>Let staff type free-text note lines directly onto the invoice (p.77).</summary>
    public bool AllowItemListEdit { get; private set; }

    /// <summary>Require a staff identity before every sale so takings and commission are attributed (p.82).</summary>
    public bool TrackStaffSales { get; private set; }

    /// <summary>Require a supervisor identity before a sale can be voided (p.82).</summary>
    public bool RequireSupervisorToVoid { get; private set; } = true;

    /// <summary>Enable clock-in / clock-out (p.82).</summary>
    public bool UseEmployeeTimeClock { get; private set; }

    // --- Printing ----------------------------------------------------------------------------

    /// <summary>Print the cardholder message and signature line on card sales (p.77).</summary>
    public bool PrintCreditCardSignatureLine { get; private set; } = true;

    /// <summary>Print the customer's name on the sales slip when one is attached (p.77).</summary>
    public bool PrintClientNameOnSalesSlip { get; private set; }

    /// <summary>Carry the previous customer's city, state and postcode into a new record (p.77).</summary>
    public bool CarryOverCityStateZip { get; private set; }

    // --- Defaults ----------------------------------------------------------------------------

    /// <summary>Tender selected when the payment screen opens (p.78).</summary>
    public long? DefaultTenderTypeId { get; private set; }

    /// <summary>
    /// Minutes an untouched cart is kept before it is abandoned. Suspended carts are never expired
    /// by this; only carts nobody returned to.
    /// </summary>
    public int AbandonedCartTimeoutMinutes { get; private set; } = 720;

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public static PosPolicy CreateDefault(long locationId) => new() { LocationId = locationId };

    public void UpdateTaxBehaviour(bool applyTax1, bool applyTax2, bool allowTaxOverride, bool applyAddOnCharge)
    {
        ApplyTax1 = applyTax1;
        ApplyTax2 = applyTax2;
        AllowTaxOverride = allowTaxOverride;
        ApplyAddOnCharge = applyAddOnCharge;
    }

    public void UpdateSellingBehaviour(
        bool fastScanMode,
        bool autoSaveSales,
        bool confirmBeforeSavingSales,
        bool scanRandomWeightBarcodes,
        bool staffMayDiscount,
        bool allowItemListEdit)
    {
        FastScanMode = fastScanMode;
        AutoSaveSales = autoSaveSales;
        ConfirmBeforeSavingSales = confirmBeforeSavingSales;
        ScanRandomWeightBarcodes = scanRandomWeightBarcodes;
        StaffMayDiscount = staffMayDiscount;
        AllowItemListEdit = allowItemListEdit;
    }

    public void UpdateStaffControls(bool trackStaffSales, bool requireSupervisorToVoid, bool useEmployeeTimeClock)
    {
        TrackStaffSales = trackStaffSales;
        RequireSupervisorToVoid = requireSupervisorToVoid;
        UseEmployeeTimeClock = useEmployeeTimeClock;
    }

    public void UpdatePrinting(
        bool printCreditCardSignatureLine,
        bool printClientNameOnSalesSlip,
        bool carryOverCityStateZip)
    {
        PrintCreditCardSignatureLine = printCreditCardSignatureLine;
        PrintClientNameOnSalesSlip = printClientNameOnSalesSlip;
        CarryOverCityStateZip = carryOverCityStateZip;
    }

    public void SetDefaultTender(long? tenderTypeId) => DefaultTenderTypeId = tenderTypeId;

    public void SetAbandonedCartTimeout(int minutes)
        => AbandonedCartTimeoutMinutes = minutes > 0 ? minutes : AbandonedCartTimeoutMinutes;
}
