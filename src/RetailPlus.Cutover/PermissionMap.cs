using RetailPlus.Core.Rules;

namespace RetailPlus.Cutover;

/// <summary>
/// Retail25's roles and permission keys in the terms of the new model. Retail25's five standard roles become the
/// standard roles of the same job; any other Retail25 role becomes a custom role with the permissions its Retail25 keys
/// translate to.
/// </summary>
public static class PermissionMap
{
    /// <summary>Retail25's standard role names (AspNetRoles.name) and the standard role each one becomes.</summary>
    public static readonly IReadOnlyDictionary<string, string> StandardRoles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Trainee"] = Permissions.TraineeRole,
        ["Cashier"] = Permissions.CashierRole,
        ["Clerk"] = Permissions.ClerkRole,
        ["Supervisor"] = Permissions.SupervisorRole,
        ["Administrator"] = Permissions.AdministratorRole,
    };

    /// <summary>Each Retail25 permission key and the keys of the new model it grants (none: nothing there to grant).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Keys = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["pos.sell"] = [Permissions.PosSell],
        ["pos.recall"] = [Permissions.PosSell],
        ["pos.suspend"] = [Permissions.PosSell],
        ["pos.reprint"] = [Permissions.PosSell],
        ["pos.unknown_item"] = [Permissions.PosSell],
        ["pos.discount"] = [Permissions.PosDiscount],
        ["pos.price_override"] = [Permissions.PosDiscount],
        ["pos.select_price_level"] = [Permissions.PosDiscount],
        ["pos.tax_override"] = [Permissions.PosDiscount],
        ["pos.return"] = [Permissions.PosReturn],
        ["pos.clear_sale"] = [Permissions.PosVoidLine],
        ["pos.void_sale"] = [Permissions.PosVoidSale],
        ["drawer.pop"] = [Permissions.PosNoSale],
        ["drawer.read"] = [Permissions.PosDrawer],
        ["drawer.open_float"] = [Permissions.PosDrawer],
        ["drawer.pay_in"] = [Permissions.PosDrawer],
        ["drawer.pay_out"] = [Permissions.PosDrawer],
        ["drawer.close"] = [Permissions.PosEndOfDay],
        ["catalog.read"] = [Permissions.InventoryView],
        ["catalog.write"] = [Permissions.InventoryEdit, Permissions.InventoryLabels],
        ["catalog.delete"] = [Permissions.InventoryEdit],
        ["catalog.bulk_adjust"] = [Permissions.InventoryPrices],
        ["inventory.adjust"] = [Permissions.InventoryAdjust],
        ["inventory.count"] = [Permissions.InventoryCount],
        ["inventory.transfer"] = [Permissions.InventoryTransfer],
        ["inventory.receive"] = [Permissions.PurchasingManage],
        ["inventory.year_end"] = [Permissions.YearEndRun],
        ["inventory.commission_tags"] = [Permissions.RfidManage],
        ["purchasing.read"] = [Permissions.PurchasingManage],
        ["purchasing.write"] = [Permissions.PurchasingManage, Permissions.SuppliersEdit],
        ["purchasing.post_order"] = [Permissions.PurchasingManage],
        ["purchasing.post_shipment"] = [Permissions.PurchasingManage],
        ["customer.read"] = [Permissions.ClientsView],
        ["customer.write"] = [Permissions.ClientsEdit],
        ["customer.delete"] = [Permissions.ClientsEdit],
        ["ar.read"] = [Permissions.ClientsView],
        ["ar.payment"] = [Permissions.InvoicesManage],
        ["ar.refund"] = [Permissions.InvoicesManage, Permissions.RmaManage],
        ["ar.void_invoice"] = [Permissions.InvoicesManage],
        ["ar.late_charges"] = [Permissions.InvoicesManage],
        ["reports.sales"] = [Permissions.ReportsView, Permissions.DashboardView],
        ["reports.inventory"] = [Permissions.ReportsView],
        ["reports.financial"] = [Permissions.ReportsView, Permissions.DashboardView, Permissions.AccountingExport],
        ["reports.cost_visibility"] = [Permissions.ReportsView],
        ["reports.hours"] = [Permissions.ReportsStaff],
        ["reports.commissions"] = [Permissions.ReportsStaff],
        ["settings.read"] = [Permissions.SetupStore],
        ["settings.write"] = [Permissions.SetupStore, Permissions.WebSignIn],
        ["settings.taxes"] = [Permissions.SetupStore],
        ["settings.hardware"] = [Permissions.SetupStations],
        ["staff.read"] = [Permissions.ReportsStaff],
        ["staff.write"] = [Permissions.SetupUsers],
        ["staff.time_clock"] = [Permissions.TimeClockPunch],
        ["staff.time_clock_edit"] = [Permissions.TimeClockEdit],
        ["users.manage"] = [Permissions.SetupUsers, Permissions.WebSignIn],
        ["audit.read"] = [Permissions.AuditView],
        ["migration.run"] = [Permissions.ImportRun],
        ["sync.run"] = [Permissions.SyncSend],
        ["system.backup"] = [Permissions.BackupManage],
        ["terminals.register"] = [Permissions.SetupStations],
        ["terminals.read"] = [],
        ["terminals.operate"] = [],
    };

    /// <summary>The new model's keys for a set of Retail25 keys, and the Retail25 keys this map does not know.</summary>
    public static (SortedSet<string> Granted, SortedSet<string> Unknown) Translate(IEnumerable<string> retail25Keys)
    {
        var granted = new SortedSet<string>(StringComparer.Ordinal);
        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var key in retail25Keys)
        {
            if (Keys.TryGetValue(key, out var ours)) granted.UnionWith(ours);
            else unknown.Add(key);
        }
        return (granted, unknown);
    }
}
