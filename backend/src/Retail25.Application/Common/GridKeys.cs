namespace Retail25.Application.Common;

/// <summary>
/// The grid names that appear in live row patches over <c>InventoryHub</c>.
/// <para>
/// Constants rather than literals because the string is a contract with the front end: a handler
/// sending <c>"products"</c> while the grid subscribes to <c>"product"</c> produces no error
/// anywhere — the row simply never arrives, and the grid quietly goes stale, which is the exact
/// failure this whole mechanism exists to remove.
/// </para>
/// </summary>
public static class GridKeys
{
    public const string Product = "product";
    public const string Customer = "customer";
    public const string Supplier = "supplier";
    public const string Department = "department";
    public const string Category = "category";
    public const string Station = "station";
    public const string TenderType = "tender_type";
    public const string Currency = "currency";
    public const string PurchaseOrder = "purchase_order";
    public const string StockLevel = "stock_level";
}

/// <summary>
/// Settings section names, used both to route a save and to tell open settings screens which tab
/// to reload. These mirror the tabs of the legacy Setup screen (user guide p.76–84).
/// </summary>
public static class SettingsSections
{
    public const string Business = "business";
    public const string Taxes = "taxes";
    public const string Pos = "pos";
    public const string Printers = "printers";
    public const string Hardware = "hardware";
    public const string Stations = "stations";
    public const string Tenders = "tenders";
    public const string Currencies = "currencies";
    public const string Numbering = "numbering";
    public const string Pricing = "pricing";
}
