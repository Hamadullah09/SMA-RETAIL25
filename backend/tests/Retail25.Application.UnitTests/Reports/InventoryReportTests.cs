using FluentAssertions;
using Retail25.Application.Reports;
using Retail25.Application.UnitTests.Masters;
using Retail25.Domain.Inventory;
using Retail25.Domain.Purchasing;
using Xunit;

namespace Retail25.Application.UnitTests.Reports;

/// <summary>
/// Stock valuation, the understock/overstock heuristic, what is on order and what arrived. The
/// boundary cases in the position report are the point: an item exactly at its reorder point is the
/// one a buyer argues about.
/// </summary>
public sealed class InventoryReportTests
{
    [Fact]
    public async Task Valuation_totals_cost_and_retail_per_department()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var tools = await harness.AddDepartmentAsync("Tools");

        var hammer = await harness.AddProductAsync("H-1", "Hammer", price: 25m, onHand: 4m, departmentId: tools.Id);
        hammer.UpdatePricing(regularPrice: 25m, lastCost: 10m, avgCost: 10m);

        var oddment = await harness.AddProductAsync("X-1", "Oddment", price: 5m, onHand: 2m);
        oddment.UpdatePricing(regularPrice: 5m, lastCost: 1m, avgCost: 1m);

        await harness.Db.SaveChangesAsync();

        var handlers = new InventoryReportHandlers(harness.Db, harness.Clock);
        var result = await handlers.Handle(new GetStockValuationQuery(harness.Location.Id), CancellationToken.None);

        var toolsRow = result.Rows.Single(r => r.DepartmentName == "Tools");
        toolsRow.UnitsOnHand.Should().Be(4m);
        toolsRow.CostValue.Should().Be(40m);
        toolsRow.RetailValue.Should().Be(100m);
        toolsRow.PotentialMargin.Should().Be(60m);

        result.TotalCostValue.Should().Be(42m);
        result.TotalRetailValue.Should().Be(110m);
    }

    /// <summary>
    /// At the reorder point, not merely below it — the purchase-order generator uses the same
    /// boundary, and a report that disagreed would have a buyer ordering things the system says
    /// are fine.
    /// </summary>
    [Fact]
    public async Task An_item_sitting_exactly_on_its_reorder_point_counts_as_understocked()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("W-1", "Widget", onHand: 5m);
        product.UpdateOrdering(baseStock: 20, reorderPoint: 5, reorderQty: 10, caseQty: 0m, shipWeight: 0m);
        await harness.Db.SaveChangesAsync();

        var handlers = new InventoryReportHandlers(harness.Db, harness.Clock);
        var rows = await handlers.Handle(new GetStockPositionQuery(harness.Location.Id), CancellationToken.None);

        rows.Should().ContainSingle();
        rows[0].Position.Should().Be(StockPosition.Understock);
    }

    /// <summary>
    /// The legacy heuristic: on hand plus on order against base stock plus three weeks of demand.
    /// Twelve sold over the window is four a week, so twelve weeks of cover on a base of ten is
    /// unambiguously too much stock.
    /// </summary>
    [Fact]
    public async Task Overstock_uses_three_weeks_of_sales_against_base_stock_and_what_is_on_order()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("W-1", "Widget", onHand: 48m);
        product.UpdateOrdering(baseStock: 10, reorderPoint: 2, reorderQty: 10, caseQty: 0m, shipWeight: 0m);
        await harness.Db.SaveChangesAsync();

        // Sales are written negative in the ledger, the way CompleteSaleCommand writes them.
        await harness.AddStockMovementAsync(product, MovementType.Sale, -12m, occurredAt: harness.Clock.Now.AddDays(-7));

        var handlers = new InventoryReportHandlers(harness.Db, harness.Clock);
        var rows = await handlers.Handle(new GetStockPositionQuery(harness.Location.Id), CancellationToken.None);

        rows.Should().ContainSingle();
        rows[0].Position.Should().Be(StockPosition.Overstock);
        rows[0].AvgWeeklySales.Should().Be(4m);
        rows[0].WeeksOfSupply.Should().Be(12m);
    }

    [Fact]
    public async Task A_healthy_item_is_left_out_unless_it_is_asked_for()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("W-1", "Widget", onHand: 12m);
        product.UpdateOrdering(baseStock: 20, reorderPoint: 5, reorderQty: 10, caseQty: 0m, shipWeight: 0m);
        await harness.Db.SaveChangesAsync();

        await harness.AddStockMovementAsync(product, MovementType.Sale, -9m, occurredAt: harness.Clock.Now.AddDays(-3));

        var handlers = new InventoryReportHandlers(harness.Db, harness.Clock);

        var flaggedOnly = await handlers.Handle(new GetStockPositionQuery(harness.Location.Id), CancellationToken.None);
        flaggedOnly.Should().BeEmpty("an item that is neither short nor drowning is not news");

        var everything = await handlers.Handle(
            new GetStockPositionQuery(harness.Location.Id, Only: StockPosition.Normal),
            CancellationToken.None);

        everything.Should().ContainSingle();
        everything[0].Position.Should().Be(StockPosition.Normal);
    }

    [Fact]
    public async Task On_order_shows_what_is_outstanding_and_drops_fully_received_lines()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var supplier = await harness.AddSupplierAsync("Acme Supply", "1");
        var open = await harness.AddProductAsync("W-1", "Widget");
        var landed = await harness.AddProductAsync("G-1", "Gadget");

        var order = new PurchaseOrder
        {
            PoNumber = 1,
            LocationId = harness.Location.Id,
            SupplierId = supplier.Id,
            Status = PurchaseOrderStatus.PartiallyReceived,
            PostedOn = new DateOnly(2026, 7, 1),
            DueOn = new DateOnly(2026, 7, 20),
        };
        harness.Db.PurchaseOrders.Add(order);

        harness.Db.PurchaseOrderLines.Add(new PurchaseOrderLine
        {
            PurchaseOrderId = order.Id,
            ProductId = open.Id,
            OrderQty = 10m,
            QtyReceived = 4m,
            CostEach = 5m,
        });

        harness.Db.PurchaseOrderLines.Add(new PurchaseOrderLine
        {
            PurchaseOrderId = order.Id,
            ProductId = landed.Id,
            OrderQty = 3m,
            QtyReceived = 3m,
            CostEach = 8m,
        });

        await harness.Db.SaveChangesAsync();

        var handlers = new InventoryReportHandlers(harness.Db, harness.Clock);
        var rows = await handlers.Handle(new GetOnOrderQuery(harness.Location.Id), CancellationToken.None);

        rows.Should().ContainSingle();
        rows[0].StockCode.Should().Be("W-1");
        rows[0].QtyOutstanding.Should().Be(6m);
        rows[0].ExpectedValue.Should().Be(30m);
        rows[0].SupplierName.Should().Be("Acme Supply");
        rows[0].PoNumber.Should().Be(1);
    }

    /// <summary>
    /// Regression: the window was built with <c>DateOnly.ToDateTime</c>, which yields an
    /// unspecified-kind DateTime that picks up the *server's* local offset on its way to a
    /// DateTimeOffset. Npgsql refuses any non-UTC offset for a timestamptz column, so the report
    /// threw against real Postgres while passing against the in-memory provider, which does not
    /// enforce it. Asserting the offset directly is the only version of this test that would have
    /// failed before the fix.
    /// </summary>
    [Fact]
    public void A_date_window_is_anchored_to_utc_so_postgres_will_accept_it()
    {
        var (from, to) = InventoryReportHandlers.DayRangeUtc(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        from.Offset.Should().Be(TimeSpan.Zero);
        to.Offset.Should().Be(TimeSpan.Zero);
        from.UtcDateTime.Should().Be(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
        to.UtcDateTime.Date.Should().Be(new DateTime(2026, 7, 31));
    }

    [Fact]
    public async Task Stock_received_reads_the_ledger_and_pages()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var supplier = await harness.AddSupplierAsync("Acme Supply", "1");
        var product = await harness.AddProductAsync("W-1", "Widget");

        var order = new PurchaseOrder
        {
            PoNumber = 7,
            LocationId = harness.Location.Id,
            SupplierId = supplier.Id,
            Status = PurchaseOrderStatus.Received,
        };
        harness.Db.PurchaseOrders.Add(order);

        var receipt = new PurchaseOrderReceipt
        {
            PurchaseOrderId = order.Id,
            ReceivedOn = new DateOnly(2026, 7, 10),
            FreightTotal = 15m,
            StaffId = Guid.NewGuid(),
        };
        harness.Db.PurchaseOrderReceipts.Add(receipt);
        await harness.Db.SaveChangesAsync();

        await harness.AddStockMovementAsync(
            product, MovementType.Receipt, 10m, unitCost: 5m,
            occurredAt: new DateTimeOffset(2026, 7, 10, 9, 0, 0, TimeSpan.Zero),
            referenceId: receipt.Id, referenceType: nameof(PurchaseOrderReceipt));

        // A movement that is not a receipt must not show up in a receiving report.
        await harness.AddStockMovementAsync(product, MovementType.Adjustment, 3m, unitCost: 5m);

        var handlers = new InventoryReportHandlers(harness.Db, harness.Clock);

        var page = await handlers.Handle(
            new GetStockReceivedQuery(harness.Location.Id, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)),
            CancellationToken.None);

        page.TotalCount.Should().Be(1);
        page.TotalCost.Should().Be(50m);
        page.Rows[0].QtyReceived.Should().Be(10m);
        page.Rows[0].UnitCost.Should().Be(5m);
        page.Rows[0].ExtendedCost.Should().Be(50m);
        page.Rows[0].PoNumber.Should().Be(7);
        page.Rows[0].SupplierName.Should().Be("Acme Supply");
        page.Rows[0].ReceiptFreightTotal.Should().Be(15m);
    }
}
