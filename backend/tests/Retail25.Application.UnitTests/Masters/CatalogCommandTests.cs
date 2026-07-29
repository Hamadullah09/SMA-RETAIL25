using FluentAssertions;
using Xunit;
using NSubstitute;
using Retail25.Application.Abstractions;
using Retail25.Application.Catalog;
using Retail25.Application.Common;
using Retail25.Domain.Catalog;

namespace Retail25.Application.UnitTests.Masters;

public sealed class CatalogCommandTests
{
    [Fact]
    public async Task Saving_one_tab_leaves_the_others_alone()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("SKU1", "Widget");

        await harness.Products.Handle(
            new UpdateProductCommand(product.Id, Ordering: new ProductOrderingSection(5, 10, 20, 12m, 1.5m, [])),
            default);

        await harness.Products.Handle(
            new UpdateProductCommand(product.Id, Messages: new ProductMessagesSection("Ask for ID", "Thanks!", "A note")),
            default);

        var form = await harness.Browse.Handle(new GetProductFormQuery(product.Id), default);

        // The ordering tab was never resent; its values must survive the messages save.
        form.Value.ReorderPoint.Should().Be(10);
        form.Value.CaseQty.Should().Be(12m);
        form.Value.PosMessage.Should().Be("Ask for ID");
        form.Value.Notes.Should().Be("A note");
    }

    [Fact]
    public async Task A_duplicate_stock_code_is_refused_on_create_and_on_rename()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("TAKEN", "First");
        var second = await harness.AddProductAsync("FREE", "Second");

        var created = await harness.Products.Handle(
            new CreateProductCommand(
                harness.Location.Id,
                new ProductGeneralSection("taken", "Another", null, ProductType.Standard, null, null, null, null),
                5m),
            default);

        created.Error.Code.Should().Be("product.duplicate_stock_code");

        var renamed = await harness.Products.Handle(
            new UpdateProductCommand(
                second.Id,
                General: new ProductGeneralSection("TAKEN", "Second", null, ProductType.Standard, null, null, null, null)),
            default);

        renamed.Error.Code.Should().Be("product.duplicate_stock_code");
    }

    [Fact]
    public async Task An_item_that_still_has_stock_cannot_be_deleted()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("SKU1", "Widget", onHand: 40m);

        var result = await harness.Products.Handle(new DeleteProductCommand(product.Id), default);

        result.Error.Code.Should().Be("product.still_in_stock");
        result.Error.Arguments!["onHand"].Should().Be(40m);
    }

    [Fact]
    public async Task Deleting_an_item_clears_the_links_that_pointed_at_it()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var target = await harness.AddProductAsync("GONE", "Discontinued");
        var referrer = await harness.AddProductAsync("KEEP", "Still sold");

        referrer.SetLinks(target.Id, null, null);
        await harness.Db.SaveChangesAsync();

        (await harness.Products.Handle(new DeleteProductCommand(target.Id), default)).IsSuccess.Should().BeTrue();

        // A substitute that resolves to a deleted item is a dead end the cashier finds with a
        // customer at the counter.
        var form = await harness.Browse.Handle(new GetProductFormQuery(referrer.Id), default);
        form.Value.Substitute.Should().BeNull();
    }

    [Fact]
    public async Task An_item_restored_onto_a_reused_stock_code_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var original = await harness.AddProductAsync("SKU1", "First widget");

        (await harness.Products.Handle(new DeleteProductCommand(original.Id), default)).IsSuccess.Should().BeTrue();

        // The code was freed and someone used it again.
        await harness.AddProductAsync("SKU1", "Replacement widget");

        var restored = await harness.Products.Handle(new RestoreProductCommand(original.Id), default);

        restored.Error.Code.Should().Be("product.duplicate_stock_code");
    }

    [Fact]
    public async Task Restoring_brings_the_item_back_into_the_browse()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("SKU1", "Widget");

        await harness.Products.Handle(new DeleteProductCommand(product.Id), default);
        (await harness.Products.Handle(new RestoreProductCommand(product.Id), default)).IsSuccess.Should().BeTrue();

        var page = await harness.Browse.Handle(new BrowseProductsQuery(harness.Location.Id), default);
        page.Items.Should().ContainSingle().Which.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task A_clone_copies_the_description_but_not_the_barcode_or_the_stock()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var source = await harness.AddProductAsync("ORIG", "Blue shirt", price: 29.99m, onHand: 12m);
        source.UpdateDetails("Blue shirt", "Cotton, long sleeve", "0641234567890", "A4", "Reorder in spring");
        await harness.Db.SaveChangesAsync();

        await harness.Products.Handle(
            new UpdateProductCommand(
                source.Id,
                Pricing: new ProductPricingSection(29.99m, 12m, [new ProductPriceDto(2, 24.99m)], [], null, null)),
            default);

        var clone = await harness.Products.Handle(new CloneProductCommand(source.Id, "COPY", "Red shirt"), default);

        clone.IsSuccess.Should().BeTrue();
        clone.Value.Name.Should().Be("Red shirt");
        clone.Value.Description.Should().Be("Cotton, long sleeve");
        clone.Value.RegularPrice.Should().Be(29.99m);
        clone.Value.Levels.Should().ContainSingle().Which.Price.Should().Be(24.99m);

        // Two items sharing one UPC would make every scan ambiguous, and a clone that arrived with
        // twelve in stock would be an inventory adjustment nobody made.
        clone.Value.Upc.Should().BeNull();
        clone.Value.OnHand.Should().Be(0m);
    }

    [Fact]
    public async Task An_impossible_bonus_rule_is_reported_rather_than_dropped()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("SKU1", "Widget");

        var result = await harness.Products.Handle(
            new UpdateProductCommand(
                product.Id,
                // Three free for every two bought would make every item free.
                Pricing: new ProductPricingSection(10m, 4m, [], [], null, new BonusPricingDto(2m, 3m))),
            default);

        // Answering "Saved" and quietly discarding the rule would leave the item priced by something
        // the user believes they just created.
        result.Error.Code.Should().Be("bonus.free_exceeds_buy");
    }

    [Fact]
    public async Task Break_points_and_a_sale_window_round_trip_through_the_form()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("SKU1", "Widget");

        var saved = await harness.Products.Handle(
            new UpdateProductCommand(
                product.Id,
                Pricing: new ProductPricingSection(
                    10m,
                    4m,
                    [new ProductPriceDto(2, 8m)],
                    [new PriceBreakDto(2, 12m)],
                    new SalePricingDto(15m, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)),
                    new BonusPricingDto(3m, 1m))),
            default);

        saved.IsSuccess.Should().BeTrue();

        var form = await harness.Browse.Handle(new GetProductFormQuery(product.Id), default);

        form.Value.Breaks.Should().ContainSingle().Which.MinQuantity.Should().Be(12m);
        form.Value.Sale!.DiscountPct.Should().Be(15m);
        form.Value.Bonus!.FreeQty.Should().Be(1m);
    }

    [Fact]
    public async Task An_item_cannot_be_its_own_substitute()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("SKU1", "Widget");

        var result = await harness.Products.Handle(
            new UpdateProductCommand(product.Id, Links: new ProductLinksSection(product.Id, null, null)),
            default);

        result.Error.Code.Should().Be("product.link_to_self");
    }

    [Fact]
    public async Task A_gift_card_stays_untaxed_however_the_form_is_saved()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("GC25", "Gift card", type: ProductType.GiftCard);

        await harness.Products.Handle(
            new UpdateProductCommand(product.Id, Tax: new ProductTaxSection(true, true)),
            default);

        var form = await harness.Browse.Handle(new GetProductFormQuery(product.Id), default);

        // The tax is charged when the card is spent. Charging it twice is a refundable error the
        // store only discovers at reconciliation.
        form.Value.Tax1Applies.Should().BeFalse();
        form.Value.Tax2Applies.Should().BeFalse();
    }

    [Fact]
    public async Task Every_save_and_delete_patches_the_open_grids()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("SKU1", "Widget");

        await harness.Products.Handle(
            new UpdateProductCommand(product.Id, Messages: new ProductMessagesSection("Hi", null, null)),
            default);

        await harness.Notifier.Received().RowChangedAsync(
            harness.Location.Id, GridKeys.Product, product.Id, Arg.Any<object>(), Arg.Any<CancellationToken>());

        await harness.Products.Handle(new DeleteProductCommand(product.Id), default);

        await harness.Notifier.Received().RowRemovedAsync(
            harness.Location.Id, GridKeys.Product, product.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_department_with_items_assigned_cannot_be_deleted()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var department = await harness.AddDepartmentAsync("Hardware");
        await harness.AddProductAsync("SKU1", "Hammer", departmentId: department.Id);

        var result = await harness.Reference.Handle(new DeleteDepartmentCommand(department.Id), default);

        result.Error.Code.Should().Be("reference.still_in_use");
    }

    [Fact]
    public async Task The_department_list_reports_how_many_items_each_one_holds()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var department = await harness.AddDepartmentAsync("Hardware");

        await harness.AddProductAsync("SKU1", "Hammer", departmentId: department.Id);
        await harness.AddProductAsync("SKU2", "Nails", departmentId: department.Id);
        await harness.AddProductAsync("SKU3", "Unfiled");

        var rows = await harness.Reference.Handle(new ListDepartmentsQuery(harness.Location.Id), default);

        rows.Should().ContainSingle().Which.UsageCount.Should().Be(2);
    }
}
