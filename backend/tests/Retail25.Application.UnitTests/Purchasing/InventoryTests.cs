using FluentAssertions;
using Retail25.Application.Inventory;
using Retail25.Application.UnitTests.Masters;
using Xunit;

namespace Retail25.Application.UnitTests.Purchasing;

/// <summary>
/// Manual stock receiving, reason-coded adjustments and case-break (guide p.20, p.22, p.43) — the
/// three inventory actions that had a domain shape but no way to reach them (Stage 2).
/// </summary>
public sealed class InventoryTests
{
    [Fact]
    public async Task Receiving_stock_moves_on_hand_and_rolls_the_average_cost()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("SKU-1", "Widget", onHand: 10m);
        product.UpdatePricing(regularPrice: 10m, lastCost: 4m, avgCost: 4m);
        await harness.Db.SaveChangesAsync();

        var result = await harness.Inventory.Handle(
            new ReceiveStockCommand(product.Id, harness.Location.Id, Quantity: 10m, UnitCost: 6m), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.OnHand.Should().Be(20m);

        // newAvg = (10*4 + 10*6) / 20 = 5.00
        (await harness.Db.Products.FindAsync(product.Id))!.AvgCost.Should().Be(5.00m);
    }

    [Fact]
    public async Task A_negative_adjustment_reduces_on_hand_without_touching_average_cost()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("SKU-1", "Widget", onHand: 10m);
        product.UpdatePricing(regularPrice: 10m, lastCost: 4m, avgCost: 4m);
        await harness.Db.SaveChangesAsync();

        var result = await harness.Inventory.Handle(
            new AdjustStockCommand(product.Id, harness.Location.Id, QuantityDelta: -3m, Reason: "Damaged in stockroom"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.OnHand.Should().Be(7m);

        var refreshed = (await harness.Db.Products.FindAsync(product.Id))!;
        refreshed.AvgCost.Should().Be(4m);

        harness.Db.StockLedgerEntries.Should().ContainSingle(e => e.ProductId == product.Id && e.Reason == "Damaged in stockroom");
    }

    [Fact]
    public async Task A_zero_adjustment_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("SKU-1", "Widget");

        var result = await harness.Inventory.Handle(
            new AdjustStockCommand(product.Id, harness.Location.Id, QuantityDelta: 0m, Reason: "Count correction"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("inventory.adjustment_is_zero");
    }

    [Fact]
    public async Task Breaking_a_case_moves_stock_from_the_parent_into_its_child_units()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var caseProduct = await harness.AddProductAsync("CASE-1", "Case of 12", onHand: 5m);
        caseProduct.UpdateOrdering(baseStock: 0, reorderPoint: 0, reorderQty: 0, caseQty: 12m, shipWeight: 0m);

        var unitProduct = await harness.AddProductAsync("UNIT-1", "Single Unit", onHand: 3m);
        unitProduct.SetLinks(substituteId: null, tagAlongId: null, parentId: caseProduct.Id);

        await harness.Db.SaveChangesAsync();

        var result = await harness.Inventory.Handle(
            new BreakCaseCommand(caseProduct.Id, harness.Location.Id, CasesToBreak: 2m), default);

        result.IsSuccess.Should().BeTrue();

        (await harness.Db.Products.FindAsync(caseProduct.Id))!.OnHand.Should().Be(3m); // 5 - 2
        (await harness.Db.Products.FindAsync(unitProduct.Id))!.OnHand.Should().Be(27m); // 3 + 2*12
    }

    [Fact]
    public async Task Cannot_break_more_cases_than_are_on_hand()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var caseProduct = await harness.AddProductAsync("CASE-1", "Case of 12", onHand: 1m);
        caseProduct.UpdateOrdering(baseStock: 0, reorderPoint: 0, reorderQty: 0, caseQty: 12m, shipWeight: 0m);

        var unitProduct = await harness.AddProductAsync("UNIT-1", "Single Unit");
        unitProduct.SetLinks(null, null, caseProduct.Id);
        await harness.Db.SaveChangesAsync();

        var result = await harness.Inventory.Handle(
            new BreakCaseCommand(caseProduct.Id, harness.Location.Id, CasesToBreak: 2m), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("inventory.insufficient_cases");
    }

    [Fact]
    public async Task Breaking_a_case_with_no_linked_child_product_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var caseProduct = await harness.AddProductAsync("CASE-1", "Case of 12", onHand: 5m);
        caseProduct.UpdateOrdering(baseStock: 0, reorderPoint: 0, reorderQty: 0, caseQty: 12m, shipWeight: 0m);
        await harness.Db.SaveChangesAsync();

        var result = await harness.Inventory.Handle(
            new BreakCaseCommand(caseProduct.Id, harness.Location.Id, CasesToBreak: 1m), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("inventory.no_child_product");
    }
}
