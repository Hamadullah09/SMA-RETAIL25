using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;
using SkiaSharp;

namespace RetailPlus.Server;

public sealed record StoreSettingsInfo(string? Version, string? CompanyName, string? Address1, string? Address2, string? City, string? Province, string? PostalCode,
    string? Phone, string? Email, string? Website, string? TaxNumber, string? CurrencySymbol, string? ReceiptHeader, string? ReceiptFooter, string? Country = null,
    string? Fax = null);

/// <summary>
/// A store's settings and its logo and watermark. The values are the store's own rows (settings, extras for what has no
/// column, the receipt texts), so they reach the store's tills with the next download.
/// </summary>
public static partial class Endpoints
{
    private const string EmailExtra = "EMAIL";
    private const string WebsiteExtra = "WEBSITE";
    private const string CurrencyExtra = "CURRENCY";

    private static void MapSettings(RouteGroupBuilder api)
    {
        api.MapGet("/stores/{id:guid}/settings", async (Guid id, ApiRequest api) =>
        {
            await RequireStoreAsync(api, id);
            return Results.Ok(await SettingsAsync(api, id));
        }).RequireAuthorization(Need(Permissions.SetupStore));
        api.MapPut("/stores/{id:guid}/settings", async (Guid id, StoreSettingsInfo body, ApiRequest api) =>
        {
            await RequireStoreAsync(api, id);
            var current = await SettingsAsync(api, id);
            ApiRequest.CheckVersion(body.Version, current.Version!);
            var setup = new SetupService(api.For(id));
            var settings = await setup.GetSettingsAsync();
            settings.CompanyName = body.CompanyName; settings.Address1 = body.Address1; settings.Address2 = body.Address2; settings.City = body.City;
            settings.State = body.Province; settings.Zip = body.PostalCode; settings.Phone = body.Phone; settings.TaxRegistrationNumber = body.TaxNumber;
            if (body.Country is not null) settings.Country = body.Country;
            if (body.Fax is not null) settings.Fax = body.Fax;
            await setup.SaveSettingsAsync(settings);
            await setup.SetExtraAsync(EmailExtra, body.Email);
            await setup.SetExtraAsync(WebsiteExtra, body.Website);
            await setup.SetExtraAsync(CurrencyExtra, body.CurrencySymbol);
            await setup.SetTextAsync(DocumentTextKind.InvoiceHeader, body.ReceiptHeader ?? "");
            await setup.SetTextAsync(DocumentTextKind.InvoiceFooter, body.ReceiptFooter ?? "");
            return Results.Ok(await SettingsAsync(api, id));
        }).RequireAuthorization(Need(Permissions.SetupStore));

        api.MapGet("/stores/{id:guid}/images/{kind}", async (Guid id, string kind, ApiRequest api) =>
        {
            await RequireStoreAsync(api, id);
            var picture = await new BrandingService(api.For(id)).GetAsync(ImageKind(kind));
            return picture is null ? throw ApiException.NotFound("That picture") : Results.File(picture, "image/jpeg");
        }).RequireAuthorization(Need(Permissions.SetupStore));
        api.MapPut("/stores/{id:guid}/images/{kind}", async (Guid id, string kind, HttpRequest request, ApiRequest api) =>
        {
            await RequireStoreAsync(api, id);
            var which = ImageKind(kind);
            if (!request.HasFormContentType) throw ApiException.Bad("Send the picture as a form upload with the field file.");
            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file") ?? throw ApiException.Bad("Send the picture in the field file.");
            if (file.Length > 20 * 1024 * 1024) throw ApiException.Bad("The picture is too large (more than 20 MB).");
            byte[] original;
            await using (var stream = file.OpenReadStream())
            using (var memory = new MemoryStream())
            {
                await stream.CopyToAsync(memory);
                original = memory.ToArray();
            }
            await new BrandingService(api.For(id)).SetAsync(which, Picture.Jpeg(original));
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.SetupStore)).DisableAntiforgery();
        api.MapDelete("/stores/{id:guid}/images/{kind}", async (Guid id, string kind, ApiRequest api) =>
        {
            await RequireStoreAsync(api, id);
            await new BrandingService(api.For(id)).SetAsync(ImageKind(kind), null);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.SetupStore));
    }

    private static StoreImageKind ImageKind(string kind) => kind.ToLowerInvariant() switch
    {
        "logo" => StoreImageKind.Logo,
        "watermark" => StoreImageKind.Watermark,
        _ => throw ApiException.NotFound("That kind of picture"),
    };

    private static async Task RequireStoreAsync(ApiRequest api, Guid id)
    {
        api.RequireStore(id, "That store");
        await using var db = api.Root.Create();
        if (!await db.Stores.AnyAsync(s => s.Id == id)) throw ApiException.NotFound("That store");
    }

    private static async Task<StoreSettingsInfo> SettingsAsync(ApiRequest api, Guid id)
    {
        var data = api.For(id);
        var setup = new SetupService(data);
        var settings = await setup.GetSettingsAsync();
        string? header = await setup.GetTextAsync(DocumentTextKind.InvoiceHeader), footer = await setup.GetTextAsync(DocumentTextKind.InvoiceFooter);
        await using var db = api.Root.Create();
        // The version covers the settings row, the extras and the texts: a change to any of them is a change.
        var times = new List<DateTime> { settings.UpdatedAtUtc };
        times.AddRange(await db.StoreSettingExtras.Where(x => x.StoreId == id).Select(x => x.UpdatedAtUtc).ToListAsync());
        times.AddRange(await db.DocumentTexts.Where(x => x.StoreId == id && x.LocationId == null).Select(x => x.UpdatedAtUtc).ToListAsync());
        return new StoreSettingsInfo(ApiRequest.Version(times.Max()), settings.CompanyName, settings.Address1, settings.Address2, settings.City, settings.State, settings.Zip,
            settings.Phone, await setup.GetExtraAsync(EmailExtra), await setup.GetExtraAsync(WebsiteExtra), settings.TaxRegistrationNumber,
            await setup.GetExtraAsync(CurrencyExtra), header, footer, settings.Country, settings.Fax);
    }
}

/// <summary>Pictures are kept as the desktop app keeps them: a JPEG of at most 800 pixels a side, on white.</summary>
public static class Picture
{
    public const int MaxSide = 800;

    public static byte[] Jpeg(byte[] original)
    {
        const string notPicture = "The file is not a picture (JPEG, PNG, GIF, BMP or WebP).";
        using var data = SKData.CreateCopy(original);
        using var codec = SKCodec.Create(data) ?? throw ApiException.Bad(notPicture);
        using var source = SKBitmap.Decode(codec) ?? throw ApiException.Bad(notPicture);
        double scale = Math.Min(1, (double)MaxSide / Math.Max(source.Width, source.Height));
        int w = Math.Max(1, (int)Math.Round(source.Width * scale)), h = Math.Max(1, (int)Math.Round(source.Height * scale));
        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        using (var image = SKImage.FromBitmap(source))
            canvas.DrawImage(image, new SKRect(0, 0, w, h), new SKSamplingOptions(SKCubicResampler.Mitchell));
        canvas.Flush();
        using var snapshot = surface.Snapshot();
        using var jpeg = snapshot.Encode(SKEncodedImageFormat.Jpeg, 85);
        return jpeg.ToArray();
    }
}
