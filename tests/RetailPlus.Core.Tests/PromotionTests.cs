using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Core.Tests;

/// <summary>The promotions engine: every kind, the schedule, coupons, combining and the sale draft.</summary>
public class PromotionTests
{
    private static readonly Guid Store = Guid.NewGuid(), OtherStore = Guid.NewGuid();
    private static readonly Guid Food = Guid.NewGuid(), Drinks = Guid.NewGuid(), Snacks = Guid.NewGuid();
    /// <summary>A Wednesday at noon.</summary>
    private static readonly DateTime Noon = new(2026, 11, 4, 12, 0, 0);

    private static PromotionContext At(DateTime? now = null, string[]? coupons = null, Dictionary<Guid, int>? uses = null) =>
        new(Store, now ?? Noon, coupons, uses);

    private static Promotion Promo(string name, PromotionKind kind, Action<Promotion>? set = null)
    {
        var p = new Promotion { Id = Guid.NewGuid(), Name = name, Kind = kind, Combinable = true };
        set?.Invoke(p);
        return p;
    }

    private static PromotionTarget Dept(Guid id, PromotionTargetRole role = PromotionTargetRole.Qualifies) => new() { Role = role, DepartmentId = id };

    private static PromotionTarget Code(string plu, PromotionTargetRole role = PromotionTargetRole.Qualifies, decimal quantity = 1) =>
        new() { Role = role, Plu = plu, Quantity = quantity };

    /// <summary>A line of <paramref name="qty"/> at <paramref name="price"/> each.</summary>
    private static PromotionLine Line(int index, string plu, decimal qty, decimal price, Guid? department = null, Guid? category = null, bool eligible = true) =>
        new(index, plu, department, category, qty, qty * price, Eligible: eligible);

    private static decimal Off(PromotionOutcome o, int index) => o.Lines.Where(l => l.Index == index).Sum(l => l.Discount);

    // Percent and amount off ------------------------------------------------------------------------------------------

    [Fact]
    public void Percent_off_a_department_takes_its_share_off_each_line_of_the_department()
    {
        var p = Promo("10% food", PromotionKind.PercentOff, p => { p.Percent = 10; p.Scope = PromotionScope.Departments; p.Targets = [Dept(Food)]; });
        var o = PromotionEngine.Evaluate([Line(0, "RICE", 2, 50, Food), Line(1, "COLA", 1, 30, Drinks)], [p], At());
        Assert.Equal(10m, Off(o, 0));
        Assert.Equal(0m, Off(o, 1));
        Assert.Equal(10m, o.Total);
        Assert.Equal([p.Id], o.Applied);
    }

    [Fact]
    public void Amount_off_items_is_per_unit_and_never_more_than_the_line()
    {
        var p = Promo("5 off", PromotionKind.AmountOff, p => { p.Amount = 5; p.Scope = PromotionScope.Items; p.Targets = [Code("GUM")]; });
        var o = PromotionEngine.Evaluate([Line(0, "gum", 3, 20), Line(1, "MINT", 1, 20), Line(2, "GUM", 1, 3)], [p], At());
        Assert.Equal(15m, Off(o, 0));
        Assert.Equal(0m, Off(o, 1));
        Assert.Equal(3m, Off(o, 2));
    }

    [Fact]
    public void Percent_off_a_category_matches_the_category()
    {
        var p = Promo("Snacks", PromotionKind.PercentOff, p => { p.Percent = 50; p.Scope = PromotionScope.Categories; p.Targets = [new PromotionTarget { CategoryId = Snacks }]; });
        var o = PromotionEngine.Evaluate([Line(0, "CHIPS", 1, 40, category: Snacks), Line(1, "RICE", 1, 40, Food)], [p], At());
        Assert.Equal(20m, Off(o, 0));
        Assert.Equal(20m, o.Total);
    }

    // Buy X get Y -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Buy_two_get_one_free_gives_the_cheapest_unit_of_each_group_of_three()
    {
        var p = Promo("3 for 2", PromotionKind.BuyXGetY, p => { p.BuyQuantity = 2; p.GetQuantity = 1; p.Scope = PromotionScope.Departments; p.Targets = [Dept(Drinks)]; });
        // 7 units: two groups of three, so the two cheapest units are free.
        var o = PromotionEngine.Evaluate([Line(0, "COLA", 4, 30, Drinks), Line(1, "WATER", 3, 10, Drinks)], [p], At());
        Assert.Equal(20m, o.Total);
        Assert.Equal(20m, Off(o, 1));
        // Both lines are under the promotion, the bought one with nothing off.
        Assert.Equal(2, o.Lines.Count);
        Assert.Equal(0m, Off(o, 0));
    }

    [Fact]
    public void Buy_x_get_y_at_a_discount_of_other_items()
    {
        var p = Promo("Buy 2 shirts, a tie at half price", PromotionKind.BuyXGetY, p =>
        {
            p.BuyQuantity = 2; p.GetQuantity = 1; p.GetPercent = 50; p.Scope = PromotionScope.Items;
            p.Targets = [Code("SHIRT"), Code("TIE", PromotionTargetRole.Reward)];
        });
        var o = PromotionEngine.Evaluate([Line(0, "SHIRT", 2, 100), Line(1, "TIE", 2, 40)], [p], At());
        Assert.Equal(20m, Off(o, 1));
        Assert.Equal(20m, o.Total);
        // One shirt alone gets nothing.
        Assert.Equal(0m, PromotionEngine.Evaluate([Line(0, "SHIRT", 1, 100), Line(1, "TIE", 1, 40)], [p], At()).Total);
    }

    // Bundles ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_bundle_sells_its_parts_together_for_its_price_as_often_as_they_are_all_there()
    {
        var p = Promo("Meal deal", PromotionKind.BundlePrice, p =>
        {
            p.BundlePrice = 400;
            p.Targets = [Code("BURGER", PromotionTargetRole.BundlePart), Code("FRIES", PromotionTargetRole.BundlePart), Code("COLA", PromotionTargetRole.BundlePart)];
        });
        // Burger 250, fries 150, cola 100: 500 regular, 400 as a bundle; two burgers but one cola make one bundle.
        var o = PromotionEngine.Evaluate([Line(0, "BURGER", 2, 250), Line(1, "FRIES", 2, 150), Line(2, "COLA", 1, 100)], [p], At());
        Assert.Equal(100m, o.Total);
        Assert.Equal(50m, Off(o, 0));
        Assert.Equal(30m, Off(o, 1));
        Assert.Equal(20m, Off(o, 2));
    }

    [Fact]
    public void A_bundle_part_can_need_more_than_one_and_a_bundle_dearer_than_its_parts_gives_nothing()
    {
        var two = Promo("Two pens and a pad", PromotionKind.BundlePrice, p =>
        {
            p.BundlePrice = 50;
            p.Targets = [Code("PEN", PromotionTargetRole.BundlePart, 2), Code("PAD", PromotionTargetRole.BundlePart)];
        });
        Assert.Equal(0m, PromotionEngine.Evaluate([Line(0, "PEN", 1, 20), Line(1, "PAD", 1, 30)], [two], At()).Total);
        Assert.Equal(20m, PromotionEngine.Evaluate([Line(0, "PEN", 2, 20), Line(1, "PAD", 1, 30)], [two], At()).Total);
        two.BundlePrice = 100;
        Assert.Equal(0m, PromotionEngine.Evaluate([Line(0, "PEN", 2, 20), Line(1, "PAD", 1, 30)], [two], At()).Total);
    }

    // Spend over ------------------------------------------------------------------------------------------------------

    [Fact]
    public void Spend_over_an_amount_gives_a_sale_discount_counted_after_the_line_promotions()
    {
        var percent = Promo("10% off food", PromotionKind.PercentOff, p => { p.Percent = 10; p.Scope = PromotionScope.Departments; p.Targets = [Dept(Food)]; });
        var spend = Promo("Spend 1000, get 50 off", PromotionKind.SpendOver, p => { p.SpendThreshold = 1000; p.Amount = 50; });
        // 1050 of food is 945 after its 10% off: below 1000, so no sale discount...
        var o = PromotionEngine.Evaluate([Line(0, "RICE", 1, 1050, Food)], [percent, spend], At());
        Assert.Null(o.Sale);
        // ...but 1200 of food is 1080, and lines that may take no promotion count towards the spend too.
        o = PromotionEngine.Evaluate([Line(0, "RICE", 1, 1100, Food), Line(1, "GIFT", 1, 100, eligible: false)], [percent, spend], At());
        Assert.Equal(50m, o.Sale!.Discount);
        Assert.Equal(160m, o.Total);
    }

    [Fact]
    public void Spend_over_as_a_percent()
    {
        var spend = Promo("Spend 500, 5% off", PromotionKind.SpendOver, p => { p.SpendThreshold = 500; p.Percent = 5; });
        Assert.Equal(30m, PromotionEngine.Evaluate([Line(0, "A", 2, 300)], [spend], At()).Sale!.Discount);
        Assert.Null(PromotionEngine.Evaluate([Line(0, "A", 1, 499.99m)], [spend], At()).Sale);
    }

    // Coupons ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_coupon_promotion_applies_only_with_its_code_and_until_it_is_used_up()
    {
        var p = Promo("Welcome", PromotionKind.PercentOff, p => { p.Percent = 20; p.CouponCode = "WELCOME20"; p.CouponMaxUses = 1; });
        var lines = new[] { Line(0, "A", 1, 100) };
        Assert.Equal(0m, PromotionEngine.Evaluate(lines, [p], At()).Total);
        var o = PromotionEngine.Evaluate(lines, [p], At(coupons: ["welcome20"]));
        Assert.Equal(20m, o.Total);
        Assert.Empty(o.Notes);
        o = PromotionEngine.Evaluate(lines, [p], At(coupons: ["WELCOME20"], uses: new() { [p.Id] = 1 }));
        Assert.Equal(0m, o.Total);
        Assert.Contains("used up", o.Notes.Single());
        Assert.Contains("not known", PromotionEngine.Evaluate(lines, [p], At(coupons: ["NOPE"])).Notes.Single());
        // Many uses: a limit of 3 still has one left after 2.
        p.CouponMaxUses = 3;
        Assert.Equal(20m, PromotionEngine.Evaluate(lines, [p], At(coupons: ["WELCOME20"], uses: new() { [p.Id] = 2 })).Total);
    }

    [Fact]
    public void A_coupon_that_gives_nothing_says_so()
    {
        var p = Promo("Drinks", PromotionKind.PercentOff, p => { p.Percent = 20; p.Scope = PromotionScope.Departments; p.Targets = [Dept(Drinks)]; p.CouponCode = "DRINKS"; });
        var o = PromotionEngine.Evaluate([Line(0, "RICE", 1, 100, Food)], [p], At(coupons: ["DRINKS"]));
        Assert.Contains("does not apply", o.Notes.Single());
    }

    // When and where --------------------------------------------------------------------------------------------------

    [Fact]
    public void A_happy_hour_runs_on_its_days_and_between_its_times()
    {
        var p = Promo("Happy hour", PromotionKind.PercentOff, p =>
        {
            p.Percent = 25;
            p.Days = PromotionEngine.DayBit(DayOfWeek.Wednesday) | PromotionEngine.DayBit(DayOfWeek.Friday);
            p.StartTime = new TimeOnly(17, 0);
            p.EndTime = new TimeOnly(19, 0);
        });
        var lines = new[] { Line(0, "BEER", 1, 100) };
        Assert.Equal(0m, PromotionEngine.Evaluate(lines, [p], At(Noon)).Total);
        Assert.Equal(25m, PromotionEngine.Evaluate(lines, [p], At(Noon.Date.AddHours(17.5))).Total);
        Assert.Equal(0m, PromotionEngine.Evaluate(lines, [p], At(Noon.Date.AddHours(19))).Total);
        Assert.Equal(0m, PromotionEngine.Evaluate(lines, [p], At(Noon.Date.AddDays(1).AddHours(18))).Total);   // Thursday
        // A late happy hour runs past midnight.
        p.Days = 0;
        p.StartTime = new TimeOnly(22, 0);
        p.EndTime = new TimeOnly(2, 0);
        Assert.Equal(25m, PromotionEngine.Evaluate(lines, [p], At(Noon.Date.AddHours(23))).Total);
        Assert.Equal(25m, PromotionEngine.Evaluate(lines, [p], At(Noon.Date.AddHours(1))).Total);
        Assert.Equal(0m, PromotionEngine.Evaluate(lines, [p], At(Noon)).Total);
    }

    [Fact]
    public void Dates_stores_and_the_switch_decide_whether_a_promotion_runs()
    {
        var p = Promo("November", PromotionKind.PercentOff, p => { p.Percent = 10; p.StartsOn = new DateOnly(2026, 11, 1); p.EndsOn = new DateOnly(2026, 11, 30); });
        Assert.True(PromotionEngine.RunsAt(p, Store, Noon));
        Assert.False(PromotionEngine.RunsAt(p, Store, new DateTime(2026, 12, 1)));
        Assert.False(PromotionEngine.RunsAt(p, Store, new DateTime(2026, 10, 31, 23, 59, 0)));
        p.AllStores = false;
        p.Stores = [new PromotionStore { StoreId = OtherStore }];
        Assert.False(PromotionEngine.RunsAt(p, Store, Noon));
        Assert.True(PromotionEngine.RunsAt(p, OtherStore, Noon));
        p.AllStores = true;
        p.IsActive = false;
        Assert.False(PromotionEngine.RunsAt(p, Store, Noon));
    }

    // Combining -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Combinable_promotions_add_up_but_a_line_takes_only_the_best_one()
    {
        var food = Promo("10% food", PromotionKind.PercentOff, p => { p.Percent = 10; p.Scope = PromotionScope.Departments; p.Targets = [Dept(Food)]; });
        var rice = Promo("Rice 30 off", PromotionKind.AmountOff, p => { p.Amount = 30; p.Scope = PromotionScope.Items; p.Targets = [Code("RICE")]; });
        var o = PromotionEngine.Evaluate([Line(0, "RICE", 1, 200, Food), Line(1, "BEANS", 1, 100, Food)], [food, rice], At());
        // Rice takes its 30 off (more than 10% of 200), the beans 10% of 100.
        Assert.Equal(30m, Off(o, 0));
        Assert.Equal(10m, Off(o, 1));
        Assert.Equal(rice.Id, o.Lines.Single(l => l.Index == 0).PromotionId);
        Assert.Equal(40m, o.Total);
    }

    [Fact]
    public void A_promotion_that_does_not_combine_is_used_alone_when_it_is_best_for_the_customer()
    {
        var a = Promo("5% everything", PromotionKind.PercentOff, p => p.Percent = 5);
        var b = Promo("Spend 100, get 10 off", PromotionKind.SpendOver, p => { p.SpendThreshold = 100; p.Amount = 10; });
        var alone = Promo("Clearance 12%", PromotionKind.PercentOff, p => { p.Percent = 12; p.Combinable = false; });
        var lines = new[] { Line(0, "A", 1, 200) };
        // Together a and b give 10 + 10 = 20; alone, 12% gives 24.
        var o = PromotionEngine.Evaluate(lines, [a, b, alone], At());
        Assert.Equal(24m, o.Total);
        Assert.Equal([alone.Id], o.Applied);
        // When the one used alone gives less, the combinable ones win.
        alone.Percent = 8;
        o = PromotionEngine.Evaluate(lines, [a, b, alone], At());
        Assert.Equal(20m, o.Total);
        Assert.Equal(2, o.Applied.Count);
    }

    [Fact]
    public void Priority_breaks_a_tie()
    {
        var low = Promo("Low", PromotionKind.PercentOff, p => { p.Percent = 10; p.Combinable = false; p.Priority = 1; });
        var high = Promo("High", PromotionKind.AmountOff, p => { p.Amount = 10; p.Combinable = false; p.Priority = 5; });
        var o = PromotionEngine.Evaluate([Line(0, "A", 1, 100)], [low, high], At());
        Assert.Equal([high.Id], o.Applied);
    }

    // The sale draft --------------------------------------------------------------------------------------------------

    [Fact]
    public void Applying_to_a_draft_sets_line_discounts_adds_a_spend_line_and_undoes_itself_when_the_sale_changes()
    {
        var food = Promo("10% food", PromotionKind.PercentOff, p => { p.Percent = 10; p.Scope = PromotionScope.Departments; p.Targets = [Dept(Food)]; });
        var spend = Promo("Spend 300, 25 off", PromotionKind.SpendOver, p => { p.SpendThreshold = 300; p.Amount = 25; });
        var draft = new SaleDraft
        {
            Lines =
            [
                new SaleDraftLine { ItemId = Guid.NewGuid(), Plu = "RICE", Description = "Rice", Quantity = 2, UnitPrice = 200, DepartmentId = Food, Taxable1 = true },
                new SaleDraftLine { ItemId = Guid.NewGuid(), Plu = "OPEN", Description = "Open price", Quantity = 1, UnitPrice = 50, DepartmentId = Food, AutoPriced = false },
                new SaleDraftLine { Kind = SaleLineKind.Return, ItemId = Guid.NewGuid(), Plu = "RICE", Description = "Rice", Quantity = -1, UnitPrice = 200, DepartmentId = Food },
            ],
        };
        var outcome = PromotionEngine.Apply(draft, [food, spend], At());
        Assert.Equal(40m, draft.Lines[0].PromotionDiscount);
        Assert.Equal(360m, draft.Lines[0].LineTotal);
        Assert.Equal("10% food", draft.Lines[0].PromotionName);
        Assert.Equal(0m, draft.Lines[1].PromotionDiscount);   // price changed by hand
        Assert.Equal(0m, draft.Lines[2].PromotionDiscount);   // a return
        var coupon = draft.Lines.Single(l => l.IsPromotionLine);
        Assert.Equal(-25m, coupon.LineTotal);
        Assert.Equal(spend.Id, coupon.PromotionId);
        Assert.Equal(65m, outcome.Total);

        // Applied again after the sale changed: nothing is counted twice, and the spend line goes when the spend drops.
        draft.Lines[0].Quantity = 1;
        PromotionEngine.Apply(draft, [food, spend], At());
        Assert.Equal(20m, draft.Lines[0].PromotionDiscount);
        Assert.DoesNotContain(draft.Lines, l => l.IsPromotionLine);
        PromotionEngine.Apply(draft, [], At());
        Assert.All(draft.Lines, l => Assert.Null(l.PromotionId));
    }

    [Fact]
    public void A_gift_card_line_takes_no_promotion()
    {
        var p = Promo("Everything 10%", PromotionKind.PercentOff, p => p.Percent = 10);
        var draft = new SaleDraft { Lines = [new SaleDraftLine { ItemId = Guid.NewGuid(), ItemType = ItemType.GiftCard, Plu = "GIFT", Quantity = 1, UnitPrice = 100 }] };
        Assert.Equal(0m, PromotionEngine.Apply(draft, [p], At()).Total);
    }

    [Fact]
    public void Describe_says_what_a_promotion_gives_and_when()
    {
        var p = Promo("Happy hour", PromotionKind.PercentOff, p =>
        {
            p.Percent = 10; p.Scope = PromotionScope.Departments; p.Targets = [Dept(Food)]; p.CouponCode = "HAPPY";
            p.Days = PromotionEngine.DayBit(DayOfWeek.Friday); p.StartTime = new TimeOnly(17, 0); p.EndTime = new TimeOnly(19, 0);
        });
        Assert.Equal("10% off department FOOD, with coupon HAPPY, Fri 17:00–19:00", PromotionEngine.Describe(p, _ => "FOOD"));
        var bundle = Promo("Deal", PromotionKind.BundlePrice, p => { p.BundlePrice = 400; p.Targets = [Code("A", PromotionTargetRole.BundlePart, 2), Code("B", PromotionTargetRole.BundlePart)]; });
        Assert.Equal("2 × A + 1 × B for 400", PromotionEngine.Describe(bundle));
        var bogo = Promo("Bogo", PromotionKind.BuyXGetY, p => { p.BuyQuantity = 1; p.GetQuantity = 1; });
        Assert.Equal("Buy 1 of all items, get 1 free", PromotionEngine.Describe(bogo));
    }
}
