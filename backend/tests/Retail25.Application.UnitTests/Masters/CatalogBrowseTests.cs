using FluentAssertions;
using Xunit;
using Retail25.Application.Catalog;

namespace Retail25.Application.UnitTests.Masters;

/// <summary>
/// The browse grid's contract: every row exactly once, in the order asked for, no matter how the
/// user pages through it. This is the property the legacy offset-paged browse could not hold — an
/// item inserted mid-scroll shifted every later page.
/// </summary>
public sealed class CatalogBrowseTests
{
    [Fact]
    public async Task Paging_through_the_catalogue_returns_every_item_exactly_once()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        for (var i = 1; i <= 47; i++)
        {
            await harness.AddProductAsync($"SKU{i:D3}", $"Item {i}");
        }

        var seen = new List<string>();
        string? cursor = null;

        do
        {
            var page = await harness.Browse.Handle(
                new BrowseProductsQuery(harness.Location.Id, Cursor: cursor, PageSize: 10),
                default);

            seen.AddRange(page.Items.Select(p => p.StockCode));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        seen.Should().HaveCount(47);
        seen.Should().OnlyHaveUniqueItems();
        seen.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Paging_by_a_column_with_duplicates_still_shows_every_row_once()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        // Twelve items at the same price. Without the stock-code tie-break this is exactly where a
        // keyset cursor loses rows: the price alone is not a total order.
        for (var i = 1; i <= 12; i++)
        {
            await harness.AddProductAsync($"SKU{i:D3}", $"Item {i}", price: 9.99m);
        }

        var seen = new List<string>();
        string? cursor = null;

        do
        {
            var page = await harness.Browse.Handle(
                new BrowseProductsQuery(harness.Location.Id, Sort: ProductSort.RegularPrice, Cursor: cursor, PageSize: 5),
                default);

            seen.AddRange(page.Items.Select(p => p.StockCode));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        seen.Should().HaveCount(12);
        seen.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task A_descending_page_reads_back_in_the_reverse_order()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        await harness.AddProductAsync("AAA", "Apple", price: 1m);
        await harness.AddProductAsync("BBB", "Banana", price: 3m);
        await harness.AddProductAsync("CCC", "Cherry", price: 2m);

        var page = await harness.Browse.Handle(
            new BrowseProductsQuery(harness.Location.Id, Sort: ProductSort.RegularPrice, Descending: true),
            default);

        page.Items.Select(p => p.StockCode).Should().ContainInOrder("BBB", "CCC", "AAA");
    }

    [Fact]
    public async Task A_mangled_cursor_starts_from_the_beginning_rather_than_failing()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("AAA", "Apple");

        var page = await harness.Browse.Handle(
            new BrowseProductsQuery(harness.Location.Id, Cursor: "not-base64-!!"),
            default);

        page.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Deleted_items_are_hidden_by_default_and_listed_on_demand()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var kept = await harness.AddProductAsync("KEEP", "Kept item");
        var gone = await harness.AddProductAsync("GONE", "Deleted item");

        (await harness.Products.Handle(new DeleteProductCommand(gone.Id), default)).IsSuccess.Should().BeTrue();

        var live = await harness.Browse.Handle(new BrowseProductsQuery(harness.Location.Id), default);
        live.Items.Select(p => p.Id).Should().ContainSingle().Which.Should().Be(kept.Id);

        var deleted = await harness.Browse.Handle(new BrowseProductsQuery(harness.Location.Id, DeletedOnly: true), default);
        deleted.Items.Select(p => p.Id).Should().ContainSingle().Which.Should().Be(gone.Id);
    }

    [Fact]
    public async Task The_reorder_filter_finds_what_needs_buying()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var low = await harness.AddProductAsync("LOW", "Running out", onHand: 2m);
        low.UpdateOrdering(0, 10, 20, 1m, 0m);

        var fine = await harness.AddProductAsync("FINE", "Plenty", onHand: 90m);
        fine.UpdateOrdering(0, 10, 20, 1m, 0m);

        await harness.Db.SaveChangesAsync();

        var page = await harness.Browse.Handle(
            new BrowseProductsQuery(harness.Location.Id, BelowReorderPoint: true),
            default);

        page.Items.Select(p => p.StockCode).Should().ContainSingle().Which.Should().Be("LOW");
    }

    [Fact]
    public async Task Search_matches_a_stock_code_a_name_or_a_barcode()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var product = await harness.AddProductAsync("WIDGET1", "Blue widget");
        product.UpdateDetails("Blue widget", null, "0641234567890", null, null);
        await harness.Db.SaveChangesAsync();

        await harness.AddProductAsync("OTHER1", "Something else");

        foreach (var term in new[] { "WIDGET", "Blue", "064123456" })
        {
            var page = await harness.Browse.Handle(new BrowseProductsQuery(harness.Location.Id, Search: term), default);
            page.Items.Select(p => p.StockCode).Should().ContainSingle().Which.Should().Be("WIDGET1");
        }
    }
}
