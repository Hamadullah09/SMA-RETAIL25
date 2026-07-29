namespace Retail25.Application.Common;

/// <summary>
/// The permission catalogue. These strings are seeded as <c>permission</c> rows and granted through
/// <c>role_permission</c>; the constants exist so a typo is a compile error rather than a silent
/// authorisation hole.
/// <para>
/// Authorisation is by permission, never by role. Roles are presets that map the legacy access
/// levels 0–4 onto sets of these keys, so a store can keep its old mental model while an
/// administrator reshapes what any given level can actually do.
/// </para>
/// </summary>
public static class PermissionKeys
{
    public static class Pos
    {
        public const string Sell = "pos.sell";
        public const string Discount = "pos.discount";
        public const string PriceOverride = "pos.price_override";
        public const string TaxOverride = "pos.tax_override";
        public const string VoidSale = "pos.void_sale";
        public const string Suspend = "pos.suspend";
        public const string Recall = "pos.recall";
        public const string UnknownItem = "pos.unknown_item";
        public const string Reprint = "pos.reprint";
        public const string SelectPriceLevel = "pos.select_price_level";
        public const string Return = "pos.return";
    }

    public static class Drawer
    {
        public const string OpenFloat = "drawer.open_float";
        public const string PayIn = "drawer.pay_in";
        public const string PayOut = "drawer.pay_out";
        public const string Pop = "drawer.pop";
        public const string Close = "drawer.close";
        public const string Read = "drawer.read";
    }

    public static class Catalog
    {
        public const string Read = "catalog.read";
        public const string Write = "catalog.write";
        public const string Delete = "catalog.delete";
        public const string BulkAdjust = "catalog.bulk_adjust";
    }

    public static class Inventory
    {
        public const string Adjust = "inventory.adjust";
        public const string Receive = "inventory.receive";
        public const string Transfer = "inventory.transfer";
        public const string Count = "inventory.count";
        public const string YearEnd = "inventory.year_end";
        public const string CommissionTags = "inventory.commission_tags";
    }

    public static class Customer
    {
        public const string Read = "customer.read";
        public const string Write = "customer.write";
        public const string Delete = "customer.delete";
    }

    public static class Ar
    {
        public const string Read = "ar.read";
        public const string Payment = "ar.payment";
        public const string VoidInvoice = "ar.void_invoice";
        public const string Refund = "ar.refund";
        public const string LateCharges = "ar.late_charges";
    }

    public static class Purchasing
    {
        public const string Read = "purchasing.read";
        public const string Write = "purchasing.write";
        public const string PostOrder = "purchasing.post_order";
        public const string PostShipment = "purchasing.post_shipment";
    }

    public static class Staff
    {
        public const string Read = "staff.read";
        public const string Write = "staff.write";
        public const string TimeClockEdit = "staff.time_clock_edit";
    }

    public static class Reports
    {
        public const string Sales = "reports.sales";
        public const string Financial = "reports.financial";
        public const string CostVisibility = "reports.cost_visibility";
    }

    public static class Settings
    {
        public const string Read = "settings.read";
        public const string Write = "settings.write";
        public const string Taxes = "settings.taxes";
        public const string Hardware = "settings.hardware";
    }

    public static class Terminals
    {
        public const string Read = "terminals.read";
        public const string Operate = "terminals.operate";
        public const string Register = "terminals.register";
    }

    public static class System
    {
        public const string UsersManage = "users.manage";
        public const string MigrationRun = "migration.run";
        public const string SyncRun = "sync.run";
        public const string AuditRead = "audit.read";
    }

    /// <summary>Everything, for seeding the catalogue and for the administrator preset.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Pos.Sell, Pos.Discount, Pos.PriceOverride, Pos.TaxOverride, Pos.VoidSale,
        Pos.Suspend, Pos.Recall, Pos.UnknownItem, Pos.Reprint, Pos.SelectPriceLevel, Pos.Return,
        Drawer.OpenFloat, Drawer.PayIn, Drawer.PayOut, Drawer.Pop, Drawer.Close, Drawer.Read,
        Catalog.Read, Catalog.Write, Catalog.Delete, Catalog.BulkAdjust,
        Inventory.Adjust, Inventory.Receive, Inventory.Transfer, Inventory.Count, Inventory.YearEnd, Inventory.CommissionTags,
        Customer.Read, Customer.Write, Customer.Delete,
        Ar.Read, Ar.Payment, Ar.VoidInvoice, Ar.Refund, Ar.LateCharges,
        Purchasing.Read, Purchasing.Write, Purchasing.PostOrder, Purchasing.PostShipment,
        Staff.Read, Staff.Write, Staff.TimeClockEdit,
        Reports.Sales, Reports.Financial, Reports.CostVisibility,
        Settings.Read, Settings.Write, Settings.Taxes, Settings.Hardware,
        Terminals.Read, Terminals.Operate, Terminals.Register,
        System.UsersManage, System.MigrationRun, System.SyncRun, System.AuditRead,
    ];

    /// <summary>
    /// The legacy access levels 0–4 (guide p.82) expressed as permission sets. Level 0 is the
    /// training mode the guide describes: everything reachable, nothing committed.
    /// </summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<string>> LegacyLevelPresets { get; } =
        new Dictionary<int, IReadOnlyList<string>>
        {
            [0] = [Pos.Sell, Catalog.Read, Customer.Read],
            [1] = [Pos.Sell, Pos.Suspend, Pos.Recall, Catalog.Read, Customer.Read, Drawer.Read],
            [2] =
            [
                Pos.Sell, Pos.Suspend, Pos.Recall, Pos.UnknownItem, Pos.Reprint, Pos.Return,
                Catalog.Read, Customer.Read, Customer.Write,
                Drawer.Read, Drawer.OpenFloat, Drawer.Pop,
            ],
            [3] =
            [
                Pos.Sell, Pos.Discount, Pos.PriceOverride, Pos.TaxOverride, Pos.VoidSale, Pos.Suspend,
                Pos.Recall, Pos.UnknownItem, Pos.Reprint, Pos.SelectPriceLevel, Pos.Return,
                Catalog.Read, Catalog.Write, Customer.Read, Customer.Write,
                Inventory.Adjust, Inventory.Receive, Inventory.CommissionTags,
                Drawer.Read, Drawer.OpenFloat, Drawer.PayIn, Drawer.PayOut, Drawer.Pop, Drawer.Close,
                Ar.Read, Ar.Payment, Reports.Sales, Terminals.Read, Terminals.Operate,
            ],
            [4] = All,
        };
}
