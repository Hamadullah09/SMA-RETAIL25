using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Application.Carts.Commands;
using Retail25.Application.Carts.Queries;
using Retail25.Application.Catalog;
using Retail25.Domain.Catalog;
using Retail25.Domain.Sales;
using Retail25.Infrastructure.Persistence;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// The other half of the shop's stock.
/// <para>
/// RFID is for the tagged, serialized items — one tag, one physical thing. Everything else is
/// counted: a barcode identifies a <em>product</em>, and a customer buying three of them is one
/// line with a quantity of three, not three lines. Both have to work in the same basket, because a
/// real one holds a tagged jacket and three pairs of socks at once.
/// </para>
/// </summary>
[Collection(CommerceApiCollection.Name)]
public sealed class BarcodeAtTheTillTests
{
    private readonly CommerceApiFixture _api;

    public BarcodeAtTheTillTests(CommerceApiFixture api) => _api = api;

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..18].ToUpperInvariant();

    /// <summary>A barcode is 13 digits so the classifier reads it as one rather than as a stock code.</summary>
    private static string UniqueBarcode() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"20{Random.Shared.NextInt64(0, 99999999999):D11}");

    [RequiresDockerFact]
    public async Task Scanning_the_same_barcode_twice_puts_two_on_one_line()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var location = await db.Locations.AsNoTracking().FirstAsync();
        var station = await db.Stations.AsNoTracking().FirstAsync();

        _api.ActingUser.LocationId = location.Id;
        _api.ActingUser.StationId = station.Id;

        var barcode = UniqueBarcode();

        await Ok(sender.Send(new CreateProductCommand(
            location.Id,
            new ProductGeneralSection(Unique("SOCKS"), "Cotton socks", null, ProductType.Standard, barcode, null, null, null),
            RegularPrice: 4.50m,
            Tax1Applies: false,
            Tax2Applies: false)));

        var cart = await EmptyCartAsync(sender, station.Id);

        await Ok(sender.Send(new AddCartLineByIdentifierCommand(cart.Id, barcode)));
        var after = await Ok(sender.Send(new AddCartLineByIdentifierCommand(cart.Id, barcode)));

        // The cashier scanned the same product twice. That is two socks, on one line.
        after.Lines.Should().HaveCount(1, "a second scan of the same barcode is a quantity, not another line");
        after.Lines[0].Quantity.Should().Be(2m);
        after.Totals.Subtotal.Should().Be(9.00m, "two at 4.50");
    }

    /// <summary>
    /// A tagged unit is one physical thing, so a second read of the same tag is the same item —
    /// never a second one. This is the rule that must survive the merging above.
    /// </summary>
    [RequiresDockerFact]
    public async Task A_tagged_unit_never_becomes_a_quantity_of_two()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var location = await db.Locations.AsNoTracking().FirstAsync();
        var station = await db.Stations.AsNoTracking().FirstAsync();

        _api.ActingUser.LocationId = location.Id;
        _api.ActingUser.StationId = station.Id;

        var stockCode = Unique("JACKET");

        var product = await Ok(sender.Send(new CreateProductCommand(
            location.Id,
            new ProductGeneralSection(stockCode, "Tagged jacket", null, ProductType.Standard, null, null, null, null),
            RegularPrice: 120.00m,
            Tax1Applies: false,
            Tax2Applies: false)));

        var epc = $"E28011700000{Random.Shared.NextInt64(0, 999999999999):D12}";

        var unit = SerializedUnit.Create(product.Id, location.Id, null, epc, DateTimeOffset.UtcNow).Value;
        unit.Commission().IsSuccess.Should().BeTrue();
        db.SerializedUnits.Add(unit);
        await db.SaveChangesAsync();

        var cart = await EmptyCartAsync(sender, station.Id);
        await Ok(sender.Send(new AddCartLineByIdentifierCommand(cart.Id, epc)));

        var again = await sender.Send(new AddCartLineByIdentifierCommand(cart.Id, epc));

        // Either the second read is refused or it is absorbed — but there must never be two of it.
        var lines = again.IsSuccess ? again.Value.Lines : (await Ok(sender.Send(new GetCartQuery(cart.Id)))).Lines;

        lines.Should().HaveCount(1);
        lines[0].Quantity.Should().Be(1m, "there is one jacket, and scanning its tag twice does not make two");
    }

    /// <summary>
    /// A product can be given its barcode when it is created.
    /// <para>
    /// The API's create request had no field for one, so a counted item arrived with no barcode and
    /// could not be scanned until somebody went back and edited it. Nothing said so — the product
    /// was created, reported success, and simply would not ring up.
    /// </para>
    /// </summary>
    [RequiresDockerFact]
    public async Task A_barcode_can_be_set_when_the_product_is_created()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var location = await db.Locations.AsNoTracking().FirstAsync();
        var station = await db.Stations.AsNoTracking().FirstAsync();

        _api.ActingUser.LocationId = location.Id;
        _api.ActingUser.StationId = station.Id;

        var barcode = UniqueBarcode();

        var product = await Ok(sender.Send(new CreateProductCommand(
            location.Id,
            new ProductGeneralSection(Unique("SCAN"), "Scannable thing", null, ProductType.Standard, barcode, null, null, null),
            RegularPrice: 7.00m, Tax1Applies: false, Tax2Applies: false)));

        (await db.Products.AsNoTracking().Where(p => p.Id == product.Id).Select(p => p.Upc).FirstAsync())
            .Should().Be(barcode, "a product created with a barcode has to keep it");

        // And the till can find it by that barcode, which is the only reason it matters.
        var cart = await EmptyCartAsync(sender, station.Id);
        var basket = await Ok(sender.Send(new AddCartLineByIdentifierCommand(cart.Id, barcode)));

        basket.Lines.Should().ContainSingle();
        basket.Lines[0].StockCode.Should().Be(product.StockCode);
    }

    /// <summary>One basket, both kinds of stock, one sale.</summary>
    [RequiresDockerFact]
    public async Task A_basket_holds_a_tagged_item_and_counted_ones_together()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var location = await db.Locations.AsNoTracking().FirstAsync();
        var station = await db.Stations.AsNoTracking().FirstAsync();

        _api.ActingUser.LocationId = location.Id;
        _api.ActingUser.StationId = station.Id;

        var tagged = await Ok(sender.Send(new CreateProductCommand(
            location.Id,
            new ProductGeneralSection(Unique("COAT"), "Tagged coat", null, ProductType.Standard, null, null, null, null),
            RegularPrice: 200.00m, Tax1Applies: false, Tax2Applies: false)));

        var epc = $"E28011700000{Random.Shared.NextInt64(0, 999999999999):D12}";
        var unit = SerializedUnit.Create(tagged.Id, location.Id, null, epc, DateTimeOffset.UtcNow).Value;
        unit.Commission().IsSuccess.Should().BeTrue();
        db.SerializedUnits.Add(unit);
        await db.SaveChangesAsync();

        var barcode = UniqueBarcode();
        await Ok(sender.Send(new CreateProductCommand(
            location.Id,
            new ProductGeneralSection(Unique("BELT"), "Counted belt", null, ProductType.Standard, barcode, null, null, null),
            RegularPrice: 25.00m, Tax1Applies: false, Tax2Applies: false)));

        var cart = await EmptyCartAsync(sender, station.Id);

        await Ok(sender.Send(new AddCartLineByIdentifierCommand(cart.Id, epc)));
        await Ok(sender.Send(new AddCartLineByIdentifierCommand(cart.Id, barcode)));
        var basket = await Ok(sender.Send(new AddCartLineByIdentifierCommand(cart.Id, barcode)));

        basket.Lines.Should().HaveCount(2, "one tagged coat, and two belts on a single counted line");
        basket.Totals.Subtotal.Should().Be(250.00m, "200 for the coat, 2 × 25 for the belts");

        var tag = basket.Lines.Single(l => l.Epc is not null);
        tag.Quantity.Should().Be(1m);

        var counted = basket.Lines.Single(l => l.Epc is null);
        counted.Quantity.Should().Be(2m);
    }

    /// <summary>
    /// A cart with nothing on it.
    /// <para>
    /// A station holds one cart at a time and opening one resumes whatever is already there. That is
    /// right at a till and wrong in a suite where every test uses station 1: without this the
    /// baskets accumulate and each test ends up asserting against the one before it.
    /// </para>
    /// </summary>
    private static async Task<Retail25.Application.Carts.Dtos.CartDto> EmptyCartAsync(ISender sender, long stationId)
    {
        var cart = await Ok(sender.Send(new CreateCartCommand(stationId)));

        return cart.Lines.Count == 0 ? cart : await Ok(sender.Send(new ClearCartCommand(cart.Id)));
    }

    private static async Task<T> Ok<T>(Task<Retail25.Domain.Common.Result<T>> pending)
    {
        var result = await pending;
        result.IsSuccess.Should().BeTrue($"the step should succeed, but failed with '{result.Error.Code}'");
        return result.Value;
    }
}
