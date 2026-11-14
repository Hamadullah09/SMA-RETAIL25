using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Core.Tests;

/// <summary>
/// Refunds for goods returned with the receipt: what was paid under a promotion, a multi-buy or a bundle broken by a
/// partial return, a sale-wide discount shared over the goods, and earlier returns of the same receipt.
/// </summary>
public class ReturnRefundTests
{
    private static readonly Guid Store = Guid.NewGuid();
    private static readonly DateTime Noon = new(2026, 11, 4, 12, 0, 0);

    private static Promotion Promo(string name, PromotionKind kind, Action<Promotion> set)
    {
        var p = new Promotion { Id = Guid.NewGuid(), Name = name, Kind = kind, Combinable = true, Scope = PromotionScope.Items };
        set(p);
        return p;
    }

    private static PromotionTarget Code(string plu, PromotionTargetRole role = PromotionTargetRole.Qualifies) => new() { Role = role, Plu = plu };

    private static SaleDraftLine Goods(string plu, decimal qty, decimal price, decimal discount = 0) => new()
    {
        ItemId = Guid.NewGuid(), Plu = plu, Description = "Item " + plu, Quantity = qty, UnitPrice = price, DiscountAmount = discount,
        AutoPriced = discount == 0, Taxable1 = true, UnitCost = 1,
    };

    /// <summary>The sale as the till saves it: the promotions worked out on the draft, then each line as SalesService writes it.</summary>
    private static Sale Sell(SaleDraft draft, params Promotion[] promotions)
    {
        PromotionEngine.Apply(draft, promotions, new PromotionContext(Store, Noon, draft.CouponCodes));
        var totals = draft.Calculate(new TaxSetup("GST", 10, null, 0, false, false));
        var sale = new Sale { Id = Guid.NewGuid(), InvoiceNumber = 1001, Subtotal = totals.Subtotal, SubtotalDiscount = totals.SubtotalDiscount, Tax1 = totals.Tax1, Total = totals.Total };
        for (int i = 0, t = 0; i < draft.Lines.Count; i++)
        {
            var l = draft.Lines[i];
            decimal tax1 = l.Kind == SaleLineKind.Text ? 0 : totals.LineTaxes[t++].Tax1;
            sale.Lines.Add(new SaleLine
            {
                Id = Guid.NewGuid(), LineNumber = i + 1, Kind = l.Kind, ItemId = l.ItemId, Plu = l.Plu, Description = l.Description, Quantity = l.Quantity,
                UnitPrice = l.UnitPrice, DiscountAmount = l.TotalDiscount, LineTotal = l.Kind == SaleLineKind.Text ? 0 : l.LineTotal, Taxable1 = l.Taxable1, Tax1 = tax1,
                Cogs = l.UnitCost * l.Quantity, PromotionId = l.PromotionId, PromotionName = l.PromotionName, PromotionDiscount = l.PromotionDiscount,
            });
        }
        return sale;
    }

    private static Dictionary<Guid, Promotion> Known(params Promotion[] promotions) => promotions.ToDictionary(p => p.Id);

    private static SaleLine Of(Sale sale, string plu) => sale.Lines.Single(l => l.Plu == plu && l.Kind == SaleLineKind.Sale);

    /// <summary>Returns <paramref name="quantities"/> one after the other, each after the ones before, and gives the refunds.</summary>
    private static List<decimal> ReturnOneByOne(Sale sale, string plu, IReadOnlyDictionary<Guid, Promotion>? known, params decimal[] quantities)
    {
        var earlier = new List<EarlierReturn>();
        var refunds = new List<decimal>();
        foreach (var q in quantities)
        {
            var quote = ReturnRefunds.Quote(sale, Of(sale, plu).Id, q, earlier, known);
            earlier.Add(new EarlierReturn(quote.Line.Id, q, quote.Refund));
            refunds.Add(quote.Refund);
        }
        return refunds;
    }

    // No promotion ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Without_a_promotion_each_unit_comes_back_at_what_was_paid_for_it_and_the_last_one_takes_the_cents()
    {
        // 3 for 10.00 by hand: each refund brings the total back to what a third, two thirds and all of 10.00 come to.
        var sale = Sell(new SaleDraft { Lines = [Goods("PEN", 3, 5, discount: 5)] });
        Assert.Equal([3.33m, 3.34m, 3.33m], ReturnOneByOne(sale, "PEN", null, 1, 1, 1));
        var quote = ReturnRefunds.Quote(sale, Of(sale, "PEN").Id, 1, []);
        Assert.Equal(0m, quote.PromotionTakenBack);
        Assert.Equal(0m, quote.SaleWideShare);
        Assert.Equal(1.67m, quote.LineDiscount);
    }

    [Fact]
    public void No_more_comes_back_than_was_sold_and_only_goods_sold_come_back()
    {
        var sale = Sell(new SaleDraft { Lines = [Goods("PEN", 2, 5), new SaleDraftLine { Kind = SaleLineKind.Text, Description = "Thank you" }] });
        var pen = Of(sale, "PEN");
        Assert.Throws<ArgumentOutOfRangeException>(() => ReturnRefunds.Quote(sale, pen.Id, 3, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReturnRefunds.Quote(sale, pen.Id, 1, [new EarlierReturn(pen.Id, 2, 10)]));
        Assert.Equal(0m, ReturnRefunds.Left(pen, [new EarlierReturn(pen.Id, 2, 10)]));
        Assert.Throws<ArgumentException>(() => ReturnRefunds.Quote(sale, sale.Lines.Single(l => l.Kind == SaleLineKind.Text).Id, 1, []));
    }

    // Percent off -----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_unit_under_a_percent_off_comes_back_at_its_promotion_price_and_the_return_line_takes_the_promotion_back()
    {
        var tenOff = Promo("10% off shirts", PromotionKind.PercentOff, p => { p.Percent = 10; p.Targets = [Code("SHIRT")]; });
        var sale = Sell(new SaleDraft { Lines = [Goods("SHIRT", 2, 100)] }, tenOff);
        Assert.Equal(180m, Of(sale, "SHIRT").LineTotal);

        var quote = ReturnRefunds.Quote(sale, Of(sale, "SHIRT").Id, 1, [], Known(tenOff));
        Assert.Equal(90m, quote.Refund);
        Assert.Equal(100m, quote.Regular);
        Assert.Equal(10m, quote.PromotionTakenBack);
        Assert.Contains("after 10% off shirts", quote.Explanation);

        var line = ReturnRefunds.DraftLine(sale, quote);
        Assert.Equal((SaleLineKind.Return, -1m, 100m, -90m), (line.Kind, line.Quantity, line.UnitPrice, line.LineTotal));
        Assert.Equal((tenOff.Id, -10m), (line.PromotionId, line.PromotionDiscount));
        Assert.Equal((Of(sale, "SHIRT").Id, 1001L), (line.ReturnedLineId, line.ReturnedInvoice));
        Assert.True(line.Taxable1);
        Assert.False(line.AutoPriced);
        Assert.False(line.IsUnverifiedReturn);
        Assert.Equal(1m, line.UnitCost);

        // The engine works the sale out again without touching what the return carries.
        var draft = new SaleDraft { Lines = [line] };
        PromotionEngine.Apply(draft, [tenOff], new PromotionContext(Store, Noon));
        Assert.Equal((tenOff.Id, -10m, -90m), (line.PromotionId, line.PromotionDiscount, line.LineTotal));
    }

    [Fact]
    public void A_line_not_taxed_on_the_sale_is_not_taxed_on_the_return()
    {
        var sale = Sell(new SaleDraft { Lines = [Goods("BREAD", 1, 4)], ExemptTax1 = true });
        var line = ReturnRefunds.DraftLine(sale, ReturnRefunds.Quote(sale, Of(sale, "BREAD").Id, 1, []));
        Assert.False(line.Taxable1);
        Assert.Equal(-4m, line.LineTotal);
    }

    // Buy X get Y -----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_unit_back_of_three_bought_as_buy_two_get_one_gives_nothing_back_and_the_rest_comes_back_at_what_was_paid()
    {
        var threeForTwo = Promo("3 for 2", PromotionKind.BuyXGetY, p => { p.BuyQuantity = 2; p.GetQuantity = 1; p.Targets = [Code("SOCKS")]; });
        var sale = Sell(new SaleDraft { Lines = [Goods("SOCKS", 3, 4)] }, threeForTwo);
        Assert.Equal(8m, Of(sale, "SOCKS").LineTotal);

        var first = ReturnRefunds.Quote(sale, Of(sale, "SOCKS").Id, 1, [], Known(threeForTwo));
        Assert.Equal(0m, first.Refund);
        Assert.Equal(4m, first.PromotionTakenBack);
        Assert.Contains("less 4.00 of 3 for 2", first.Explanation);
        var line = ReturnRefunds.DraftLine(sale, first);
        Assert.Equal((0m, -4m), (line.LineTotal, line.PromotionDiscount));

        // The two kept cost what was paid for three: each further one comes back at full price, 8.00 in all.
        Assert.Equal([0m, 4m, 4m], ReturnOneByOne(sale, "SOCKS", Known(threeForTwo), 1, 1, 1));
        Assert.Equal([0m, 8m], ReturnOneByOne(sale, "SOCKS", Known(threeForTwo), 1, 2));
    }

    [Fact]
    public void Of_six_bought_as_buy_two_get_one_the_goods_kept_keep_the_groups_they_still_make()
    {
        var threeForTwo = Promo("3 for 2", PromotionKind.BuyXGetY, p => { p.BuyQuantity = 2; p.GetQuantity = 1; p.Targets = [Code("SOCKS")]; });
        var sale = Sell(new SaleDraft { Lines = [Goods("SOCKS", 6, 4)] }, threeForTwo);
        Assert.Equal(16m, Of(sale, "SOCKS").LineTotal);
        // One back: the 5 kept make one group, so 4.00 of the promotion goes and nothing comes back.
        Assert.Equal(0m, ReturnRefunds.Quote(sale, Of(sale, "SOCKS").Id, 1, [], Known(threeForTwo)).Refund);
        // Three back: the 3 kept still make a group (8.00), so 8.00 of the 16.00 comes back.
        var three = ReturnRefunds.Quote(sale, Of(sale, "SOCKS").Id, 3, [], Known(threeForTwo));
        Assert.Equal((8m, 4m), (three.Refund, three.PromotionTakenBack));
        Assert.Equal(16m, ReturnOneByOne(sale, "SOCKS", Known(threeForTwo), 1, 2, 3).Sum());
    }

    [Fact]
    public void A_buy_x_get_y_the_till_does_not_know_is_shared_over_its_units()
    {
        var threeForTwo = Promo("3 for 2", PromotionKind.BuyXGetY, p => { p.BuyQuantity = 2; p.GetQuantity = 1; p.Targets = [Code("SOCKS")]; });
        var sale = Sell(new SaleDraft { Lines = [Goods("SOCKS", 3, 4)] }, threeForTwo);
        Assert.Equal([2.67m, 2.66m, 2.67m], ReturnOneByOne(sale, "SOCKS", null, 1, 1, 1));
    }

    // Bundles ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_part_of_a_bundle_back_comes_back_less_what_the_bundle_no_longer_gives_the_part_kept()
    {
        var bundle = Promo("Shampoo and conditioner for 80", PromotionKind.BundlePrice, p =>
        {
            p.BundlePrice = 80;
            p.Targets = [Code("SHAMPOO", PromotionTargetRole.BundlePart), Code("COND", PromotionTargetRole.BundlePart)];
        });
        var sale = Sell(new SaleDraft { Lines = [Goods("SHAMPOO", 1, 60), Goods("COND", 1, 40)] }, bundle);
        Assert.Equal((48m, 32m), (Of(sale, "SHAMPOO").LineTotal, Of(sale, "COND").LineTotal));

        // The conditioner back: the shampoo kept costs 60.00 on its own, so 20.00 of the 80.00 comes back.
        var cond = ReturnRefunds.Quote(sale, Of(sale, "COND").Id, 1, [], Known(bundle));
        Assert.Equal((20m, 40m, 20m), (cond.Refund, cond.Regular, cond.PromotionTakenBack));
        var line = ReturnRefunds.DraftLine(sale, cond);
        Assert.Equal((-20m, -20m, bundle.Id), (line.LineTotal, line.PromotionDiscount, line.PromotionId));

        // Then the shampoo: the rest of what was paid.
        var shampoo = ReturnRefunds.Quote(sale, Of(sale, "SHAMPOO").Id, 1, [new EarlierReturn(Of(sale, "COND").Id, 1, 20)], Known(bundle));
        Assert.Equal(60m, shampoo.Refund);
        Assert.Equal(0m, shampoo.PromotionTakenBack);
    }

    [Fact]
    public void Refunds_of_a_bundle_never_add_up_to_more_than_was_paid()
    {
        // A dear part and a cheap one: the cheap one back gives nothing (the dear one kept costs more than the bundle).
        var bundle = Promo("Kit for 50", PromotionKind.BundlePrice, p =>
        {
            p.BundlePrice = 50;
            p.Targets = [Code("DRILL", PromotionTargetRole.BundlePart), Code("BIT", PromotionTargetRole.BundlePart)];
        });
        var sale = Sell(new SaleDraft { Lines = [Goods("DRILL", 1, 100), Goods("BIT", 1, 1)] }, bundle);
        var bit = ReturnRefunds.Quote(sale, Of(sale, "BIT").Id, 1, [], Known(bundle));
        Assert.Equal(0m, bit.Refund);
        var drill = ReturnRefunds.Quote(sale, Of(sale, "DRILL").Id, 1, [new EarlierReturn(Of(sale, "BIT").Id, 1, 0)], Known(bundle));
        Assert.Equal(50m, drill.Refund);
        // What was not refunded is the bundle's: with both returns its 51.00 of discount is taken back in full.
        Assert.Equal(51m, bit.PromotionTakenBack + drill.PromotionTakenBack);
        Assert.Equal(51m, sale.Lines.Sum(l => l.PromotionDiscount));
    }

    // Sale-wide discounts ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_spend_over_coupon_is_shared_over_the_goods_and_each_refund_is_less_its_share()
    {
        var coupon = Promo("Spend 100, 15 off", PromotionKind.SpendOver, p => { p.SpendThreshold = 100; p.Amount = 15; p.Scope = PromotionScope.AllItems; p.CouponCode = "SAVE15"; });
        var sale = Sell(new SaleDraft { Lines = [Goods("COAT", 1, 100), Goods("HAT", 1, 50)], CouponCodes = ["SAVE15"] }, coupon);
        Assert.Equal(-15m, sale.Lines.Single(l => l.Kind == SaleLineKind.Coupon).LineTotal);
        Assert.Equal(0.9m, ReturnRefunds.SaleWideFactor(sale));

        var hat = ReturnRefunds.Quote(sale, Of(sale, "HAT").Id, 1, [], Known(coupon));
        Assert.Equal((45m, 5m), (hat.Refund, hat.SaleWideShare));
        Assert.Contains("less 5.00, its share of the sale's discounts", hat.Explanation);
        var line = ReturnRefunds.DraftLine(sale, hat);
        Assert.Equal((-45m, -5m, 0m), (line.LineTotal, line.DiscountAmount, line.PromotionDiscount));
        Assert.Null(line.PromotionId);
        Assert.Equal(90m, ReturnRefunds.Quote(sale, Of(sale, "COAT").Id, 1, [], Known(coupon)).Refund);
    }

    [Fact]
    public void The_discount_on_the_sale_and_a_line_promotion_both_come_off_the_refund()
    {
        var tenOff = Promo("10% off shirts", PromotionKind.PercentOff, p => { p.Percent = 10; p.Targets = [Code("SHIRT")]; });
        var sale = Sell(new SaleDraft { Lines = [Goods("SHIRT", 1, 100), Goods("TIE", 1, 30)], SubtotalDiscountPercent = 10 }, tenOff);
        Assert.Equal(12m, sale.SubtotalDiscount);
        var shirt = ReturnRefunds.Quote(sale, Of(sale, "SHIRT").Id, 1, [], Known(tenOff));
        Assert.Equal((81m, 10m, 9m), (shirt.Refund, shirt.PromotionTakenBack, shirt.SaleWideShare));
        // Everything back: what was paid for the goods, before tax.
        Assert.Equal(108m, shirt.Refund + ReturnRefunds.Quote(sale, Of(sale, "TIE").Id, 1, [], Known(tenOff)).Refund);
        Assert.Equal(108m, sale.Subtotal - sale.SubtotalDiscount);
    }

    // The sale draft --------------------------------------------------------------------------------------------------

    [Fact]
    public void A_return_with_the_receipt_joins_no_other_line_and_says_where_its_price_comes_from()
    {
        var sale = Sell(new SaleDraft { Lines = [Goods("PEN", 2, 5)] });
        var item = new Item { Id = sale.Lines[0].ItemId!.Value, Plu = "PEN", Name = "Pen" };
        var withReceipt = ReturnRefunds.DraftLine(sale, ReturnRefunds.Quote(sale, sale.Lines[0].Id, 1, []));
        Assert.Null(SaleDraft.LineToJoin([withReceipt], item, null, SaleLineKind.Return, 1));
        Assert.Equal("Receipt 1001: the price paid", withReceipt.ReturnNote);

        var noReceipt = new SaleDraftLine { Kind = SaleLineKind.Return, ItemId = item.Id, Plu = "PEN", Quantity = -1, UnitPrice = 6 };
        Assert.Same(noReceipt, SaleDraft.LineToJoin([withReceipt, noReceipt], item, null, SaleLineKind.Return, 1));
        Assert.True(noReceipt.IsUnverifiedReturn);
        Assert.Equal("No receipt: today's price, not checked", noReceipt.ReturnNote);
        Assert.Null(Goods("PEN", 1, 5).ReturnNote);
    }
}
