using FluentAssertions;
using Retail25.Application.Reports;
using Retail25.Application.UnitTests.Masters;
using Retail25.Domain.Sales;
using Xunit;

namespace Retail25.Application.UnitTests.Reports;

/// <summary>
/// The one query behind "sales by product/department/client/period", top sellers and the margin
/// report. The arithmetic is what these tests pin down — the grouping is only useful if the numbers
/// inside each group are right.
/// </summary>
public sealed class SalesAnalysisTests
{
    private static readonly DateOnly Day1 = new(2026, 7, 1);
    private static readonly DateOnly Day2 = new(2026, 7, 2);
    private static readonly DateOnly Whole = new(2026, 7, 31);

    [Fact]
    public async Task Sales_group_by_product_and_sum_quantity_revenue_and_cost()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var widget = await harness.AddProductAsync("W-1", "Widget");
        var gadget = await harness.AddProductAsync("G-1", "Gadget");

        await harness.AddSaleAsync(widget, quantity: 2m, unitPrice: 10m, unitCost: 4m, Day1);
        await harness.AddSaleAsync(widget, quantity: 3m, unitPrice: 10m, unitCost: 4m, Day2);
        await harness.AddSaleAsync(gadget, quantity: 1m, unitPrice: 50m, unitCost: 30m, Day1);

        var handlers = new SalesAnalysisHandlers(harness.Db);

        var result = await handlers.Handle(
            new SalesAnalysisQuery(harness.Location.Id, Day1, Whole, SalesAnalysisGroupBy.Product),
            CancellationToken.None);

        result.Rows.Should().HaveCount(2);

        // Gadget leads: 50 net beats the widget's 50... so ordering is by net desc and they tie —
        // assert by lookup rather than position to keep the test about the arithmetic.
        var widgetRow = result.Rows.Single(r => r.GroupLabel.StartsWith("W-1", StringComparison.Ordinal));
        widgetRow.Quantity.Should().Be(5m);
        widgetRow.NetSales.Should().Be(50m);
        widgetRow.Cogs.Should().Be(20m);
        widgetRow.GrossMargin.Should().Be(30m);
        widgetRow.GrossMarginPct.Should().Be(60m);
        widgetRow.TransactionCount.Should().Be(2);

        var gadgetRow = result.Rows.Single(r => r.GroupLabel.StartsWith("G-1", StringComparison.Ordinal));
        gadgetRow.Quantity.Should().Be(1m);
        gadgetRow.NetSales.Should().Be(50m);
        gadgetRow.Cogs.Should().Be(30m);
        gadgetRow.GrossMargin.Should().Be(20m);

        result.GrandNetSales.Should().Be(100m);
        result.GrandCogs.Should().Be(50m);
        result.GrandGrossMargin.Should().Be(50m);
    }

    [Fact]
    public async Task Grouping_by_department_rolls_products_up_and_names_the_unfiled_ones()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var tools = await harness.AddDepartmentAsync("Tools");
        var filed = await harness.AddProductAsync("W-1", "Widget", departmentId: tools.Id);
        var unfiled = await harness.AddProductAsync("X-1", "Oddment");

        await harness.AddSaleAsync(filed, 2m, 10m, 4m, Day1);
        await harness.AddSaleAsync(unfiled, 1m, 7m, 2m, Day1);

        var handlers = new SalesAnalysisHandlers(harness.Db);

        var result = await handlers.Handle(
            new SalesAnalysisQuery(harness.Location.Id, Day1, Whole, SalesAnalysisGroupBy.Department),
            CancellationToken.None);

        result.Rows.Should().HaveCount(2);
        result.Rows.Single(r => r.GroupLabel == "Tools").NetSales.Should().Be(20m);
        result.Rows.Single(r => r.GroupLabel == "(no department)").NetSales.Should().Be(7m);
    }

    [Fact]
    public async Task Grouping_by_day_buckets_by_business_date_and_reads_forwards()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var widget = await harness.AddProductAsync("W-1", "Widget");

        await harness.AddSaleAsync(widget, 1m, 10m, 4m, Day2);
        await harness.AddSaleAsync(widget, 2m, 10m, 4m, Day1);

        var handlers = new SalesAnalysisHandlers(harness.Db);

        var result = await handlers.Handle(
            new SalesAnalysisQuery(harness.Location.Id, Day1, Whole, SalesAnalysisGroupBy.Day),
            CancellationToken.None);

        result.Rows.Select(r => r.GroupKey).Should().ContainInOrder("2026-07-01", "2026-07-02");
        result.Rows[0].NetSales.Should().Be(20m);
        result.Rows[1].NetSales.Should().Be(10m);
    }

    [Fact]
    public async Task Top_sellers_is_the_same_query_sorted_by_quantity_and_capped()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var slow = await harness.AddProductAsync("S-1", "Slow mover");
        var fast = await harness.AddProductAsync("F-1", "Fast mover");

        await harness.AddSaleAsync(slow, 1m, 100m, 50m, Day1);
        await harness.AddSaleAsync(fast, 40m, 1m, 0.4m, Day1);

        var handlers = new SalesAnalysisHandlers(harness.Db);

        var result = await handlers.Handle(
            new SalesAnalysisQuery(
                harness.Location.Id, Day1, Whole, SalesAnalysisGroupBy.Product,
                Top: 1, SortBy: "quantity"),
            CancellationToken.None);

        result.Rows.Should().HaveCount(1);
        result.Rows[0].GroupLabel.Should().StartWith("F-1");
        result.Rows[0].Quantity.Should().Be(40m);
    }

    /// <summary>
    /// The permission boundary is enforced by nulling the fields server-side, not by the browser
    /// choosing not to render them — this is the test that keeps that true.
    /// </summary>
    [Fact]
    public async Task Hiding_cost_removes_cost_and_margin_from_the_response_entirely()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var widget = await harness.AddProductAsync("W-1", "Widget");
        await harness.AddSaleAsync(widget, 2m, 10m, 4m, Day1);

        var handlers = new SalesAnalysisHandlers(harness.Db);

        var hidden = await handlers.Handle(
            new SalesAnalysisQuery(harness.Location.Id, Day1, Whole, HideCost: true),
            CancellationToken.None);

        hidden.Rows[0].Cogs.Should().BeNull();
        hidden.Rows[0].GrossMargin.Should().BeNull();
        hidden.Rows[0].GrossMarginPct.Should().BeNull();
        hidden.GrandCogs.Should().BeNull();
        hidden.Rows[0].NetSales.Should().Be(20m, "revenue is still visible without cost visibility");

        var shown = await handlers.Handle(
            new MarginAnalysisQuery(new SalesAnalysisQuery(harness.Location.Id, Day1, Whole)),
            CancellationToken.None);

        shown.Rows[0].Cogs.Should().Be(8m);
    }

    [Fact]
    public async Task A_voided_sale_is_excluded_by_default_and_included_on_request()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var widget = await harness.AddProductAsync("W-1", "Widget");

        await harness.AddSaleAsync(widget, 2m, 10m, 4m, Day1);
        await harness.AddSaleAsync(widget, 5m, 10m, 4m, Day1, status: TransactionStatus.Voided);

        var handlers = new SalesAnalysisHandlers(harness.Db);

        var excluded = await handlers.Handle(
            new SalesAnalysisQuery(harness.Location.Id, Day1, Whole),
            CancellationToken.None);

        excluded.GrandNetSales.Should().Be(20m);

        var included = await handlers.Handle(
            new SalesAnalysisQuery(harness.Location.Id, Day1, Whole, IncludeVoided: true),
            CancellationToken.None);

        included.GrandNetSales.Should().Be(70m);
    }

    /// <summary>
    /// A trainee practising on a live till must never move the numbers the shop is run on.
    /// </summary>
    [Fact]
    public async Task A_training_sale_never_appears_in_the_analysis()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var widget = await harness.AddProductAsync("W-1", "Widget");

        await harness.AddSaleAsync(widget, 2m, 10m, 4m, Day1);
        await harness.AddSaleAsync(widget, 99m, 10m, 4m, Day1, isTraining: true);

        var handlers = new SalesAnalysisHandlers(harness.Db);

        var result = await handlers.Handle(
            new SalesAnalysisQuery(harness.Location.Id, Day1, Whole, IncludeVoided: true),
            CancellationToken.None);

        result.GrandQuantity.Should().Be(2m);
        result.GrandNetSales.Should().Be(20m);
    }

    [Fact]
    public async Task The_export_writes_a_row_per_group_and_omits_cost_when_it_is_hidden()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var widget = await harness.AddProductAsync("W-1", "Widget, large");
        await harness.AddSaleAsync(widget, 2m, 10m, 4m, Day1);

        var handlers = new SalesAnalysisHandlers(harness.Db);

        var csv = await handlers.Handle(
            new ExportSalesAnalysisQuery(new SalesAnalysisQuery(harness.Location.Id, Day1, Whole, HideCost: true)),
            CancellationToken.None);

        csv.Should().Contain("Group,Quantity,NetSales,Discount,Tax,Transactions");
        csv.Should().NotContain("Cogs");
        // The comma in the product name must not split the row.
        csv.Should().Contain("\"W-1 — Widget, large\"");
    }
}
