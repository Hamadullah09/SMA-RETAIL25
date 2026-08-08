using FluentAssertions;
using Retail25.Application.Purchasing;
using Retail25.Application.UnitTests.Masters;
using Retail25.Domain.Purchasing;
using Xunit;

namespace Retail25.Application.UnitTests.Purchasing;

/// <summary>
/// The PO lifecycle (guide p.63–71): generate from a quantity strategy, edit while Draft, post
/// (reserving <c>OnOrder</c>), receive with freight (rolling the moving-average cost), and cancel.
/// <para>
/// These are the highest-risk parts of Stage 1 — a wrong quantity-strategy formula either starves a
/// shelf or ties up cash in stock nobody asked for, and a wrong freight allocation quietly misstates
/// margin on every item in the shipment, not just the ones actually over- or under-priced.
/// </para>
/// </summary>
public sealed class PurchaseOrderTests
{
    [Fact]
    public async Task Blank_strategy_creates_an_empty_draft_with_no_lines()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var supplier = await harness.AddSupplierAsync("Acme Supply", "S-1");

        var result = await harness.PurchaseOrders.Handle(
            new GeneratePurchaseOrderCommand(harness.Location.Id, supplier.Id, OrderQuantityStrategy.Blank), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(PurchaseOrderStatus.Draft);
        result.Value.Lines.Should().BeEmpty();
        result.Value.Total.Should().Be(0m);
    }

    [Fact]
    public async Task Reorder_point_fixed_orders_the_fixed_quantity_only_at_or_below_the_point()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var supplier = await harness.AddSupplierAsync("Acme Supply", "S-1");

        var low = await harness.AddProductAsync("SKU-LOW", "Low Stock Widget", onHand: 2m);
        low.UpdateOrdering(baseStock: 50, reorderPoint: 5, reorderQty: 20, caseQty: 0m, shipWeight: 0m);

        var high = await harness.AddProductAsync("SKU-HIGH", "Well Stocked Widget", onHand: 100m);
        high.UpdateOrdering(baseStock: 50, reorderPoint: 5, reorderQty: 20, caseQty: 0m, shipWeight: 0m);

        await harness.Db.SaveChangesAsync();
        await harness.AddProductSupplierAsync(low, supplier, rank: 1, cost: 3.50m);
        await harness.AddProductSupplierAsync(high, supplier, rank: 1, cost: 3.50m);

        var result = await harness.PurchaseOrders.Handle(
            new GeneratePurchaseOrderCommand(harness.Location.Id, supplier.Id, OrderQuantityStrategy.ReorderPointFixed), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Lines.Should().ContainSingle(l => l.ProductId == low.Id && l.OrderQty == 20m);
        result.Value.Lines.Should().NotContain(l => l.ProductId == high.Id);
        result.Value.Total.Should().Be(70m); // 20 * 3.50
    }

    [Fact]
    public async Task Reorder_point_to_base_tops_up_to_base_stock_rather_than_a_fixed_amount()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var supplier = await harness.AddSupplierAsync("Acme Supply", "S-1");

        var product = await harness.AddProductAsync("SKU-1", "Widget", onHand: 10m);
        product.UpdateOrdering(baseStock: 40, reorderPoint: 15, reorderQty: 999, caseQty: 0m, shipWeight: 0m);
        await harness.Db.SaveChangesAsync();
        await harness.AddProductSupplierAsync(product, supplier, rank: 1, cost: 2m);

        var result = await harness.PurchaseOrders.Handle(
            new GeneratePurchaseOrderCommand(harness.Location.Id, supplier.Id, OrderQuantityStrategy.ReorderPointToBase), default);

        // onHand(10) + onOrder(0) = 10 <= reorderPoint(15) -> order up to base(40): 40 - 10 = 30.
        result.Value.Lines.Should().ContainSingle(l => l.OrderQty == 30m);
    }

    [Fact]
    public async Task A_supplier_ranked_second_for_a_product_is_not_included_in_that_products_generation()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var preferred = await harness.AddSupplierAsync("Preferred Co", "S-1");
        var backup = await harness.AddSupplierAsync("Backup Co", "S-2");

        var product = await harness.AddProductAsync("SKU-1", "Widget", onHand: 0m);
        product.UpdateOrdering(baseStock: 10, reorderPoint: 5, reorderQty: 10, caseQty: 0m, shipWeight: 0m);
        await harness.Db.SaveChangesAsync();
        await harness.AddProductSupplierAsync(product, preferred, rank: 1, cost: 2m);
        await harness.AddProductSupplierAsync(product, backup, rank: 2, cost: 2.25m);

        var result = await harness.PurchaseOrders.Handle(
            new GeneratePurchaseOrderCommand(harness.Location.Id, backup.Id, OrderQuantityStrategy.ReorderPointFixed), default);

        // Rank 2 for this product is not the preferred source, so generating for the backup supplier
        // yields nothing to order — the preferred supplier's PO is where this product belongs.
        result.Value.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task Posting_reserves_the_ordered_quantity_on_each_products_on_order()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var supplier = await harness.AddSupplierAsync("Acme Supply", "S-1");
        var product = await harness.AddProductAsync("SKU-1", "Widget", onHand: 0m);

        var generated = await harness.PurchaseOrders.Handle(
            new GeneratePurchaseOrderCommand(harness.Location.Id, supplier.Id, OrderQuantityStrategy.Blank), default);
        var poId = generated.Value.Id;

        await harness.PurchaseOrders.Handle(
            new AddPurchaseOrderLineCommand(poId, product.Id, OrderQty: 12m, CostEach: 5m, CaseQty: 0m), default);

        var posted = await harness.PurchaseOrders.Handle(new PostPurchaseOrderCommand(poId), default);

        posted.IsSuccess.Should().BeTrue();
        posted.Value.Status.Should().Be(PurchaseOrderStatus.Posted);
        posted.Value.Total.Should().Be(60m);
        posted.Value.PostedOn.Should().Be(harness.Today);
        posted.Value.DueOn.Should().Be(harness.Today.AddDays(30));

        (await harness.Db.Products.FindAsync(product.Id))!.OnOrder.Should().Be(12m);
    }

    [Fact]
    public async Task Lines_cannot_be_edited_once_the_order_is_posted()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var supplier = await harness.AddSupplierAsync("Acme Supply", "S-1");
        var product = await harness.AddProductAsync("SKU-1", "Widget");

        var generated = await harness.PurchaseOrders.Handle(
            new GeneratePurchaseOrderCommand(harness.Location.Id, supplier.Id, OrderQuantityStrategy.Blank), default);
        await harness.PurchaseOrders.Handle(
            new AddPurchaseOrderLineCommand(generated.Value.Id, product.Id, 5m, 1m, 0m), default);
        await harness.PurchaseOrders.Handle(new PostPurchaseOrderCommand(generated.Value.Id), default);

        var result = await harness.PurchaseOrders.Handle(
            new AddPurchaseOrderLineCommand(generated.Value.Id, product.Id, 1m, 1m, 0m), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("purchase_order.not_draft");
    }

    [Fact]
    public async Task Receiving_allocates_freight_pro_rata_and_rolls_the_moving_average_cost()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var supplier = await harness.AddSupplierAsync("Acme Supply", "S-1");

        // Starts with 10 on hand at $4 avg cost.
        var product = await harness.AddProductAsync("SKU-1", "Widget", onHand: 10m);
        product.UpdatePricing(regularPrice: 10m, lastCost: 4m, avgCost: 4m);
        await harness.Db.SaveChangesAsync();

        var generated = await harness.PurchaseOrders.Handle(
            new GeneratePurchaseOrderCommand(harness.Location.Id, supplier.Id, OrderQuantityStrategy.Blank), default);
        var lineResult = await harness.PurchaseOrders.Handle(
            new AddPurchaseOrderLineCommand(generated.Value.Id, product.Id, OrderQty: 10m, CostEach: 5m, CaseQty: 0m), default);
        await harness.PurchaseOrders.Handle(new PostPurchaseOrderCommand(generated.Value.Id), default);

        var lineId = lineResult.Value.Lines.Single().Id;

        var received = await harness.PurchaseOrders.Handle(
            new ReceivePurchaseOrderCommand(
                generated.Value.Id,
                harness.Today,
                FreightTotal: 10m,
                Lines: [new ReceivePurchaseOrderLine(lineId, QtyReceived: 10m)]),
            default);

        received.IsSuccess.Should().BeTrue();
        received.Value.Status.Should().Be(PurchaseOrderStatus.Received);
        received.Value.Lines.Single().QtyReceived.Should().Be(10m);

        // newAvg = (10*4 + 10*5 + 10) / (10+10) = 100/20 = 5.00
        var refreshed = (await harness.Db.Products.FindAsync(product.Id))!;
        refreshed.AvgCost.Should().Be(5.00m);
        refreshed.OnHand.Should().Be(20m);
        refreshed.OnOrder.Should().Be(0m);
        refreshed.LastCost.Should().Be(5m);
    }

    [Fact]
    public async Task A_partial_receipt_leaves_the_order_partially_received_and_the_remainder_on_order()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var supplier = await harness.AddSupplierAsync("Acme Supply", "S-1");
        var product = await harness.AddProductAsync("SKU-1", "Widget");

        var generated = await harness.PurchaseOrders.Handle(
            new GeneratePurchaseOrderCommand(harness.Location.Id, supplier.Id, OrderQuantityStrategy.Blank), default);
        var lineResult = await harness.PurchaseOrders.Handle(
            new AddPurchaseOrderLineCommand(generated.Value.Id, product.Id, OrderQty: 10m, CostEach: 2m, CaseQty: 0m), default);
        await harness.PurchaseOrders.Handle(new PostPurchaseOrderCommand(generated.Value.Id), default);

        var lineId = lineResult.Value.Lines.Single().Id;

        var received = await harness.PurchaseOrders.Handle(
            new ReceivePurchaseOrderCommand(
                generated.Value.Id, harness.Today, FreightTotal: 0m, Lines: [new ReceivePurchaseOrderLine(lineId, 4m)]),
            default);

        received.Value.Status.Should().Be(PurchaseOrderStatus.PartiallyReceived);
        (await harness.Db.Products.FindAsync(product.Id))!.OnOrder.Should().Be(6m);
    }

    [Fact]
    public async Task Receiving_more_than_remains_on_a_line_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var supplier = await harness.AddSupplierAsync("Acme Supply", "S-1");
        var product = await harness.AddProductAsync("SKU-1", "Widget");

        var generated = await harness.PurchaseOrders.Handle(
            new GeneratePurchaseOrderCommand(harness.Location.Id, supplier.Id, OrderQuantityStrategy.Blank), default);
        var lineResult = await harness.PurchaseOrders.Handle(
            new AddPurchaseOrderLineCommand(generated.Value.Id, product.Id, OrderQty: 5m, CostEach: 1m, CaseQty: 0m), default);
        await harness.PurchaseOrders.Handle(new PostPurchaseOrderCommand(generated.Value.Id), default);

        var lineId = lineResult.Value.Lines.Single().Id;

        var result = await harness.PurchaseOrders.Handle(
            new ReceivePurchaseOrderCommand(
                generated.Value.Id, harness.Today, FreightTotal: 0m, Lines: [new ReceivePurchaseOrderLine(lineId, 6m)]),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("purchase_order.receipt_exceeds_order");
    }

    [Fact]
    public async Task Cancelling_a_posted_order_releases_its_reserved_on_order_quantity()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var supplier = await harness.AddSupplierAsync("Acme Supply", "S-1");
        var product = await harness.AddProductAsync("SKU-1", "Widget");

        var generated = await harness.PurchaseOrders.Handle(
            new GeneratePurchaseOrderCommand(harness.Location.Id, supplier.Id, OrderQuantityStrategy.Blank), default);
        await harness.PurchaseOrders.Handle(
            new AddPurchaseOrderLineCommand(generated.Value.Id, product.Id, 8m, 1m, 0m), default);
        await harness.PurchaseOrders.Handle(new PostPurchaseOrderCommand(generated.Value.Id), default);

        (await harness.Db.Products.FindAsync(product.Id))!.OnOrder.Should().Be(8m);

        var cancelled = await harness.PurchaseOrders.Handle(new CancelPurchaseOrderCommand(generated.Value.Id), default);

        cancelled.IsSuccess.Should().BeTrue();
        cancelled.Value.Status.Should().Be(PurchaseOrderStatus.Cancelled);
        (await harness.Db.Products.FindAsync(product.Id))!.OnOrder.Should().Be(0m);
    }

    [Fact]
    public async Task A_purchase_order_that_has_already_received_a_shipment_cannot_be_cancelled()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var supplier = await harness.AddSupplierAsync("Acme Supply", "S-1");
        var product = await harness.AddProductAsync("SKU-1", "Widget");

        var generated = await harness.PurchaseOrders.Handle(
            new GeneratePurchaseOrderCommand(harness.Location.Id, supplier.Id, OrderQuantityStrategy.Blank), default);
        var lineResult = await harness.PurchaseOrders.Handle(
            new AddPurchaseOrderLineCommand(generated.Value.Id, product.Id, 5m, 1m, 0m), default);
        await harness.PurchaseOrders.Handle(new PostPurchaseOrderCommand(generated.Value.Id), default);

        var lineId = lineResult.Value.Lines.Single().Id;
        await harness.PurchaseOrders.Handle(
            new ReceivePurchaseOrderCommand(generated.Value.Id, harness.Today, 0m, [new ReceivePurchaseOrderLine(lineId, 1m)]),
            default);

        var result = await harness.PurchaseOrders.Handle(new CancelPurchaseOrderCommand(generated.Value.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("purchase_order.cannot_cancel_received");
    }
}
