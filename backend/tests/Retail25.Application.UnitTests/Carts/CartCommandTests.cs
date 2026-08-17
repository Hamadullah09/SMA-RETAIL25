using FluentAssertions;
using Retail25.Application.Abstractions;
using Retail25.Application.Carts.Commands;
using Retail25.Application.Carts.Services;
using Retail25.Application.Common;
using Retail25.Domain.Sales;
using Xunit;

namespace Retail25.Application.UnitTests.Carts;

/// <summary>
/// Cart handlers end to end against an in-memory store and database.
/// </summary>
public sealed class CartCommandTests
{
    private const string Epc = "3034257BF400B7800004CB2F";

    /// <summary>
    /// A cart has to be addressable, and nothing had been giving it an identity.
    /// <para>
    /// <c>Cart.Open</c> left <c>Id</c> at the language default, and no cart store assigned one
    /// either, so every cart ever created was id 0. The store keyed them all on that one value, and
    /// the till then posted its lines to <c>/carts/0/lines</c> — which is why selecting an item at
    /// the counter appeared to do nothing at all, and why the sales table was still empty after a
    /// full day of testing. Two carts are opened here rather than one because a single cart would
    /// pass this test with a hard-coded constant.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Every_cart_is_opened_with_an_identity_of_its_own()
    {
        using var harness = await PosTestHarness.CreateAsync();

        var first = await harness.OpenCartAsync();
        var second = await harness.OpenCartAsync();

        first.Id.Should().NotBe(0, "a cart addressed as 0 is a cart the till cannot post lines to");
        second.Id.Should().NotBe(0);
        second.Id.Should().NotBe(first.Id, "two baskets keyed the same would overwrite each other in the store");
    }

    /// <summary>
    /// A station holding an unaddressable cart must not be stuck with it.
    /// <para>
    /// Opening a cart resumes whatever active one the station already has, which is right — a
    /// browser refresh must not abandon a basket the customer is standing next to. But a cart whose
    /// id is 0 cannot be acted on at all: every route is <c>/carts/{cartId}/…</c>, so the till can
    /// read it and never add to it, empty it or tender it. Carts opened before ids were assigned are
    /// exactly that, and resuming one would leave that station permanently unable to sell even
    /// though new carts are fine. Found on the live system immediately after deploying the id fix.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_unaddressable_cart_left_at_a_station_is_replaced_rather_than_resumed()
    {
        using var harness = await PosTestHarness.CreateAsync();

        // The shape the old code left behind: active, at this station, never inserted, so id 0.
        var orphan = Cart.Open(harness.Station.Id, harness.Location.Id, 1, harness.Clock.Now, 720);
        await harness.CartStore.SaveAsync(new CartSnapshot(orphan));

        // Opening a cart moved into CartOpener, shared with the shopper app's trolley pairing. The
        // handler is now only the staff half — permission gate and whose staff number the sale books
        // against — so the mechanics these tests are about are assembled here.
        var handler = new CreateCartHandler(
            new CartOpener(
                harness.CartStore,
                harness.ContextLoader,
                harness.Pricing,
                harness.Clock,
                harness.Db),
            harness.CurrentUser);

        var result = await handler.Handle(new CreateCartCommand(harness.Station.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().NotBe(0, "resuming the orphan would leave this till unable to sell");
    }

    /// <summary>
    /// A parked basket claiming an id no row has must not be resumed either.
    /// <para>
    /// The previous version of this guard asked only whether the snapshot carried a non-zero id,
    /// which is asking the cache to vouch for itself. A build that numbered carts from a sequence
    /// wrote snapshots with ids nothing ever inserted, and those outlived it: the live till resumed
    /// one, priced it correctly, took the tag reads, and then failed at the moment of payment with
    /// <c>cannot insert explicit value for identity column in table 'carts'</c> — because the
    /// write-behind was being asked to create the cart under an id the table would not accept. It
    /// repeated for ever, since every request for a cart handed back the same doomed one.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_parked_cart_whose_row_was_never_written_is_replaced_rather_than_resumed()
    {
        using var harness = await PosTestHarness.CreateAsync();

        // Exactly what the sequence-numbered build left in the store: a snapshot carrying a
        // plausible id, and no row under it.
        var phantom = await harness.OpenCartAsync();
        harness.Db.Carts.Remove(harness.Db.Carts.First(c => c.Id == phantom.Id));
        await harness.Db.SaveChangesAsync(default);

        // Opening a cart moved into CartOpener, shared with the shopper app's trolley pairing. The
        // handler is now only the staff half — permission gate and whose staff number the sale books
        // against — so the mechanics these tests are about are assembled here.
        var handler = new CreateCartHandler(
            new CartOpener(
                harness.CartStore,
                harness.ContextLoader,
                harness.Pricing,
                harness.Clock,
                harness.Db),
            harness.CurrentUser);

        var result = await handler.Handle(new CreateCartCommand(harness.Station.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().NotBe(phantom.Id, "no row was ever written under that id");
        harness.Db.Carts.Any(c => c.Id == result.Value.Id).Should()
            .BeTrue("the cart handed back has to be one the database agrees exists");
    }

    /// <summary>
    /// The identity has to survive the round trip, because the till reads it back out of the store
    /// on every line it adds.
    /// </summary>
    [Fact]
    public async Task A_cart_is_found_again_by_the_id_it_was_opened_with()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var cart = await harness.OpenCartAsync();

        var found = await harness.CartStore.GetAsync(cart.Id);

        found.Should().NotBeNull();
        found!.Cart.Id.Should().Be(cart.Id);
    }

    [Fact]
    public async Task Adding_by_stock_code_prices_the_line_and_returns_the_whole_cart()
    {
        using var harness = await PosTestHarness.CreateAsync();
        await harness.AddProductAsync("POLO01", "Columbia polo", 49.99m);
        var cart = await harness.OpenCartAsync();

        var handler = new AddCartLineByIdentifierHandler(harness.Workflow, harness.Resolver, harness.LineFactory);
        var result = await handler.Handle(new AddCartLineByIdentifierCommand(cart.Id, "POLO01"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Lines.Should().ContainSingle();
        result.Value.Totals.Subtotal.Should().Be(49.99m);
        result.Value.Totals.Tax1Total.Should().Be(2.50m);
        result.Value.Totals.Tax2Total.Should().Be(3.50m);
        result.Value.Totals.GrandTotal.Should().Be(55.99m);
    }

    [Fact]
    public async Task Adding_by_upc_resolves_the_same_product()
    {
        using var harness = await PosTestHarness.CreateAsync();
        await harness.AddProductAsync("POLO01", "Columbia polo", 49.99m, upc: "0123456789012");
        var cart = await harness.OpenCartAsync();

        var handler = new AddCartLineByIdentifierHandler(harness.Workflow, harness.Resolver, harness.LineFactory);
        var result = await handler.Handle(new AddCartLineByIdentifierCommand(cart.Id, "0123456789012"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Lines.Single().Source.Should().Be(LineSource.Barcode);
    }

    [Fact]
    public async Task An_unknown_identifier_is_refused_with_a_machine_readable_code()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var cart = await harness.OpenCartAsync();

        var handler = new AddCartLineByIdentifierHandler(harness.Workflow, harness.Resolver, harness.LineFactory);
        var result = await handler.Handle(new AddCartLineByIdentifierCommand(cart.Id, "NOSUCHTHING"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("product.not_found");
    }

    /// <summary>
    /// A cashier without the discount permission cannot discount, even though the request carries the
    /// field. Dropping the check would make the permission decorative.
    /// </summary>
    [Fact]
    public async Task A_discount_is_refused_when_the_cashier_lacks_the_permission()
    {
        using var harness = await PosTestHarness.CreateAsync();
        await harness.AddProductAsync("POLO01", "Columbia polo", 49.99m);
        harness.CurrentUser.Revoke(PermissionKeys.Pos.Discount);
        var cart = await harness.OpenCartAsync();

        var handler = new AddCartLineByIdentifierHandler(harness.Workflow, harness.Resolver, harness.LineFactory);
        var result = await handler.Handle(
            new AddCartLineByIdentifierCommand(cart.Id, "POLO01", ManualDiscountPct: 20m),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("discount.not_permitted");
    }

    [Fact]
    public async Task Attaching_a_customer_reprices_lines_that_were_already_rung()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("TOOL01", "Wrench", 10.00m);
        var cart = await harness.OpenCartAsync();

        var addHandler = new AddCartLineByIdentifierHandler(harness.Workflow, harness.Resolver, harness.LineFactory);
        await addHandler.Handle(new AddCartLineByIdentifierCommand(cart.Id, "TOOL01", 2m), default);

        var customer = Domain.Customers.Customer.Create(harness.Location.Id, 1001, "Jane", "Doe").Value;
        harness.Db.Customers.Add(customer);

        var profile = Domain.Customers.CustomerPricingProfile.Create(customer.Id);
        profile.UsualDiscountPct = 10m;
        harness.Db.CustomerPricingProfiles.Add(profile);
        await harness.Db.SaveChangesAsync();

        var contextHandler = new CartContextHandlers(harness.Workflow, harness.Db, harness.CurrentUser);
        var result = await contextHandler.Handle(new AssignCartCustomerCommand(cart.Id, customer.Id), default);

        result.IsSuccess.Should().BeTrue();

        // The line was rung before the customer was attached; it must still pick up their discount.
        result.Value.Totals.Subtotal.Should().Be(18.00m);
        result.Value.Customer!.UsualDiscountPct.Should().Be(10m);
    }

    /// <summary>The legacy contract at guide p.11: the override reaches only what comes after it.</summary>
    [Fact]
    public async Task A_tax_override_does_not_reach_lines_already_on_the_screen()
    {
        using var harness = await PosTestHarness.CreateAsync();
        await harness.AddProductAsync("A", "First item", 100.00m);
        await harness.AddProductAsync("B", "Second item", 100.00m);
        var cart = await harness.OpenCartAsync();

        var addHandler = new AddCartLineByIdentifierHandler(harness.Workflow, harness.Resolver, harness.LineFactory);
        await addHandler.Handle(new AddCartLineByIdentifierCommand(cart.Id, "A"), default);

        var contextHandler = new CartContextHandlers(harness.Workflow, harness.Db, harness.CurrentUser);
        await contextHandler.Handle(new SetCartTaxOverrideCommand(cart.Id, false, false), default);

        var result = await addHandler.Handle(new AddCartLineByIdentifierCommand(cart.Id, "B"), default);

        result.IsSuccess.Should().BeTrue();

        var lines = result.Value.Lines.OrderBy(l => l.Sequence).ToList();
        lines[0].Tax1Applies.Should().BeTrue("the first line was rung before the override");
        lines[1].Tax1Applies.Should().BeFalse("the second line was rung after it");
        result.Value.Totals.Tax1Total.Should().Be(5.00m);
    }

    [Fact]
    public async Task Removing_a_line_releases_its_tag_and_returns_the_unit_to_stock()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("SHIRT", "Shirt", 20.00m);
        var unit = await harness.AddTaggedUnitAsync(product, Epc);
        var cart = await harness.OpenCartAsync();

        var addHandler = new AddCartLineByIdentifierHandler(harness.Workflow, harness.Resolver, harness.LineFactory);
        var added = await addHandler.Handle(new AddCartLineByIdentifierCommand(cart.Id, Epc), default);
        added.IsSuccess.Should().BeTrue();

        (await harness.Debouncer.GetHolderAsync(Epc)).Should().Be(harness.Station.Id);

        var removeHandler = new RemoveCartLineHandler(harness.Workflow, harness.Db, harness.Debouncer);
        var removed = await removeHandler.Handle(
            new RemoveCartLineCommand(cart.Id, added.Value.Lines.Single().Sequence),
            default);

        removed.IsSuccess.Should().BeTrue();
        removed.Value.Lines.Should().BeEmpty();
        (await harness.Debouncer.GetHolderAsync(Epc)).Should().BeNull();
        unit.State.Should().Be(Domain.Catalog.SerializedUnitState.InStock);
    }

    [Fact]
    public async Task A_tag_that_is_already_sold_is_refused_with_a_reason_the_cashier_can_act_on()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("SHIRT", "Shirt", 20.00m);
        var unit = await harness.AddTaggedUnitAsync(product, Epc);

        unit.ClaimForCart();
        unit.Sell();
        await harness.Db.SaveChangesAsync();

        var cart = await harness.OpenCartAsync();

        var handler = new AddCartLineByIdentifierHandler(harness.Workflow, harness.Resolver, harness.LineFactory);
        var result = await handler.Handle(new AddCartLineByIdentifierCommand(cart.Id, Epc), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("epc.already_sold");
    }

    [Fact]
    public async Task An_unknown_item_rings_immediately_without_touching_the_catalogue()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var cart = await harness.OpenCartAsync();

        var handler = new AddUnknownItemHandler(harness.Workflow, harness.Db, harness.LineFactory);
        var result = await handler.Handle(
            new AddUnknownItemCommand(cart.Id, "Mystery item", 12.50m),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Lines.Single().Name.Should().Be("Mystery item");
        result.Value.Totals.Subtotal.Should().Be(12.50m);

        harness.Db.Products.Should().ContainSingle(p => p.StockCode == AddUnknownItemHandler.PlaceholderStockCode);
    }

    [Fact]
    public async Task An_unknown_item_can_be_promoted_into_a_real_catalogue_row()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var cart = await harness.OpenCartAsync();

        var handler = new AddUnknownItemHandler(harness.Workflow, harness.Db, harness.LineFactory);
        var result = await handler.Handle(
            new AddUnknownItemCommand(cart.Id, "Cast iron pan", 45.00m, CreateProduct: true, StockCode: "PAN01"),
            default);

        result.IsSuccess.Should().BeTrue();
        harness.Db.Products.Should().ContainSingle(p => p.StockCode == "PAN01");
    }

    [Fact]
    public async Task A_coupon_reduces_the_taxable_base_rather_than_only_the_total()
    {
        using var harness = await PosTestHarness.CreateAsync();
        await harness.AddProductAsync("BOOK", "Hardback", 100.00m);
        var cart = await harness.OpenCartAsync();

        var addHandler = new AddCartLineByIdentifierHandler(harness.Workflow, harness.Resolver, harness.LineFactory);
        await addHandler.Handle(new AddCartLineByIdentifierCommand(cart.Id, "BOOK"), default);

        var adjustmentHandler = new ApplyCartAdjustmentHandler(harness.Workflow, harness.Db, harness.CurrentUser);
        var result = await adjustmentHandler.Handle(
            new ApplyCartAdjustmentCommand(cart.Id, AdjustmentType.Coupon, "SAVE20", 20.00m),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Totals.DiscountTotal.Should().Be(20.00m);
        result.Value.Totals.Tax1Total.Should().Be(4.00m, "tax is charged on 80.00, not 100.00");
        result.Value.Totals.GrandTotal.Should().Be(89.60m);
    }

    [Fact]
    public async Task A_return_line_produces_a_negative_total()
    {
        using var harness = await PosTestHarness.CreateAsync();
        await harness.AddProductAsync("SHIRT", "Shirt", 20.00m);
        var cart = await harness.OpenCartAsync();

        var handler = new AddCartLineByIdentifierHandler(harness.Workflow, harness.Resolver, harness.LineFactory);
        var result = await handler.Handle(
            new AddCartLineByIdentifierCommand(cart.Id, "SHIRT", LineType: LineType.Return),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Totals.Subtotal.Should().Be(-20.00m);
        result.Value.Totals.GrandTotal.Should().Be(-22.40m);
    }

    [Fact]
    public async Task Clearing_the_cart_keeps_the_sale_open()
    {
        using var harness = await PosTestHarness.CreateAsync();
        await harness.AddProductAsync("SHIRT", "Shirt", 20.00m);
        var cart = await harness.OpenCartAsync();

        var addHandler = new AddCartLineByIdentifierHandler(harness.Workflow, harness.Resolver, harness.LineFactory);
        await addHandler.Handle(new AddCartLineByIdentifierCommand(cart.Id, "SHIRT"), default);

        var removeHandler = new RemoveCartLineHandler(harness.Workflow, harness.Db, harness.Debouncer);
        var result = await removeHandler.Handle(new ClearCartCommand(cart.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Lines.Should().BeEmpty();
        result.Value.Status.Should().Be(CartStatus.Active);
    }

    [Fact]
    public async Task Every_mutation_advances_the_revision_so_clients_can_detect_a_gap()
    {
        using var harness = await PosTestHarness.CreateAsync();
        await harness.AddProductAsync("SHIRT", "Shirt", 20.00m);
        var cart = await harness.OpenCartAsync();

        var handler = new AddCartLineByIdentifierHandler(harness.Workflow, harness.Resolver, harness.LineFactory);

        var first = await handler.Handle(new AddCartLineByIdentifierCommand(cart.Id, "SHIRT"), default);
        var second = await handler.Handle(new AddCartLineByIdentifierCommand(cart.Id, "SHIRT"), default);

        second.Value.Revision.Should().BeGreaterThan(first.Value.Revision);
    }
}
