using FluentAssertions;
using Retail25.Application.Catalog.Commands;
using Retail25.Application.UnitTests.Masters;
using Retail25.Domain.Catalog;
using Xunit;

namespace Retail25.Application.UnitTests.Bulk;

/// <summary>
/// Batch repricing. Every one of these is arithmetic against a figure worked out by hand, because a
/// batch reprice has no undo and the only safety net is that the sums are right.
/// </summary>
public sealed class BulkAdjustTests
{
    private static BulkFilter All(MastersTestHarness harness) => new(harness.Location.Id);

    [Fact]
    public async Task A_percentage_rise_moves_every_matching_item()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("A-1", "Widget", price: 10m);
        await harness.AddProductAsync("B-2", "Gadget", price: 25m);

        var result = await harness.BulkAdjust.Handle(
            new ApplyBulkPriceChangeCommand(All(harness), BulkPriceTarget.RegularPrice, BulkAdjustMethod.Percentage, 10m),
            CancellationToken.None);

        result.Value.Should().Be(2);

        var prices = harness.Db.Products.ToDictionary(p => p.StockCode, p => p.RegularPrice);
        prices["A-1"].Should().Be(11.00m);
        prices["B-2"].Should().Be(27.50m);
    }

    [Fact]
    public async Task A_negative_percentage_is_a_reduction()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("A-1", "Widget", price: 20m);

        await harness.BulkAdjust.Handle(
            new ApplyBulkPriceChangeCommand(All(harness), BulkPriceTarget.RegularPrice, BulkAdjustMethod.Percentage, -25m),
            CancellationToken.None);

        harness.Db.Products.Single().RegularPrice.Should().Be(15.00m);
    }

    [Fact]
    public async Task A_fixed_amount_adds_the_same_money_to_each()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("A-1", "Widget", price: 10m);
        await harness.AddProductAsync("B-2", "Gadget", price: 25m);

        await harness.BulkAdjust.Handle(
            new ApplyBulkPriceChangeCommand(All(harness), BulkPriceTarget.RegularPrice, BulkAdjustMethod.FixedAmount, 1.50m),
            CancellationToken.None);

        var prices = harness.Db.Products.ToDictionary(p => p.StockCode, p => p.RegularPrice);
        prices["A-1"].Should().Be(11.50m);
        prices["B-2"].Should().Be(26.50m);
    }

    [Fact]
    public async Task Set_to_puts_every_item_on_the_same_price()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("A-1", "Widget", price: 10m);
        await harness.AddProductAsync("B-2", "Gadget", price: 25m);

        await harness.BulkAdjust.Handle(
            new ApplyBulkPriceChangeCommand(All(harness), BulkPriceTarget.RegularPrice, BulkAdjustMethod.SetTo, 4.99m),
            CancellationToken.None);

        harness.Db.Products.Select(p => p.RegularPrice).Should().AllBeEquivalentTo(4.99m);
    }

    /// <summary>Priced off average cost, not last cost — one outlier delivery should not set the shelf.</summary>
    [Fact]
    public async Task Markup_on_cost_prices_from_the_average()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget", price: 1m);
        product.RecalculateAvgCost(10m, 4m, 0m);
        product.UpdatePricing(1m, lastCost: 99m, avgCost: product.AvgCost);
        await harness.Db.SaveChangesAsync();

        await harness.BulkAdjust.Handle(
            new ApplyBulkPriceChangeCommand(
                All(harness), BulkPriceTarget.RegularPrice, BulkAdjustMethod.MarkupOnCost, 50m, PriceRounding.NearestCent),
            CancellationToken.None);

        // Average cost is 4.00 (nothing on hand before), so 50% on top is 6.00.
        harness.Db.Products.Single().RegularPrice.Should().Be(6.00m);
    }

    [Fact]
    public async Task Markup_on_cost_is_refused_for_the_cost_column()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("A-1", "Widget");

        var result = await harness.BulkAdjust.Handle(
            new ApplyBulkPriceChangeCommand(All(harness), BulkPriceTarget.LastCost, BulkAdjustMethod.MarkupOnCost, 50m),
            CancellationToken.None);

        result.Error.Should().Be(BulkAdjustHandlers.MarkupNeedsPrice);
    }

    [Fact]
    public async Task Targeting_cost_leaves_the_shelf_price_alone()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget", price: 20m);
        product.UpdatePricing(20m, 8m, 8m);
        await harness.Db.SaveChangesAsync();

        await harness.BulkAdjust.Handle(
            new ApplyBulkPriceChangeCommand(All(harness), BulkPriceTarget.LastCost, BulkAdjustMethod.Percentage, 25m),
            CancellationToken.None);

        var saved = harness.Db.Products.Single();
        saved.LastCost.Should().Be(10.00m);
        saved.RegularPrice.Should().Be(20m);
    }

    /// <summary>
    /// The batch is refused outright rather than applied to the items it would not break. Half a
    /// repriced catalogue is worse than none of one.
    /// </summary>
    [Fact]
    public async Task A_batch_that_would_go_negative_writes_nothing()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("A-1", "Cheap", price: 2m);
        await harness.AddProductAsync("B-2", "Dear", price: 50m);

        var result = await harness.BulkAdjust.Handle(
            new ApplyBulkPriceChangeCommand(All(harness), BulkPriceTarget.RegularPrice, BulkAdjustMethod.FixedAmount, -5m),
            CancellationToken.None);

        result.Error.Should().Be(BulkAdjustHandlers.WouldGoNegative);
        harness.Db.Products.Single(p => p.StockCode == "B-2").RegularPrice.Should().Be(50m);
    }

    [Fact]
    public async Task A_selection_that_matches_nothing_is_reported_rather_than_silently_doing_nothing()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var result = await harness.BulkAdjust.Handle(
            new ApplyBulkPriceChangeCommand(All(harness), BulkPriceTarget.RegularPrice, BulkAdjustMethod.Percentage, 10m),
            CancellationToken.None);

        result.Error.Should().Be(BulkAdjustHandlers.NothingMatched);
    }

    [Fact]
    public async Task The_department_filter_narrows_what_is_touched()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var hardware = await harness.AddDepartmentAsync("Hardware");
        await harness.AddProductAsync("A-1", "Widget", price: 10m, departmentId: hardware.Id);
        await harness.AddProductAsync("B-2", "Loose", price: 10m);

        var result = await harness.BulkAdjust.Handle(
            new ApplyBulkPriceChangeCommand(
                new BulkFilter(harness.Location.Id, DepartmentId: hardware.Id),
                BulkPriceTarget.RegularPrice, BulkAdjustMethod.Percentage, 100m),
            CancellationToken.None);

        result.Value.Should().Be(1);
        harness.Db.Products.Single(p => p.StockCode == "B-2").RegularPrice.Should().Be(10m);
    }

    [Fact]
    public async Task The_supplier_filter_reaches_through_the_link_table()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var supplier = await harness.AddSupplierAsync("Acme", "SUP-1");
        var linked = await harness.AddProductAsync("A-1", "Widget", price: 10m);
        await harness.AddProductAsync("B-2", "Unlinked", price: 10m);
        await harness.AddProductSupplierAsync(linked, supplier, 1, 5m);

        var result = await harness.BulkAdjust.Handle(
            new ApplyBulkPriceChangeCommand(
                new BulkFilter(harness.Location.Id, SupplierId: supplier.Id),
                BulkPriceTarget.RegularPrice, BulkAdjustMethod.SetTo, 1m),
            CancellationToken.None);

        result.Value.Should().Be(1);
        harness.Db.Products.Single(p => p.StockCode == "B-2").RegularPrice.Should().Be(10m);
    }

    [Fact]
    public async Task A_deleted_item_is_never_repriced()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var kept = await harness.AddProductAsync("A-1", "Widget", price: 10m);
        var removed = await harness.AddProductAsync("B-2", "Gone", price: 10m);

        harness.Db.Products.Remove(removed);
        await harness.Db.SaveChangesAsync();

        var result = await harness.BulkAdjust.Handle(
            new ApplyBulkPriceChangeCommand(All(harness), BulkPriceTarget.RegularPrice, BulkAdjustMethod.SetTo, 1m),
            CancellationToken.None);

        result.Value.Should().Be(1);
        harness.Db.Products.Single(p => p.Id == removed.Id).RegularPrice.Should().Be(10m);
        harness.Db.Products.Single(p => p.Id == kept.Id).RegularPrice.Should().Be(1m);
    }

    [Fact]
    public async Task An_item_already_at_the_target_price_is_not_counted_as_changed()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("A-1", "Widget", price: 4.99m);
        await harness.AddProductAsync("B-2", "Gadget", price: 10m);

        var result = await harness.BulkAdjust.Handle(
            new ApplyBulkPriceChangeCommand(All(harness), BulkPriceTarget.RegularPrice, BulkAdjustMethod.SetTo, 4.99m),
            CancellationToken.None);

        result.Value.Should().Be(1);
    }

    [Fact]
    public async Task The_preview_shows_the_proposal_without_writing_it()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("A-1", "Widget", price: 10m);

        var result = await harness.BulkAdjust.Handle(
            new PreviewBulkPriceChangeQuery(All(harness), BulkPriceTarget.RegularPrice, BulkAdjustMethod.Percentage, 10m),
            CancellationToken.None);

        var row = result.Value.Rows.Should().ContainSingle().Subject;
        row.Current.Should().Be(10m);
        row.Proposed.Should().Be(11m);
        result.Value.MatchedCount.Should().Be(1);

        harness.Db.Products.Single().RegularPrice.Should().Be(10m);
    }

    /// <summary>
    /// The negative count is over the whole selection, not the sample — the operator has to see that
    /// a hundred items would break even when only two hundred rows are on screen.
    /// </summary>
    [Fact]
    public async Task The_preview_counts_negatives_beyond_the_rows_it_shows()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        for (var i = 1; i <= 6; i++)
        {
            await harness.AddProductAsync($"A-{i}", $"Item {i}", price: i);
        }

        var result = await harness.BulkAdjust.Handle(
            new PreviewBulkPriceChangeQuery(
                All(harness), BulkPriceTarget.RegularPrice, BulkAdjustMethod.FixedAmount, -3.50m, Take: 2),
            CancellationToken.None);

        result.Value.ShownCount.Should().Be(2);
        result.Value.MatchedCount.Should().Be(6);

        // Items priced 1, 2 and 3 all fall below zero once 3.50 comes off.
        result.Value.WouldGoNegative.Should().Be(3);
    }

    [Theory]
    [InlineData(4.8737, PriceRounding.NearestCent, 4.87)]
    [InlineData(4.8737, PriceRounding.WholeNumber, 5)]
    [InlineData(4.20, PriceRounding.EndsIn99, 3.99)]
    [InlineData(4.80, PriceRounding.EndsIn99, 4.99)]
    [InlineData(4.99, PriceRounding.EndsIn99, 4.99)]
    [InlineData(4.20, PriceRounding.EndsIn95, 3.95)]
    [InlineData(4.80, PriceRounding.EndsIn95, 4.95)]
    [InlineData(0.40, PriceRounding.EndsIn99, 0.99)]
    [InlineData(12.3456, PriceRounding.None, 12.3456)]
    public void Rounding_lands_where_it_should(double raw, PriceRounding rounding, double expected)
        => BulkAdjustHandlers.Round((decimal)raw, rounding).Should().Be((decimal)expected);

    /// <summary>
    /// 4.20 rounds down to 3.99 rather than up to 4.99: snapping to the nearest charm price is a
    /// rounding, and forcing everything upward would be a 19% rise nobody asked for.
    /// </summary>
    [Fact]
    public void Charm_rounding_picks_the_closer_candidate()
    {
        BulkAdjustHandlers.Round(4.40m, PriceRounding.EndsIn99).Should().Be(3.99m);
        BulkAdjustHandlers.Round(4.60m, PriceRounding.EndsIn99).Should().Be(4.99m);
    }

    [Fact]
    public async Task A_tax_flag_left_null_is_left_alone()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget");
        product.SetTaxFlags(true, true);
        await harness.Db.SaveChangesAsync();

        var result = await harness.BulkAdjust.Handle(
            new ApplyBulkTaxChangeCommand(All(harness), Tax1Applies: false, Tax2Applies: null),
            CancellationToken.None);

        result.Value.Should().Be(1);

        var saved = harness.Db.Products.Single();
        saved.Tax1Applies.Should().BeFalse();
        saved.Tax2Applies.Should().BeTrue();
    }

    [Fact]
    public async Task Asking_for_no_tax_change_at_all_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("A-1", "Widget");

        var result = await harness.BulkAdjust.Handle(
            new ApplyBulkTaxChangeCommand(All(harness), null, null), CancellationToken.None);

        result.Error.Should().Be(BulkAdjustHandlers.NoChangeRequested);
    }

    /// <summary>
    /// A gift card is never taxable, whatever a batch asks for. The count has to reflect what
    /// actually happened, not what was requested.
    /// </summary>
    [Fact]
    public async Task A_gift_card_stays_untaxed_and_is_not_counted_as_changed()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("GC-1", "Gift card", type: ProductType.GiftCard);

        var result = await harness.BulkAdjust.Handle(
            new ApplyBulkTaxChangeCommand(All(harness), Tax1Applies: true, Tax2Applies: true),
            CancellationToken.None);

        result.Value.Should().Be(0);

        var saved = harness.Db.Products.Single();
        saved.Tax1Applies.Should().BeFalse();
        saved.Tax2Applies.Should().BeFalse();
    }

    [Fact]
    public async Task An_item_already_on_the_asked_for_flags_is_not_counted()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("A-1", "Widget");

        var result = await harness.BulkAdjust.Handle(
            new ApplyBulkTaxChangeCommand(All(harness), Tax1Applies: true, Tax2Applies: true),
            CancellationToken.None);

        result.Value.Should().Be(0);
    }
}
