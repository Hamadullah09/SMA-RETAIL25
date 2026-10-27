namespace RetailPlus.Core.Model;

/// <summary>
/// A shop. A till's database holds its own store; the server's database holds every store of the company. Stations,
/// locations, settings and everything sold are tied to one store.
/// </summary>
public class Store : Entity
{
    /// <summary>Short code, e.g. MAIN.</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Windows time zone id. Business times (when a sale was made, a drawer closed) are this store's local time.</summary>
    public string? TimeZoneId { get; set; }
}

/// <summary>
/// The settings of one store: the single record of SETUP.DBF. Only fields whose meaning is clear from the manual are
/// typed here; the others are kept as <see cref="StoreSettingExtra"/> rows under their original field names.
/// </summary>
public class StoreSettings : Entity, IStoreScoped
{
    public Guid StoreId { get; set; }

    // Business ID [COMPANY, ADDR1, ADDR2, CITY, STATE, ZIP, COUNTRY, PHONE, FAX]
    public string? CompanyName { get; set; }
    public string? Address1 { get; set; }
    public string? Address2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
    public string? Fax { get; set; }

    // Taxes tab
    /// <summary>[VAT]; tax-inclusive pricing. To confirm.</summary>
    public bool TaxInclusive { get; set; }
    /// <summary>[VATREG]</summary>
    public string? TaxRegistrationNumber { get; set; }
    public string? Tax1Name { get; set; }
    /// <summary>Percent, e.g. 5.000 [TAX1].</summary>
    public decimal Tax1Rate { get; set; }
    public string? Tax2Name { get; set; }
    /// <summary>[TAX2]</summary>
    public decimal Tax2Rate { get; set; }
    /// <summary>Tax 1 is included in the calculation of Tax 2 [TAXCOMP].</summary>
    public bool Tax2Compound { get; set; }

    // POS and Security tabs
    /// <summary>Staff with level &lt; 3 may give discounts [STAFFDISC].</summary>
    public bool StaffMayDiscount { get; set; }
    /// <summary>Supervisor password for voiding sales [VOID_ID].</summary>
    public bool SupervisorForVoids { get; set; }
    /// <summary>Password before every sale, to track staff sales [STAFF_ID].</summary>
    public bool TrackStaffSales { get; set; }
    /// <summary>[CARRYCSZ]</summary>
    public bool CarryOverCityStateZip { get; set; }
    /// <summary>[UPCWEIGHT]</summary>
    public bool ScanRandomWeightBarcodes { get; set; }
    /// <summary>[FORCERMA]</summary>
    public bool UseRmaOnReturns { get; set; }
    /// <summary>Cash payment discount in percent [DISC4CASH].</summary>
    public decimal CashDiscountPercent { get; set; }
    /// <summary>[YNTHCLIMIT]</summary>
    public bool LimitThc { get; set; }
    /// <summary>Grams per transaction [THCLIMIT].</summary>
    public decimal ThcLimit { get; set; }

    // Options tab
    /// <summary>[GIFTCARDMAX]</summary>
    public decimal GiftCardMaximum { get; set; }
    /// <summary>[DP_ON]</summary>
    public bool BonusPointsEnabled { get; set; }
    /// <summary>[DP_PRINT]</summary>
    public bool PrintPointsOnSlips { get; set; }
    /// <summary>[DP_FACTOR]</summary>
    public decimal PointsPerDollar { get; set; }
    /// <summary>[DP_THRESHO]</summary>
    public decimal PointsRequired { get; set; }
    /// <summary>[DP_PERCENT]</summary>
    public bool PercentDiscountReward { get; set; }
    /// <summary>[DP_RATE]</summary>
    public decimal RewardPercent { get; set; }
    /// <summary>[DP_FIXED]</summary>
    public bool FixedDiscountReward { get; set; }
    /// <summary>[DP_AMT]</summary>
    public decimal RewardAmount { get; set; }
    /// <summary>Monthly interest on overdue accounts, percent [PENRATE].</summary>
    public decimal InterestRatePerMonth { get; set; }
    /// <summary>[GRACE]</summary>
    public int GracePeriodDays { get; set; }
    /// <summary>Smallest coin for cash rounding [MINIMUM_COI].</summary>
    public decimal MinimumTender { get; set; }
    /// <summary>[UK_DATE]</summary>
    public bool UseUkDateFormat { get; set; }
}

/// <summary>A SETUP.DBF field whose meaning isn't confirmed yet, kept under its original name.</summary>
public class StoreSettingExtra : Entity, IStoreScoped
{
    public Guid StoreId { get; set; }
    public string Name { get; set; } = "";
    public string? Value { get; set; }
}

/// <summary>
/// A POS station (workstation, till), numbered 1–999 as in the original's POS Station ID. Numbers are unique across
/// the company, not only the store, because each station hands out document numbers from its own range
/// (<see cref="NumberCounter"/>).
/// </summary>
public class Station : Entity, IStoreScoped
{
    public Guid StoreId { get; set; }
    public int Number { get; set; }
    public string? Name { get; set; }
    public List<StationSetting> Settings { get; set; } = [];
}

/// <summary>
/// The last document number of one kind a station handed out. Station n numbers from n × 10,000,000 + 1 up, so tills
/// working without a connection never issue the same invoice, customer, PO or RMA number.
/// </summary>
public class NumberCounter : Entity
{
    /// <summary>Numbers each station has for each kind of document.</summary>
    public const long NumbersPerStation = 10_000_000;

    /// <summary>One of the names in RetailPlusDbContext.Sequences.</summary>
    public string Kind { get; set; } = "";
    public int StationNumber { get; set; }
    public long LastValue { get; set; }

    public static long FirstNumber(int stationNumber) => stationNumber * NumbersPerStation + 1;
}

/// <summary>A per-workstation setting, mirroring one key of Plus50.INI.</summary>
public class StationSetting : Entity
{
    public Guid StationId { get; set; }
    public string Section { get; set; } = "";
    public string Key { get; set; } = "";
    public string? Value { get; set; }
}

public enum DocumentTextKind
{
    InvoiceHeader,
    InvoiceFooter,
    PurchaseOrderHeader,
    ReturnAddress,
}

/// <summary>Header, footer and address texts (HEADER.TXT, FOOTER.TXT, POHEADER.TXT, RETADDR.ADD).</summary>
public class DocumentText : Entity, IStoreScoped
{
    public Guid StoreId { get; set; }
    public DocumentTextKind Kind { get; set; }
    /// <summary>Null for store-wide texts; set for per-location ones such as the return address.</summary>
    public Guid? LocationId { get; set; }
    public Location? Location { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>A login (USERS.DBF). Passwords are never stored in a readable form.</summary>
public class UserAccount : Entity
{
    public string UserName { get; set; } = "";
    public string? PasswordHash { get; set; }
    /// <summary>0 = trainee, 1–4 as in the Security tab.</summary>
    public int AccessLevel { get; set; }
    public bool MustChangePassword { get; set; }
}
