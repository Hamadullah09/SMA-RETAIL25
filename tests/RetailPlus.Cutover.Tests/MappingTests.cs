using RetailPlus.Core.Rules;

namespace RetailPlus.Cutover.Tests;

/// <summary>The parts of the cutover that need no database: row ids and the translation of Retail25's permissions.</summary>
public class MappingTests
{
    /// <summary>Retail25's 62 permission keys (its permissions table, as its seed writes it).</summary>
    private static readonly string[] Retail25Keys =
    [
        "drawer.close", "drawer.open_float", "drawer.pay_in", "drawer.pay_out", "drawer.pop", "drawer.read",
        "catalog.bulk_adjust", "catalog.delete", "catalog.read", "catalog.write",
        "customer.delete", "customer.read", "customer.write",
        "inventory.adjust", "inventory.commission_tags", "inventory.count", "inventory.receive", "inventory.transfer", "inventory.year_end",
        "pos.clear_sale", "pos.discount", "pos.price_override", "pos.recall", "pos.reprint", "pos.return", "pos.select_price_level",
        "pos.sell", "pos.suspend", "pos.tax_override", "pos.unknown_item", "pos.void_sale",
        "purchasing.post_order", "purchasing.post_shipment", "purchasing.read", "purchasing.write",
        "ar.late_charges", "ar.payment", "ar.read", "ar.refund", "ar.void_invoice",
        "reports.commissions", "reports.cost_visibility", "reports.financial", "reports.hours", "reports.inventory", "reports.sales",
        "settings.hardware", "settings.read", "settings.taxes", "settings.write",
        "staff.read", "staff.time_clock", "staff.time_clock_edit", "staff.write",
        "audit.read", "migration.run", "sync.run", "system.backup", "users.manage",
        "terminals.operate", "terminals.read", "terminals.register",
    ];

    [Fact]
    public void Ids_are_the_same_on_every_run_and_never_look_like_the_programs_own()
    {
        var id = StableIds.For("products", 42);
        Assert.Equal(id, StableIds.For("products", 42));
        Assert.Equal(id, StableIds.For("products", "42"));
        Assert.NotEqual(id, StableIds.For("products", 43));
        Assert.NotEqual(id, StableIds.For("customers", 42));
        Assert.Equal(8, id.Version);
        Assert.Equal(7, Guid.CreateVersion7().Version);
    }

    [Fact]
    public void Every_Retail25_permission_translates_to_known_keys()
    {
        Assert.Equal(62, Retail25Keys.Length);
        var (granted, unknown) = PermissionMap.Translate(Retail25Keys);
        Assert.Empty(unknown);
        Assert.All(granted, key => Assert.True(Permissions.IsKnown(key), key));
        Assert.All(PermissionMap.Keys.Values.SelectMany(k => k), key => Assert.True(Permissions.IsKnown(key), key));
        // Every key of Retail25 together grants everything here but the cross-store keys, gift cards (sold under pos.sell
        // in Retail25, where the standard roles keep it) and pos.approve (added to custom roles as a whole).
        Assert.Equal(Permissions.All.Select(p => p.Key).Except([Permissions.StoresManage, Permissions.StoresAll, Permissions.GiftCardsIssue, Permissions.PosApprove]).Order(), granted.Order());
    }

    [Fact]
    public void Retail25s_standard_roles_are_the_standard_roles_of_the_same_job()
    {
        Assert.All(PermissionMap.StandardRoles.Values, role => Assert.Contains(Permissions.DefaultRoles, r => r.Name == role));
        Assert.Equal(Permissions.ClerkRole, PermissionMap.StandardRoles["clerk"]);
        var (granted, unknown) = PermissionMap.Translate(["pos.sell", "loyalty.adjust"]);
        Assert.Equal([Permissions.PosSell], granted);
        Assert.Equal(["loyalty.adjust"], unknown);
    }
}
