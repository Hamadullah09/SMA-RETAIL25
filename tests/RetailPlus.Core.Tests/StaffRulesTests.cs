using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Core.Tests;

public class CommissionTests
{
    private static readonly Guid Ann = Guid.NewGuid(), Bob = Guid.NewGuid();
    private static readonly Guid Dept = Guid.NewGuid(), Cat = Guid.NewGuid(), Thing = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 15);

    /// <summary>Ann sells 2 of item Thing (department Dept, category Cat) for 100; the item's own rate gave 3.</summary>
    private static CommissionLine Line(Guid? staff = null, DateOnly? day = null) =>
        new(staff ?? Ann, "ann", day ?? Day, Thing, Dept, Cat, 2, 100m, 3m);

    private static CommissionRule Rule(string name, CommissionScope scope, decimal rate, Guid? user = null,
        CommissionBasis basis = CommissionBasis.PercentOfNetSales, DateOnly? from = null, DateOnly? to = null) => new()
    {
        Name = name, Scope = scope, Rate = rate, UserId = user, Basis = basis, EffectiveFrom = from, EffectiveTo = to,
        DepartmentId = scope == CommissionScope.Department ? Dept : null,
        CategoryId = scope == CommissionScope.Category ? Cat : null,
        ItemId = scope == CommissionScope.Item ? Thing : null,
    };

    [Fact]
    public void Without_a_rule_a_line_keeps_the_commission_of_the_item_rate()
    {
        Assert.Null(Commissions.RuleFor([], Line()));
        Assert.Equal(3m, Commissions.For([], Line()));
    }

    [Fact]
    public void Item_beats_category_beats_department_beats_all_items()
    {
        var all = Rule("all", CommissionScope.AllItems, 1);
        var dept = Rule("dept", CommissionScope.Department, 2);
        var cat = Rule("cat", CommissionScope.Category, 3);
        var item = Rule("item", CommissionScope.Item, 4);
        Assert.Equal("item", Commissions.RuleFor([all, dept, cat, item], Line())!.Name);
        Assert.Equal("cat", Commissions.RuleFor([all, dept, cat], Line())!.Name);
        Assert.Equal("dept", Commissions.RuleFor([all, dept], Line())!.Name);
        Assert.Equal("all", Commissions.RuleFor([all], Line())!.Name);
        Assert.Equal(4m, Commissions.For([all, dept, cat, item], Line()));
    }

    [Fact]
    public void A_rule_for_the_salesperson_beats_one_for_everyone_of_the_same_scope()
    {
        var everyone = Rule("everyone", CommissionScope.Department, 2);
        var ann = Rule("ann", CommissionScope.Department, 5, user: Ann);
        var bob = Rule("bob", CommissionScope.Department, 7, user: Bob);
        Assert.Equal("ann", Commissions.RuleFor([everyone, ann, bob], Line())!.Name);
        Assert.Equal("bob", Commissions.RuleFor([everyone, ann, bob], Line(staff: Bob))!.Name);
        Assert.Equal("everyone", Commissions.RuleFor([everyone, ann, bob], Line(staff: Guid.NewGuid()))!.Name);
        // A narrower scope still wins over a rule just for the salesperson.
        Assert.Equal("item", Commissions.RuleFor([ann, Rule("item", CommissionScope.Item, 1)], Line())!.Name);
    }

    [Fact]
    public void Only_rules_in_force_on_the_day_count_and_the_latest_start_wins()
    {
        var old = Rule("old", CommissionScope.AllItems, 1, from: new DateOnly(2026, 1, 1));
        var october = Rule("october", CommissionScope.AllItems, 2, from: new DateOnly(2026, 10, 1), to: new DateOnly(2026, 10, 31));
        var ended = Rule("ended", CommissionScope.Item, 9, to: new DateOnly(2026, 9, 30));
        var future = Rule("future", CommissionScope.Item, 9, from: new DateOnly(2026, 11, 1));
        var off = Rule("off", CommissionScope.Item, 9);
        off.IsActive = false;
        Assert.Equal("october", Commissions.RuleFor([old, october, ended, future, off], Line())!.Name);
        Assert.Equal("ended", Commissions.RuleFor([old, october, ended, future, off], Line(day: new DateOnly(2026, 9, 30)))!.Name);
        Assert.Equal("future", Commissions.RuleFor([old, october, ended, future, off], Line(day: new DateOnly(2026, 11, 1)))!.Name);
        Assert.Null(Commissions.RuleFor([old, october], Line(day: new DateOnly(2025, 12, 31))));
        Assert.Equal("old", Commissions.RuleFor([old, october], Line(day: new DateOnly(2026, 11, 1)))!.Name);
    }

    [Fact]
    public void Percent_of_net_sales_and_amount_per_unit()
    {
        Assert.Equal(12.5m, Commissions.For([Rule("pct", CommissionScope.AllItems, 12.5m)], Line()));
        Assert.Equal(1.5m, Commissions.For([Rule("unit", CommissionScope.AllItems, 0.75m, basis: CommissionBasis.AmountPerUnit)], Line()));
        // A return takes the commission back.
        var refund = Line() with { Quantity = -1, NetAmount = -50m };
        Assert.Equal(-5m, Commissions.For([Rule("pct", CommissionScope.AllItems, 10)], refund));
    }
}

public class TimeClockTests
{
    [Fact]
    public void A_shift_over_midnight_counts_on_both_days()
    {
        var days = TimeClock.SplitByDay(new DateTime(2026, 10, 14, 22, 0, 0), new DateTime(2026, 10, 15, 2, 30, 0));
        Assert.Equal([(new DateOnly(2026, 10, 14), 2m), (new DateOnly(2026, 10, 15), 2.5m)], days);
    }

    [Fact]
    public void A_shift_on_one_day_and_hours_to_the_hundredth()
    {
        Assert.Equal([(new DateOnly(2026, 10, 14), 8.25m)], TimeClock.SplitByDay(new DateTime(2026, 10, 14, 9, 0, 0), new DateTime(2026, 10, 14, 17, 15, 0)));
        Assert.Equal(0.33m, TimeClock.Hours(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 9, 20, 0)));
        Assert.Empty(TimeClock.SplitByDay(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 9, 0, 0)));
    }

    [Fact]
    public void Weeks_start_on_monday()
    {
        Assert.Equal(new DateOnly(2026, 10, 26), TimeClock.WeekStart(new DateOnly(2026, 10, 26)));
        Assert.Equal(new DateOnly(2026, 10, 26), TimeClock.WeekStart(new DateOnly(2026, 11, 1)));
        Assert.Equal(new DateOnly(2026, 10, 19), TimeClock.WeekStart(new DateOnly(2026, 10, 25)));
    }
}

public class ApprovalRuleTests
{
    [Fact]
    public void An_approver_needs_approve_and_the_permission_itself()
    {
        Assert.True(Permissions.CanApprove(new HashSet<string> { Permissions.PosApprove, Permissions.PosVoidSale }, Permissions.PosVoidSale));
        Assert.False(Permissions.CanApprove(new HashSet<string> { Permissions.PosApprove }, Permissions.PosVoidSale));
        Assert.False(Permissions.CanApprove(new HashSet<string> { Permissions.PosVoidSale }, Permissions.PosVoidSale));
    }

    [Fact]
    public void Standard_supervisors_can_approve_voids_and_cashiers_cannot()
    {
        HashSet<string> Of(string role) => [.. Permissions.DefaultRoles.Single(r => r.Name == role).Permissions];
        Assert.True(Permissions.CanApprove(Of(Permissions.SupervisorRole), Permissions.PosVoidSale));
        Assert.False(Permissions.CanApprove(Of(Permissions.CashierRole), Permissions.PosVoidSale));
        Assert.False(Permissions.CanApprove(Of(Permissions.SupervisorRole), Permissions.SetupUsers));
    }
}

public class StandardRoleTests
{
    private static HashSet<string> Of(string role) => [.. Permissions.DefaultRoles.Single(r => r.Name == role).Permissions];

    [Fact]
    public void Managers_sign_in_to_the_web_for_their_own_store_and_supervisors_work_at_the_tills()
    {
        Assert.Contains(Permissions.WebSignIn, Of(Permissions.ManagerRole));
        Assert.DoesNotContain(Permissions.StoresAll, Of(Permissions.ManagerRole));
        Assert.DoesNotContain(Permissions.WebSignIn, Of(Permissions.SupervisorRole));
    }

    [Fact]
    public void Marketing_hr_and_the_accountant_work_for_every_store_and_production_for_its_own()
    {
        foreach (var role in new[] { Permissions.MarketingRole, Permissions.HrOfficerRole, Permissions.AccountantRole, Permissions.SuperAdministratorRole })
            Assert.Contains(Permissions.StoresAll, Of(role));
        foreach (var role in new[] { Permissions.ManagerRole, Permissions.AdministratorRole, Permissions.ProductionPlannerRole, Permissions.ProductionOperatorRole })
            Assert.DoesNotContain(Permissions.StoresAll, Of(role));
        // HR officers still need a second person to approve payroll.
        Assert.DoesNotContain(Permissions.PayrollApprove, Of(Permissions.HrOfficerRole));
    }

    [Fact]
    public void The_web_roles_of_one_store_are_the_administrator_the_manager_and_the_production_planner()
    {
        // Their users sign in to the web without seeing every store, so they need a home store.
        var oneStore = Permissions.DefaultRoles.Where(r => r.Permissions.Contains(Permissions.WebSignIn) && !r.Permissions.Contains(Permissions.StoresAll))
            .Select(r => r.Name).Order(StringComparer.Ordinal);
        Assert.Equal([Permissions.AdministratorRole, Permissions.ManagerRole, Permissions.ProductionPlannerRole], oneStore);
    }

    [Fact]
    public void The_keys_added_to_standard_roles_are_in_those_roles()
    {
        Assert.NotEmpty(Permissions.KeysAddedToStandardRoles);
        foreach (var (role, key) in Permissions.KeysAddedToStandardRoles)
        {
            Assert.True(Permissions.IsKnown(key), key);
            Assert.Contains(key, Of(role));
        }
        Assert.Contains((Permissions.ManagerRole, Permissions.WebSignIn), Permissions.KeysAddedToStandardRoles);
        Assert.Contains((Permissions.MarketingRole, Permissions.StoresAll), Permissions.KeysAddedToStandardRoles);
        Assert.Contains((Permissions.HrOfficerRole, Permissions.StoresAll), Permissions.KeysAddedToStandardRoles);
    }
}

public class SignInNameTests
{
    [Fact]
    public void An_e_mail_is_kept_trimmed_and_in_lower_case()
    {
        Assert.Equal("ann.lee@example.com", SignInNames.Email("  Ann.Lee@Example.COM "));
        Assert.Null(SignInNames.Email("   "));
        Assert.Null(SignInNames.Email(null));
    }

    [Theory]
    [InlineData("ann@example.com", true)]
    [InlineData("a@b.co", true)]
    [InlineData("first.last@mail.example.com", true)]
    [InlineData("a@b..c", true)]
    [InlineData("annexample.com", false)]
    [InlineData("@example.com", false)]
    [InlineData("ann@@example.com", false)]
    [InlineData("ann@shop@example.com", false)]
    [InlineData("ann@example", false)]
    [InlineData("ann@example.", false)]
    [InlineData("ann@.com", false)]
    [InlineData("ann lee@example.com", false)]
    [InlineData("ann@exa\tmple.com", false)]
    public void An_e_mail_has_one_at_a_dot_after_it_and_no_spaces(string text, bool email)
    {
        Assert.Equal(email, SignInNames.IsEmail(text));
        Assert.Equal(text.Contains('@', StringComparison.Ordinal), SignInNames.MayBeEmail(text));
    }
}
