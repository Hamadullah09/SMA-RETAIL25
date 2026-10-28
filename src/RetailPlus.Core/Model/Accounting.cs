namespace RetailPlus.Core.Model;

/// <summary>What an account code is used for in the journal exported to the accounting program.</summary>
public enum AccountMappingKind
{
    /// <summary>Money taken by one tender type (<see cref="AccountMapping.Key"/> is its name): cash, bank, card clearing.</summary>
    Tender,
    /// <summary>Sales of one department (<see cref="AccountMapping.Key"/> is its name).</summary>
    Department,
    /// <summary>Sales of items without a department, or of a department without its own account.</summary>
    Sales,
    Tax1,
    Tax2,
    /// <summary>Gift cards sold and not yet spent (a liability).</summary>
    GiftCardLiability,
    CostOfGoods,
    Inventory,
    /// <summary>Sales on account and layaway balances; payments on account reduce it.</summary>
    AccountsReceivable,
    /// <summary>Discounts on the whole sale.</summary>
    Discounts,
    /// <summary>Cash rounding to the smallest coin.</summary>
    Rounding,
    /// <summary>Stock found or lost: adjustments, stock counts, case breaks.</summary>
    StockAdjustments,
    /// <summary>Stock received on purchase orders, until the supplier's bill is entered (goods received not invoiced).</summary>
    PurchasesClearing,
    /// <summary>Stock shipped to another store and not received yet.</summary>
    InventoryInTransit,
    /// <summary>Gift card balances corrected by a supervisor: the other side of the liability (goodwill, write-offs).</summary>
    GiftCardAdjustments,
}

/// <summary>
/// The account code of the store's accounting program for one kind of amount (Setup › Accounting): one table for every
/// mapping, keyed by kind and, for tenders and departments, by name.
/// </summary>
public class AccountMapping : Entity, IStoreScoped
{
    public Guid StoreId { get; set; }
    public AccountMappingKind Kind { get; set; }
    /// <summary>The tender or department name for those kinds; empty for the others.</summary>
    public string Key { get; set; } = "";
    public string AccountCode { get; set; } = "";
    public string? AccountName { get; set; }
}

public enum StoreImageKind
{
    /// <summary>Shown in the app header, on receipts and on printed reports.</summary>
    Logo,
    /// <summary>Printed faintly behind receipts and reports.</summary>
    Watermark,
}

/// <summary>
/// A store's branding picture, kept in the database (a JPEG of at most 800 pixels a side, like item pictures) so every
/// till of the store shows and prints it.
/// </summary>
public class StoreImage : Entity, IStoreScoped
{
    public Guid StoreId { get; set; }
    public StoreImageKind Kind { get; set; }
    public byte[] Data { get; set; } = [];
}
