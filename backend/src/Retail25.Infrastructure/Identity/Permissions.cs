namespace Retail25.Infrastructure.Identity;

/// <summary>
/// Seeded permission catalogue (doc 07). Every permission that can be checked by
/// [RequiresPermission] on a MediatR request or on an endpoint.
/// </summary>
public static class Permissions
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
    }

    public static class Drawer
    {
        public const string OpenFloat = "drawer.open_float";
        public const string PayIn = "drawer.pay_in";
        public const string PayOut = "drawer.pay_out";
        public const string Pop = "drawer.pop";
        public const string Close = "drawer.close";
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

    public static class System
    {
        public const string UsersManage = "users.manage";
        public const string MigrationRun = "migration.run";
        public const string SyncRun = "sync.run";
        public const string AuditRead = "audit.read";
    }

    /// <summary>All permissions in the system, for seeding.</summary>
    public static IReadOnlyList<string> AllPermissions { get; } =
    [
        Pos.Sell, Pos.Discount, Pos.PriceOverride, Pos.TaxOverride, Pos.VoidSale,
        Pos.Suspend, Pos.Recall, Pos.UnknownItem, Pos.Reprint, Pos.SelectPriceLevel,
        Drawer.OpenFloat, Drawer.PayIn, Drawer.PayOut, Drawer.Pop, Drawer.Close,
        Catalog.Read, Catalog.Write, Catalog.Delete, Catalog.BulkAdjust,
        Inventory.Adjust, Inventory.Receive, Inventory.Transfer, Inventory.Count, Inventory.YearEnd,
        Customer.Read, Customer.Write, Customer.Delete,
        Ar.Read, Ar.Payment, Ar.VoidInvoice, Ar.Refund, Ar.LateCharges,
        Purchasing.Read, Purchasing.Write, Purchasing.PostOrder, Purchasing.PostShipment,
        Staff.Read, Staff.Write, Staff.TimeClockEdit,
        Reports.Sales, Reports.Financial, Reports.CostVisibility,
        Settings.Read, Settings.Write, Settings.Taxes, Settings.Hardware,
        System.UsersManage, System.MigrationRun, System.SyncRun, System.AuditRead,
    ];
}
