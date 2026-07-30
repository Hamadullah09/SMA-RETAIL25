using FluentAssertions;
using Retail25.Application.Orders;
using Retail25.Application.UnitTests.Masters;
using Retail25.Domain.Configuration;
using Retail25.Domain.Orders;
using Xunit;

namespace Retail25.Application.UnitTests.Purchasing;

/// <summary>
/// Stage 6: customer orders / back orders, layaways and price quotes (guide p.9, p.16) — the last
/// entirely-net-new vertical in Phase 5. Each reserving type (order, layaway) claims its stock via
/// <see cref="Retail25.Domain.Inventory.StockLevel.Committed"/> the moment it is placed and releases
/// it the moment it is filled, paid off, or cancelled — never both, never neither.
/// </summary>
public sealed class OrdersTests
{
    [Fact]
    public async Task Placing_a_customer_order_reserves_the_ordered_quantity()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");
        var product = await harness.AddProductAsync("SKU-1", "Widget", onHand: 0m);

        var result = await harness.CustomerOrders.Handle(
            new CreateCustomerOrderCommand(customer.Id, harness.Location.Id, [new CustomerOrderLineInput(product.Id, 5m, 12.50m)]), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(CustomerOrderStatus.Open);

        harness.Db.StockLevels.Should().ContainSingle(s => s.ProductId == product.Id && s.Committed == 5m);
    }

    [Fact]
    public async Task Filling_an_order_with_no_stock_yet_leaves_it_open()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");
        var product = await harness.AddProductAsync("SKU-1", "Widget", onHand: 0m);
        var created = await harness.CustomerOrders.Handle(
            new CreateCustomerOrderCommand(customer.Id, harness.Location.Id, [new CustomerOrderLineInput(product.Id, 5m, 12.50m)]), default);

        var result = await harness.CustomerOrders.Handle(new FillCustomerOrderCommand(created.Value.Id), default);

        result.Value.Status.Should().Be(CustomerOrderStatus.Open);
        result.Value.Lines.Single().FilledQty.Should().Be(0m);
    }

    [Fact]
    public async Task Filling_an_order_once_stock_arrives_releases_the_reservation_and_closes_it()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");
        var product = await harness.AddProductAsync("SKU-1", "Widget", onHand: 0m);
        var created = await harness.CustomerOrders.Handle(
            new CreateCustomerOrderCommand(customer.Id, harness.Location.Id, [new CustomerOrderLineInput(product.Id, 5m, 12.50m)]), default);

        // A PO receipt (or any restock) lands after the order was placed.
        product.ReceiveStock(5m, 8m, 0m);
        await harness.Db.SaveChangesAsync();

        var result = await harness.CustomerOrders.Handle(new FillCustomerOrderCommand(created.Value.Id), default);

        result.Value.Status.Should().Be(CustomerOrderStatus.Filled);
        result.Value.Lines.Single().FilledQty.Should().Be(5m);
        result.Value.Lines.Single().UnitPrice.Should().Be(12.50m); // the price the customer was promised, not today's price

        harness.Db.StockLevels.Should().ContainSingle(s => s.ProductId == product.Id && s.Committed == 0m);
    }

    [Fact]
    public async Task A_partial_restock_partially_fills_the_order()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");
        var product = await harness.AddProductAsync("SKU-1", "Widget", onHand: 0m);
        var created = await harness.CustomerOrders.Handle(
            new CreateCustomerOrderCommand(customer.Id, harness.Location.Id, [new CustomerOrderLineInput(product.Id, 10m, 12.50m)]), default);

        product.ReceiveStock(4m, 8m, 0m);
        await harness.Db.SaveChangesAsync();

        var result = await harness.CustomerOrders.Handle(new FillCustomerOrderCommand(created.Value.Id), default);

        result.Value.Status.Should().Be(CustomerOrderStatus.PartiallyFilled);
        result.Value.Lines.Single().FilledQty.Should().Be(4m);
        harness.Db.StockLevels.Should().ContainSingle(s => s.ProductId == product.Id && s.Committed == 6m);
    }

    [Fact]
    public async Task Cancelling_an_open_order_releases_its_full_reservation()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");
        var product = await harness.AddProductAsync("SKU-1", "Widget", onHand: 0m);
        var created = await harness.CustomerOrders.Handle(
            new CreateCustomerOrderCommand(customer.Id, harness.Location.Id, [new CustomerOrderLineInput(product.Id, 5m, 12.50m)]), default);

        var result = await harness.CustomerOrders.Handle(new CancelCustomerOrderCommand(created.Value.Id), default);

        result.Value.Status.Should().Be(CustomerOrderStatus.Cancelled);
        harness.Db.StockLevels.Should().ContainSingle(s => s.ProductId == product.Id && s.Committed == 0m);
    }

    [Fact]
    public async Task A_layaway_reserves_stock_and_totals_its_lines()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");
        var product = await harness.AddProductAsync("SKU-1", "Widget", onHand: 20m);

        var result = await harness.Layaways.Handle(
            new CreateLayawayCommand(customer.Id, harness.Location.Id, [new LayawayLineInput(product.Id, 2m, 40m)]), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Total.Should().Be(80m);
        result.Value.Status.Should().Be(LayawayStatus.Open);
        harness.Db.StockLevels.Should().ContainSingle(s => s.ProductId == product.Id && s.Committed == 2m);
    }

    [Fact]
    public async Task A_partial_deposit_leaves_the_layaway_open_and_stock_reserved()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");
        var product = await harness.AddProductAsync("SKU-1", "Widget", onHand: 20m);
        var cash = await harness.AddTenderAsync("CASH", "Cash", TenderBehaviour.Cash);
        var created = await harness.Layaways.Handle(
            new CreateLayawayCommand(customer.Id, harness.Location.Id, [new LayawayLineInput(product.Id, 2m, 40m)]), default);

        var result = await harness.Layaways.Handle(new TakeLayawayPaymentCommand(created.Value.Id, 30m, cash.Id), default);

        result.Value.Status.Should().Be(LayawayStatus.Open);
        result.Value.AmountPaid.Should().Be(30m);
        harness.Db.StockLevels.Should().ContainSingle(s => s.ProductId == product.Id && s.Committed == 2m);
    }

    [Fact]
    public async Task Paying_off_a_layaway_in_full_releases_its_reservation()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");
        var product = await harness.AddProductAsync("SKU-1", "Widget", onHand: 20m);
        var cash = await harness.AddTenderAsync("CASH", "Cash", TenderBehaviour.Cash);
        var created = await harness.Layaways.Handle(
            new CreateLayawayCommand(customer.Id, harness.Location.Id, [new LayawayLineInput(product.Id, 2m, 40m)]), default);

        await harness.Layaways.Handle(new TakeLayawayPaymentCommand(created.Value.Id, 30m, cash.Id), default);
        var result = await harness.Layaways.Handle(new TakeLayawayPaymentCommand(created.Value.Id, 50m, cash.Id), default);

        result.Value.Status.Should().Be(LayawayStatus.PaidInFull);
        result.Value.AmountPaid.Should().Be(80m);
        harness.Db.StockLevels.Should().ContainSingle(s => s.ProductId == product.Id && s.Committed == 0m);
        harness.Db.LayawayPayments.Should().HaveCount(2);
    }

    [Fact]
    public async Task Cancelling_a_layaway_releases_its_reservation()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");
        var product = await harness.AddProductAsync("SKU-1", "Widget", onHand: 20m);
        var created = await harness.Layaways.Handle(
            new CreateLayawayCommand(customer.Id, harness.Location.Id, [new LayawayLineInput(product.Id, 2m, 40m)]), default);

        var result = await harness.Layaways.Handle(new CancelLayawayCommand(created.Value.Id), default);

        result.Value.Status.Should().Be(LayawayStatus.Cancelled);
        harness.Db.StockLevels.Should().ContainSingle(s => s.ProductId == product.Id && s.Committed == 0m);
    }

    [Fact]
    public async Task A_price_quote_totals_its_lines_and_reserves_nothing()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");
        var product = await harness.AddProductAsync("SKU-1", "Widget", onHand: 20m);

        var result = await harness.PriceQuotes.Handle(
            new CreatePriceQuoteCommand(customer.Id, harness.Location.Id, [new PriceQuoteLineInput(product.Id, 3m, 15m)]), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Total.Should().Be(45m);
        harness.Db.StockLevels.Should().BeEmpty();
    }

    [Fact]
    public async Task Converting_an_open_quote_marks_it_converted()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");
        var product = await harness.AddProductAsync("SKU-1", "Widget", onHand: 20m);
        var created = await harness.PriceQuotes.Handle(
            new CreatePriceQuoteCommand(customer.Id, harness.Location.Id, [new PriceQuoteLineInput(product.Id, 3m, 15m)]), default);

        var result = await harness.PriceQuotes.Handle(new ConvertPriceQuoteCommand(created.Value.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(PriceQuoteStatus.Converted);
    }

    [Fact]
    public async Task Converting_an_expired_quote_is_refused_and_marks_it_expired()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");
        var product = await harness.AddProductAsync("SKU-1", "Widget", onHand: 20m);
        var created = await harness.PriceQuotes.Handle(
            new CreatePriceQuoteCommand(
                customer.Id, harness.Location.Id, [new PriceQuoteLineInput(product.Id, 3m, 15m)], harness.Today.AddDays(-1)),
            default);

        var result = await harness.PriceQuotes.Handle(new ConvertPriceQuoteCommand(created.Value.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("price_quote.expired");

        var refreshed = await harness.PriceQuotes.Handle(new GetPriceQuoteQuery(created.Value.Id), default);
        refreshed.Value.Status.Should().Be(PriceQuoteStatus.Expired);
    }
}
