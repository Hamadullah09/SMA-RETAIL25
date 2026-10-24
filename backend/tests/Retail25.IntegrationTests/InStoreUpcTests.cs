using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Application.Behaviors;
using Retail25.Application.Carts.Commands;
using Retail25.Application.Catalog;
using Retail25.Application.Common;
using Retail25.Domain.Catalog;
using Retail25.Domain.Identification;
using Retail25.Domain.Sales;
using Retail25.Infrastructure.Persistence;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// Giving items without a barcode a UPC-A of the shop's own, and ringing them up by it.
/// <para>
/// A shop asked for UPC-A barcodes. Its own goods carry alphanumeric stock codes, which UPC-A cannot
/// hold, so they get a number from the retailer's range instead — and these pin the two things that
/// make that safe: a barcode the item already has is never replaced, and the number issued is one a
/// scanner can actually ring, in either of the forms scanners send.
/// </para>
/// </summary>
[Collection(CommerceApiCollection.Name)]
public sealed class InStoreUpcTests
{
    private readonly CommerceApiFixture _api;

    public InStoreUpcTests(CommerceApiFixture api) => _api = api;

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..18].ToUpperInvariant();

    /// <summary>A valid manufacturer-style UPC-A no other test will have used.</summary>
    private static string ManufacturerUpc()
    {
        var body = "7" + Random.Shared.NextInt64(0, 10_000_000_000).ToString("D10", System.Globalization.CultureInfo.InvariantCulture);
        return body + UpcA.CheckDigit(body);
    }

    private async Task<(long LocationId, long StationId)> TillAsync(ApplicationDbContext db)
    {
        var location = await db.Locations.AsNoTracking().FirstAsync();
        var station = await db.Stations.AsNoTracking().FirstAsync();

        _api.ActingUser.LocationId = location.Id;
        _api.ActingUser.StationId = station.Id;

        return (location.Id, station.Id);
    }

    private static async Task<long> ProductAsync(ISender sender, long locationId, string? upc)
    {
        var product = await Ok(sender.Send(new CreateProductCommand(
            locationId,
            new ProductGeneralSection(Unique("UPC"), "Barcode test item", null, ProductType.Standard, upc, null, null, null),
            RegularPrice: 5.00m,
            Tax1Applies: false,
            Tax2Applies: false)));

        return product.Id;
    }

    [RequiresDockerFact]
    public async Task Items_without_a_barcode_get_an_in_store_upc_a_and_the_rest_are_left_alone()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (locationId, _) = await TillAsync(db);

        var manufacturers = ManufacturerUpc();
        var branded = await ProductAsync(sender, locationId, manufacturers);
        var ownBrand = await ProductAsync(sender, locationId, null);

        var result = await Ok(sender.Send(new AssignInStoreUpcsCommand(locationId, [branded, ownBrand])));

        result.Assigned.Should().ContainSingle();
        result.Assigned[0].ProductId.Should().Be(ownBrand);
        result.Assigned[0].Upc.Should().Be(UpcA.InStore(UpcA.InStoreKind.Product, ownBrand));
        result.AlreadyHadOne.Should().Be(1);
        result.Skipped.Should().Be(0);

        var stored = await db.Products.AsNoTracking()
            .Where(p => p.Id == branded || p.Id == ownBrand)
            .ToDictionaryAsync(p => p.Id, p => p.Upc, CancellationToken.None);

        stored[branded].Should().Be(manufacturers, "a barcode printed on someone else's packaging is never replaced");
        UpcA.IsValid(stored[ownBrand]).Should().BeTrue();

        // Asking again changes nothing: the number is the item's now.
        var again = await Ok(sender.Send(new AssignInStoreUpcsCommand(locationId, [branded, ownBrand])));
        again.Assigned.Should().BeEmpty();
        again.AlreadyHadOne.Should().Be(2);
    }

    [RequiresDockerFact]
    public async Task An_item_rings_by_its_upc_a_whether_the_scanner_sends_twelve_digits_or_thirteen()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (locationId, stationId) = await TillAsync(db);

        var ownBrand = await ProductAsync(sender, locationId, null);
        var issued = await Ok(sender.Send(new AssignInStoreUpcsCommand(locationId, [ownBrand])));
        var upc = issued.Assigned.Single().Upc;

        var cart = await Ok(sender.Send(new CreateCartCommand(stationId)));

        var byUpc = await Ok(sender.Send(new AddCartLineByIdentifierCommand(cart.Id, upc)));
        byUpc.Lines.Should().Contain(l => l.ProductId == ownBrand && l.Source == LineSource.Barcode);

        var byEan = await Ok(sender.Send(new AddCartLineByIdentifierCommand(cart.Id, "0" + upc)));
        byEan.Lines.Where(l => l.ProductId == ownBrand).Sum(l => l.Quantity).Should().Be(2m);

        await Ok(sender.Send(new ClearCartCommand(cart.Id)));
    }

    [RequiresDockerFact]
    public async Task A_cashier_cannot_number_the_catalogue()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (locationId, _) = await TillAsync(db);

        var previous = _api.ActingUser.ActAs(PermissionKeys.LegacyLevelPresets[1]);

        try
        {
            var numbering = () => sender.Send(new AssignInStoreUpcsCommand(locationId));

            await numbering.Should().ThrowAsync<PermissionDeniedException>();
        }
        finally
        {
            _api.ActingUser.Permissions = previous;
        }
    }

    private static async Task<T> Ok<T>(Task<Retail25.Domain.Common.Result<T>> pending)
    {
        var result = await pending;
        result.IsSuccess.Should().BeTrue($"the step should succeed, but failed with '{result.Error.Code}'");
        return result.Value;
    }
}
