using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Application.Behaviors;
using Retail25.Application.Carts.Commands;
using Retail25.Application.Catalog;
using Retail25.Application.Common;
using Retail25.Application.Customers;
using Retail25.Domain.Catalog;
using Retail25.Domain.Configuration;
using Retail25.Domain.Sales;
using Retail25.Infrastructure.Persistence;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// A customer's own sales, as a cashier sees them.
/// <para>
/// A shop asked for this in so many words: click a customer, see what they bought, filter it, open an
/// old invoice — and a cashier must be able to. The sales log already lists every sale, but it is a
/// takings report gated on <c>reports.sales</c>, which a cashier does not hold. So these pin both
/// halves of the bargain: a cashier can see one customer's history, and that history is no back door
/// into anyone else's.
/// </para>
/// </summary>
[Collection(CommerceApiCollection.Name)]
public sealed class CustomerSalesTests
{
    private readonly CommerceApiFixture _api;

    public CustomerSalesTests(CommerceApiFixture api) => _api = api;

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..18].ToUpperInvariant();

    private async Task<(long LocationId, long StationId)> TillAsync(ApplicationDbContext db)
    {
        var location = await db.Locations.AsNoTracking().FirstAsync();
        var station = await db.Stations.AsNoTracking().FirstAsync();

        _api.ActingUser.LocationId = location.Id;
        _api.ActingUser.StationId = station.Id;

        return (location.Id, station.Id);
    }

    private static async Task<long> CustomerAsync(ISender sender, long locationId, string lastName)
    {
        var customer = await Ok(sender.Send(new CreateCustomerCommand(
            locationId,
            new CustomerIdentitySection("Test", lastName, null, null, null, null, null))));

        return customer.Id;
    }

    /// <summary>Rings one sale of <paramref name="stockCode"/> for <paramref name="customerId"/>.</summary>
    private static async Task<(long TransactionId, long TransactionNumber)> SellAsync(
        ISender sender,
        ApplicationDbContext db,
        long stationId,
        long customerId,
        string stockCode,
        decimal quantity)
    {
        var cheque = await db.TenderTypes.AsNoTracking().FirstAsync(t => t.Behaviour == TenderBehaviour.Manual);

        var cart = await Ok(sender.Send(new CreateCartCommand(stationId)));
        await Ok(sender.Send(new AssignCartCustomerCommand(cart.Id, customerId)));
        var priced = await Ok(sender.Send(new AddCartLineByIdentifierCommand(cart.Id, stockCode, Quantity: quantity)));

        var total = priced.Totals.GrandTotal;

        var sale = await Ok(sender.Send(new CompleteSaleCommand(
            cart.Id,
            [new TenderRequest(cheque.Id, Amount: total, AmountTendered: total, Reference: "CHQ")],
            Guid.NewGuid().ToString("N"),
            PrintReceipt: false)));

        return (sale.TransactionId, sale.TransactionNumber);
    }

    private static async Task<string> ProductAsync(ISender sender, long locationId, string name)
    {
        var stockCode = Unique("CUST");

        await Ok(sender.Send(new CreateProductCommand(
            locationId,
            new ProductGeneralSection(stockCode, name, null, ProductType.Standard, null, null, null, null),
            RegularPrice: 25.00m,
            Tax1Applies: false,
            Tax2Applies: false)));

        return stockCode;
    }

    [RequiresDockerFact]
    public async Task A_customers_history_is_theirs_alone_newest_first_and_filterable()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (locationId, stationId) = await TillAsync(db);

        var shirt = await ProductAsync(sender, locationId, "Oxford shirt history");
        var mug = await ProductAsync(sender, locationId, "Enamel mug history");

        var alice = await CustomerAsync(sender, locationId, Unique("Alice"));
        var bob = await CustomerAsync(sender, locationId, Unique("Bob"));

        var first = await SellAsync(sender, db, stationId, alice, shirt, 2);
        var second = await SellAsync(sender, db, stationId, alice, mug, 1);
        await SellAsync(sender, db, stationId, bob, shirt, 5);

        var history = await Ok(sender.Send(new CustomerSalesQuery(alice)));

        history.Rows.Select(r => r.Id).Should().Equal([second.TransactionId, first.TransactionId]);
        history.TotalCount.Should().Be(2);
        history.TotalSpent.Should().Be(75.00m);
        history.HasMore.Should().BeFalse();
        history.Rows[1].ItemCount.Should().Be(2m);
        history.Rows[1].PaidWith.Should().NotBeEmpty();

        var byItem = await Ok(sender.Send(new CustomerSalesQuery(alice, Item: "Oxford")));
        byItem.Rows.Select(r => r.Id).Should().Equal([first.TransactionId]);

        var byNumber = await Ok(sender.Send(new CustomerSalesQuery(alice, TransactionNumber: second.TransactionNumber)));
        byNumber.Rows.Select(r => r.Id).Should().Equal([second.TransactionId]);

        var paged = await Ok(sender.Send(new CustomerSalesQuery(alice, Take: 1)));
        paged.Rows.Should().HaveCount(1);
        paged.HasMore.Should().BeTrue();
        paged.TotalSpent.Should().Be(75.00m, "the total covers every match, not just the page on screen");
    }

    [RequiresDockerFact]
    public async Task A_cashier_can_open_a_customers_old_invoice_but_not_someone_elses()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (locationId, stationId) = await TillAsync(db);

        var scarf = await ProductAsync(sender, locationId, "Wool scarf invoice");
        var carol = await CustomerAsync(sender, locationId, Unique("Carol"));
        var dave = await CustomerAsync(sender, locationId, Unique("Dave"));

        var carols = await SellAsync(sender, db, stationId, carol, scarf, 1);
        var daves = await SellAsync(sender, db, stationId, dave, scarf, 3);

        // A cashier: the legacy level-1 preset, which holds customer.read and not reports.sales.
        var previous = _api.ActingUser.ActAs(PermissionKeys.LegacyLevelPresets[1]);

        try
        {
            var invoice = await Ok(sender.Send(new GetCustomerSaleQuery(carol, carols.TransactionId)));

            invoice.TransactionNumber.Should().Be(carols.TransactionNumber);
            invoice.Status.Should().Be(TransactionStatus.Completed);
            invoice.Lines.Should().ContainSingle(l => l.StockCode == scarf && l.Quantity == 1m);
            invoice.GrandTotal.Should().Be(25.00m);

            var history = await Ok(sender.Send(new CustomerSalesQuery(carol)));
            history.Rows.Should().ContainSingle(r => r.Id == carols.TransactionId);

            // Dave's sale, asked for through Carol: the same answer as a sale that does not exist.
            var crossed = await sender.Send(new GetCustomerSaleQuery(carol, daves.TransactionId));
            crossed.IsFailure.Should().BeTrue();
            crossed.Error.Code.Should().Be(CustomerSalesHandlers.SaleNotFound.Code);

            var missing = await sender.Send(new GetCustomerSaleQuery(carol, long.MaxValue));
            missing.Error.Code.Should().Be(CustomerSalesHandlers.SaleNotFound.Code);
        }
        finally
        {
            _api.ActingUser.Permissions = previous;
        }
    }

    [RequiresDockerFact]
    public async Task Without_customer_read_the_history_is_refused()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (locationId, _) = await TillAsync(db);
        var erin = await CustomerAsync(sender, locationId, Unique("Erin"));

        var previous = _api.ActingUser.ActAs([PermissionKeys.Pos.Sell]);

        try
        {
            var asking = () => sender.Send(new CustomerSalesQuery(erin));

            await asking.Should().ThrowAsync<PermissionDeniedException>();
        }
        finally
        {
            _api.ActingUser.Permissions = previous;
        }
    }

    [RequiresDockerFact]
    public async Task An_unknown_customer_is_reported_as_one()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.Send(new CustomerSalesQuery(long.MaxValue));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(CustomerBrowseHandlers.NotFound.Code);
    }

    private static async Task<T> Ok<T>(Task<Retail25.Domain.Common.Result<T>> pending)
    {
        var result = await pending;
        result.IsSuccess.Should().BeTrue($"the step should succeed, but failed with '{result.Error.Code}'");
        return result.Value;
    }
}
