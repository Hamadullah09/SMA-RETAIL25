using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Application.Settings;
using Retail25.Domain.Configuration;
using Retail25.Infrastructure.Persistence;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// White-labelling: the two marks that make an installation belong to a shop.
/// <para>
/// The behaviour worth testing is not "bytes go in, bytes come out". It is that a slot holds exactly
/// one image however many people upload to it at once, that the cheap query the chrome runs on every
/// page load does not drag two megabytes along with it, and that an upload is trusted only as far as
/// its own magic number — a logo endpoint that echoes a caller's content type back is a stored
/// cross-site scripting hole on every page of the application.
/// </para>
/// </summary>
[Collection(CommerceApiCollection.Name)]
public sealed class BrandingTests
{
    private readonly CommerceApiFixture _api;

    public BrandingTests(CommerceApiFixture api) => _api = api;

    /// <summary>The eight-byte PNG signature followed by enough filler to be a plausible file.</summary>
    private static byte[] Png(byte fill = 0x20) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Enumerable.Repeat(fill, 64)];

    // ---------------------------------------------------------------------------------------------

    [RequiresDockerFact]
    public async Task A_logo_is_stored_served_back_and_replaced_in_place()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var location = await Location(db);

        var first = Png(0x11);
        var stored = await Ok(sender.Send(
            new SetBrandingImageCommand(location, BrandingSlot.CompanyLogo, first, "image/png")));

        stored.Present.Should().BeTrue();
        stored.OpacityPct.Should().Be(BrandingAsset.DefaultLogoOpacityPct);

        var served = await Ok(sender.Send(new GetBrandingImageQuery(location, BrandingSlot.CompanyLogo)));
        served.Content.Should().Equal(first);
        served.ContentType.Should().Be("image/png");

        // A second upload is somebody correcting the first, not building a gallery.
        var second = Png(0x22);
        var replaced = await Ok(sender.Send(
            new SetBrandingImageCommand(location, BrandingSlot.CompanyLogo, second, "image/png")));

        replaced.ETag.Should().NotBe(stored.ETag,
            "the cache tag has to move with the bytes or the browser serves yesterday's logo");

        var rows = await db.BrandingAssets.CountAsync(a => a.LocationId == location && a.Slot == BrandingSlot.CompanyLogo);
        rows.Should().Be(1, "one image per slot is a database constraint, not a convention");

        (await Ok(sender.Send(new GetBrandingImageQuery(location, BrandingSlot.CompanyLogo))))
            .Content.Should().Equal(second);
    }

    /// <summary>
    /// The watermark opens at the figure the specification asks for, and can be moved off it without
    /// re-uploading — a pale logo and a dark one do not carry at the same weight.
    /// </summary>
    [RequiresDockerFact]
    public async Task A_watermark_defaults_to_twenty_per_cent_and_is_adjustable()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var location = await Location(db);

        var stored = await Ok(sender.Send(
            new SetBrandingImageCommand(location, BrandingSlot.Watermark, Png(), "image/png")));

        stored.OpacityPct.Should().Be(20);

        (await Ok(sender.Send(new SetBrandingOpacityCommand(location, BrandingSlot.Watermark, 35))))
            .OpacityPct.Should().Be(35);

        var refused = await sender.Send(new SetBrandingOpacityCommand(location, BrandingSlot.Watermark, 140));
        refused.IsFailure.Should().BeTrue();
        refused.Error.Code.Should().Be(BrandingAsset.OpacityOutOfRange.Code);
    }

    /// <summary>
    /// The query the chrome runs on every page load. It reports both slots whether or not anything
    /// has been uploaded — an unbranded installation is a normal state, not a missing row — and it
    /// must not select the bytes to do it.
    /// </summary>
    [RequiresDockerFact]
    public async Task The_chrome_query_reports_every_slot_without_fetching_any_bytes()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var location = await Location(db);

        await sender.Send(new RemoveBrandingImageCommand(location, BrandingSlot.Watermark));
        await sender.Send(new RemoveBrandingImageCommand(location, BrandingSlot.CompanyLogo));

        var empty = await Ok(sender.Send(new GetBrandingQuery(location)));
        empty.Slots.Should().HaveCount(2);
        empty.Slots.Should().OnlyContain(s => !s.Present);
        empty.BusinessName.Should().NotBeNullOrWhiteSpace();

        // A megabyte, so a query that did drag the content along would be obvious rather than
        // arguable. What is asserted is the DTO's shape: there is nowhere for the bytes to go.
        var large = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        }.Concat(Enumerable.Repeat((byte)0x20, 1024 * 1024)).ToArray();

        await Ok(sender.Send(new SetBrandingImageCommand(location, BrandingSlot.Watermark, large, "image/png")));

        var filled = await Ok(sender.Send(new GetBrandingQuery(location)));

        filled.Slots.Single(s => s.Slot == BrandingSlot.Watermark).Present.Should().BeTrue();
        filled.Slots.Single(s => s.Slot == BrandingSlot.CompanyLogo).Present.Should().BeFalse();

        // The absent slot still reports the opacity the chrome would use if something were uploaded,
        // so the client never has to know the default.
        filled.Slots.Single(s => s.Slot == BrandingSlot.CompanyLogo).OpacityPct
            .Should().Be(BrandingAsset.DefaultLogoOpacityPct);

        // The bytes are still there in full — the cheap query simply does not go and get them.
        var storedLength = await db.BrandingAssets.AsNoTracking()
            .Where(a => a.LocationId == location && a.Slot == BrandingSlot.Watermark)
            .Select(a => a.Content.Length)
            .FirstAsync();

        storedLength.Should().Be(large.Length);
    }

    /// <summary>
    /// The check that matters. The stored content type is echoed on the response, so a caller who
    /// can choose it freely can serve a script from this origin.
    /// </summary>
    [RequiresDockerFact]
    public async Task An_upload_is_trusted_only_as_far_as_its_own_magic_number()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var location = await Location(db);

        // HTML claiming to be a PNG. The declared type is on the allow-list; the bytes are not.
        var html = System.Text.Encoding.UTF8.GetBytes("<script>alert(document.cookie)</script>");

        var refused = await sender.Send(
            new SetBrandingImageCommand(location, BrandingSlot.CompanyLogo, html, "image/png"));

        refused.IsFailure.Should().BeTrue();
        refused.Error.Code.Should().Be("image.unsupported_type");

        // And a type that is not on the list at all, whatever the bytes say.
        var svg = await sender.Send(
            new SetBrandingImageCommand(location, BrandingSlot.CompanyLogo, Png(), "image/svg+xml"));

        svg.IsFailure.Should().BeTrue("SVG is a document that can carry script");
    }

    // ---------------------------------------------------------------------------------------------

    private async Task<long> Location(ApplicationDbContext db)
    {
        var location = await db.Locations.AsNoTracking().FirstAsync();
        _api.ActingUser.LocationId = location.Id;
        return location.Id;
    }

    private static async Task<T> Ok<T>(Task<Retail25.Domain.Common.Result<T>> pending)
    {
        var result = await pending;
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }
}
