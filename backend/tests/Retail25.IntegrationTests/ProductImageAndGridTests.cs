using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Application.Catalog;
using Retail25.Domain.Catalog;
using Retail25.Infrastructure.Persistence;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// Product pictures, and the till grid that decides how to draw them.
/// <para>
/// The interesting behaviour is not "bytes go in, bytes come out" — it is the two things that keep
/// the grid honest: an upload is trusted only as far as its own magic number, and the choice between
/// tiles and rows is made from the whole catalogue rather than from whatever happens to be on screen.
/// Both have a failure mode that a smoke test would sail past.
/// </para>
/// </summary>
[Collection(CommerceApiCollection.Name)]
public sealed class ProductImageAndGridTests
{
    private readonly CommerceApiFixture _api;

    public ProductImageAndGridTests(CommerceApiFixture api) => _api = api;

    /// <summary>The eight-byte PNG signature followed by enough filler to be a plausible file.</summary>
    private static byte[] Png() =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Enumerable.Repeat((byte)0x20, 64)];

    private static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, .. Enumerable.Repeat((byte)0x20, 64)];

    // ---------------------------------------------------------------------------------------------

    [RequiresDockerFact]
    public async Task A_picture_is_stored_served_back_and_flagged_on_the_item()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (location, _) = await Context(db);
        var product = await NewProduct(sender, location, "Photographed mug");

        var bytes = Png();
        (await sender.Send(new SetProductImageCommand(product.Id, bytes, "image/png")))
            .IsSuccess.Should().BeTrue();

        // The flag the grid reads, written in the same transaction as the bytes.
        var flagged = await db.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
        flagged.HasImage.Should().BeTrue();

        var served = await sender.Send(new GetProductImageQuery(product.Id));
        served.IsSuccess.Should().BeTrue();
        served.Value.Content.Should().Equal(bytes);
        served.Value.ContentType.Should().Be("image/png");
        served.Value.ETag.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// A second upload replaces the first rather than adding a row, and the ETag moves with it —
    /// otherwise a clerk correcting a photo would keep being served the one they just replaced.
    /// </summary>
    [RequiresDockerFact]
    public async Task Uploading_again_replaces_the_picture_and_changes_the_tag()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (location, _) = await Context(db);
        var product = await NewProduct(sender, location, "Twice-photographed mug");

        await sender.Send(new SetProductImageCommand(product.Id, Png(), "image/png"));
        var first = (await sender.Send(new GetProductImageQuery(product.Id))).Value;

        await sender.Send(new SetProductImageCommand(product.Id, Jpeg(), "image/jpeg"));
        var second = (await sender.Send(new GetProductImageQuery(product.Id))).Value;

        second.ContentType.Should().Be("image/jpeg");
        second.ETag.Should().NotBe(first.ETag, "a cached browser must be able to tell the bytes changed");

        (await db.ProductImages.AsNoTracking().CountAsync(i => i.ProductId == product.Id))
            .Should().Be(1, "one picture per item is a database constraint, not a convention");
    }

    /// <summary>
    /// The check that matters. A caller can put anything in a Content-Type header, and that header is
    /// echoed back on the response — so a script served as <c>image/png</c> would be stored cross-site
    /// scripting. The bytes have to agree with the claim.
    /// </summary>
    [RequiresDockerFact]
    public async Task A_file_that_is_not_the_image_it_claims_to_be_is_refused()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (location, _) = await Context(db);
        var product = await NewProduct(sender, location, "Not really a mug");

        // "<script>alert(1)</script>", declared as a PNG.
        var script = "<script>alert(1)</script>"u8.ToArray();

        var refused = await sender.Send(new SetProductImageCommand(product.Id, script, "image/png"));

        refused.IsFailure.Should().BeTrue();
        refused.Error.Code.Should().Be(ProductImage.UnsupportedType.Code);

        var untouched = await db.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
        untouched.HasImage.Should().BeFalse("a refused upload must not leave the item claiming a picture");
    }

    [RequiresDockerFact]
    public async Task A_type_outside_the_allow_list_is_refused_even_when_the_bytes_are_real()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (location, _) = await Context(db);
        var product = await NewProduct(sender, location, "SVG mug");

        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray();

        // SVG is an image and a browser will render it — including any script inside it. It is not on
        // the list for exactly that reason.
        var refused = await sender.Send(new SetProductImageCommand(product.Id, svg, "image/svg+xml"));

        refused.IsFailure.Should().BeTrue();
        refused.Error.Code.Should().Be(ProductImage.UnsupportedType.Code);
    }

    [RequiresDockerFact]
    public async Task Removing_a_picture_clears_the_flag_and_the_row()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (location, _) = await Context(db);
        var product = await NewProduct(sender, location, "Briefly photographed mug");

        await sender.Send(new SetProductImageCommand(product.Id, Png(), "image/png"));
        (await sender.Send(new RemoveProductImageCommand(product.Id))).IsSuccess.Should().BeTrue();

        (await db.ProductImages.AsNoTracking().AnyAsync(i => i.ProductId == product.Id)).Should().BeFalse();
        (await db.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id)).HasImage.Should().BeFalse();
        (await sender.Send(new GetProductImageQuery(product.Id))).IsFailure.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The grid finds the item and reports it as having no picture, so the till lays it out as a row.
    /// </summary>
    [RequiresDockerFact]
    public async Task The_grid_returns_items_with_their_picture_flag()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (location, _) = await Context(db);
        var name = $"Grid mug {Guid.NewGuid():N}"[..20];
        var product = await NewProduct(sender, location, name);

        var page = await sender.Send(new PosGridQuery(location, Search: name));

        page.IsSuccess.Should().BeTrue();
        var row = page.Value.Items.Should().ContainSingle(i => i.Id == product.Id).Subject;
        row.HasImage.Should().BeFalse();
        row.Name.Should().Be(name);
        row.RegularPrice.Should().Be(12.50m);

        await sender.Send(new SetProductImageCommand(product.Id, Png(), "image/png"));

        var after = await sender.Send(new PosGridQuery(location, Search: name));
        after.Value.Items.Single(i => i.Id == product.Id).HasImage.Should().BeTrue();
        after.Value.AnyImages.Should().BeTrue();
    }

    /// <summary>
    /// The layout decision, and the bug it is guarding against.
    /// <para>
    /// <c>AnyImages</c> is asked of the whole filtered catalogue, not of the returned page. Sixty
    /// picture-less items at the top of an alphabet must not report "no pictures" when the sixty-first
    /// has one — the grid would open as rows and then flip to tiles when the cashier scrolled.
    /// </para>
    /// </summary>
    [RequiresDockerFact]
    public async Task Any_images_is_answered_from_the_whole_filter_not_the_page()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (location, _) = await Context(db);

        // A family of items sharing a searchable prefix, named so that the only photographed one
        // sorts last and therefore falls off the first page.
        var family = $"ZPAGE{Guid.NewGuid():N}"[..12].ToUpperInvariant();

        for (var index = 0; index < 4; index += 1)
        {
            await NewProduct(sender, location, $"{family} item {index}");
        }

        var photographed = await NewProduct(sender, location, $"{family} zzz last");
        await sender.Send(new SetProductImageCommand(photographed.Id, Png(), "image/png"));

        // A page too small to reach the photographed item.
        var page = await sender.Send(new PosGridQuery(location, Search: family, Take: 2));

        page.Value.Items.Should().HaveCount(2);
        page.Value.Total.Should().Be(5);
        page.Value.Items.Should().NotContain(i => i.Id == photographed.Id, "it sorts last, by construction");

        page.Value.AnyImages.Should().BeTrue(
            "the flag describes the catalogue being browsed, not the slice that fits on one screen");
    }

    [RequiresDockerFact]
    public async Task The_grid_pages_without_repeating_or_skipping_an_item()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (location, _) = await Context(db);
        var family = $"YPAGE{Guid.NewGuid():N}"[..12].ToUpperInvariant();

        for (var index = 0; index < 5; index += 1)
        {
            await NewProduct(sender, location, $"{family} item {index}");
        }

        var first = await sender.Send(new PosGridQuery(location, Search: family, Take: 2));
        var second = await sender.Send(new PosGridQuery(location, Search: family, Skip: 2, Take: 2));
        var third = await sender.Send(new PosGridQuery(location, Search: family, Skip: 4, Take: 2));

        var seen = first.Value.Items.Concat(second.Value.Items).Concat(third.Value.Items).Select(i => i.Id).ToList();

        seen.Should().HaveCount(5);
        seen.Should().OnlyHaveUniqueItems("offset paging over a stable sort must not repeat a row");
    }

    /// <summary>A deleted item is not sellable, so it is not on the picker.</summary>
    [RequiresDockerFact]
    public async Task A_deleted_item_is_not_on_the_grid()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (location, _) = await Context(db);
        var name = $"Gone mug {Guid.NewGuid():N}"[..20];
        var product = await NewProduct(sender, location, name);

        await sender.Send(new DeleteProductCommand(product.Id));

        var page = await sender.Send(new PosGridQuery(location, Search: name));
        page.Value.Items.Should().NotContain(i => i.Id == product.Id);
    }

    // ---------------------------------------------------------------------------------------------

    private async Task<(long Location, long Station)> Context(ApplicationDbContext db)
    {
        var location = await db.Locations.AsNoTracking().FirstAsync();
        var station = await db.Stations.AsNoTracking().FirstAsync();

        _api.ActingUser.LocationId = location.Id;
        _api.ActingUser.StationId = station.Id;

        return (location.Id, station.Id);
    }

    private static async Task<ProductFormDto> NewProduct(ISender sender, long location, string name)
    {
        var stockCode = $"IMG{Guid.NewGuid():N}"[..12].ToUpperInvariant();

        var created = await sender.Send(new CreateProductCommand(
            location,
            new ProductGeneralSection(stockCode, name, null, ProductType.Standard, null, null, null, null),
            RegularPrice: 12.50m,
            Tax1Applies: false,
            Tax2Applies: false));

        created.IsSuccess.Should().BeTrue($"the item should be created, but failed with '{created.Error.Code}'");
        return created.Value;
    }
}
