namespace Retail25.Infrastructure.LegacyData;

/// <summary>What kind of thing a source file holds. Drives which layout and which importer apply.</summary>
public enum LegacyEntity
{
    Inventory = 0,
    Client = 1,
    Supplier = 2,

    /// <summary>Cash-register <c>.ASC</c> export from a third-party till (guide p.17).</summary>
    RegisterSales = 3,

    /// <summary>Stock-count device export (guide p.22).</summary>
    StockCount = 4,

    Invoice = 5,
}

/// <summary>
/// The documented field order of one legacy export, in the order the columns appear.
/// <para>
/// These are positional, not named. The legacy exports have no header row, and the guide documents
/// them by position — so getting the order right is the whole job, and it is written here once
/// rather than spread through an importer.
/// </para>
/// </summary>
public sealed record LegacyLayout(LegacyEntity Entity, string Name, string GuideReference, IReadOnlyList<string> Columns)
{
    public int ColumnCount => Columns.Count;
}

/// <summary>
/// The field orders the legacy guide documents (doc 09 §3, guide p.28, p.48, p.61, p.17, p.22).
/// </summary>
public static class LegacyLayouts
{
    /// <summary>Inventory export (guide p.28) — eleven fields.</summary>
    public static readonly LegacyLayout Inventory = new(
        LegacyEntity.Inventory,
        "Inventory (.DTA / CSV)",
        "guide p.28",
        [
            "ItemName",
            "StockCode",
            "Department",
            "Category",
            "Size",
            "PackQuantity",
            "Cost",
            "Price",
            "OnHand",
            "Supplier",
            "ReorderNumber",
        ]);

    /// <summary>Client export (guide p.48) — fourteen fields.</summary>
    public static readonly LegacyLayout Client = new(
        LegacyEntity.Client,
        "Clients (.DTA / CSV)",
        "guide p.48",
        [
            "CustomerNumber",
            "FirstName",
            "LastName",
            "Company",
            "Address1",
            "Address2",
            "City",
            "Province",
            "PostalCode",
            "Phone",
            "Fax",
            "Email",
            "ClientType",
            "CreditLimit",
        ]);

    /// <summary>Supplier export (guide p.61) — fifteen fields.</summary>
    public static readonly LegacyLayout Supplier = new(
        LegacyEntity.Supplier,
        "Suppliers (.DTA / CSV)",
        "guide p.61",
        [
            "SupplierNumber",
            "Company",
            "ContactName",
            "Address1",
            "Address2",
            "City",
            "Province",
            "PostalCode",
            "Phone",
            "Fax",
            "Email",
            "AccountNumber",
            "Terms",
            "MinimumOrder",
            "Notes",
        ]);

    /// <summary>Cash-register export (guide p.17) — four fields.</summary>
    public static readonly LegacyLayout RegisterSales = new(
        LegacyEntity.RegisterSales,
        "Register sales (.ASC)",
        "guide p.17",
        ["StockCode", "ItemName", "QuantitySold", "Total"]);

    /// <summary>Stock-count device export (guide p.22) — two fields.</summary>
    public static readonly LegacyLayout StockCount = new(
        LegacyEntity.StockCount,
        "Stock count device",
        "guide p.22",
        ["StockCode", "ShelfCount"]);

    /// <summary>
    /// Accounts receivable (guide p.103). Exported from <c>INVOICE.DBF</c> rather than as a
    /// documented CSV, so the column names here are the DBF's own.
    /// </summary>
    public static readonly LegacyLayout Invoice = new(
        LegacyEntity.Invoice,
        "Invoices (INVOICE.DBF)",
        "guide p.103",
        ["InvoiceNumber", "CustomerNumber", "InvoiceDate", "DueDate", "Total", "Paid", "Balance"]);

    public static IReadOnlyList<LegacyLayout> All { get; } =
        [Inventory, Client, Supplier, RegisterSales, StockCount, Invoice];

    public static LegacyLayout For(LegacyEntity entity) => All.First(l => l.Entity == entity);

    /// <summary>
    /// Guesses which export a headerless file is, by counting its columns.
    /// <para>
    /// Only a starting point for the mapping step — column counts are not unique forever and the
    /// operator confirms it. Better than making them pick from a list of six with no hint.
    /// </para>
    /// </summary>
    public static LegacyLayout? GuessByColumnCount(int columns)
        => All.FirstOrDefault(l => l.ColumnCount == columns);
}
