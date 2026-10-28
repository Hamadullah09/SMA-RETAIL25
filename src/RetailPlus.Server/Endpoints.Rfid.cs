using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Services;

namespace RetailPlus.Server;

/// <summary>An RFID hub's key as the list shows it: never the key itself, only its first characters.</summary>
public sealed record RfidHubInfo(Guid Id, Guid StoreId, string Name, string KeyHint, DateTime CreatedAtUtc, string? CreatedBy, DateTime? LastSeenAtUtc,
    string? LastAddress, bool Revoked, DateTime? RevokedAtUtc);
public sealed record NewRfidHubRequest(string? Name);
/// <summary>A new hub key. <c>Key</c> is shown this once; the server keeps only its hash.</summary>
public sealed record NewRfidHubResponse(RfidHubInfo Hub, string Key);
/// <summary>What a hub receives: its store, and the store's readers with their antennas and every setting the hub uses.</summary>
public sealed record RfidHubReaders(Guid StoreId, string StoreCode, string StoreName, string? Hub, IReadOnlyList<RfidReader> Readers);

/// <summary>
/// RFID hubs that read their readers from the server instead of a database (docs/RFID.md, "A hub that reads its readers
/// from the server"). An administrator makes a key for a store; a hub that sends it reads that store's readers and
/// antennas, as the store's tills set them up and sent them. The key gives access to nothing else.
/// </summary>
public static partial class Endpoints
{
    private const string HubKeyMade = "RFID hub key made";
    private const string HubKeyRevoked = "RFID hub key revoked";

    private static void MapRfid(RouteGroupBuilder api)
    {
        api.MapGet("/stores/{id:guid}/rfid-hubs", async (Guid id, ApiRequest api) =>
        {
            await RequireStoreAsync(api, id);
            await using var db = api.Root.Create();
            var hubs = await db.RfidHubLinks.AsNoTracking().Where(h => h.StoreId == id).ToListAsync();
            return Results.Ok(hubs.OrderBy(h => h.RevokedAtUtc is not null).ThenBy(h => h.Name, StringComparer.CurrentCultureIgnoreCase).Select(HubInfo));
        }).RequireAuthorization(Need(Permissions.WebSignIn));

        api.MapPost("/stores/{id:guid}/rfid-hubs", async (Guid id, NewRfidHubRequest request, ApiRequest api) =>
        {
            await RequireStoreAsync(api, id);
            var name = string.IsNullOrWhiteSpace(request.Name) ? "RFID hub" : request.Name.Trim();
            if (name.Length > 40) throw ApiException.Bad("A hub name has at most 40 characters.");
            var key = RfidHubKeys.New();
            var hub = new RfidHubLink { StoreId = id, Name = name, KeyHash = RfidHubKeys.Hash(key), KeyHint = key[..8], CreatedBy = Cut(api.User.UserName, 40) };
            await using (var db = api.Root.Create())
            {
                db.RfidHubLinks.Add(hub);
                await db.SaveChangesAsync();
            }
            await new AuditService(api.For(id)).WriteAsync(HubKeyMade, $"{name} ({hub.KeyHint}…)", nameof(RfidHubLink), hub.Id);
            return Results.Created($"/api/rfid-hubs/{hub.Id}", new NewRfidHubResponse(HubInfo(hub), key));
        }).RequireAuthorization(Need(Permissions.StoresManage));

        api.MapPost("/rfid-hubs/{id:guid}/revoke", async (Guid id, ApiRequest api) =>
        {
            RfidHubLink? hub;
            bool revoked = false;
            await using (var db = api.Root.Create())
            {
                hub = await db.RfidHubLinks.SingleOrDefaultAsync(h => h.Id == id);
                if (hub is null || !api.User.CanSee(hub.StoreId)) throw ApiException.NotFound("That RFID hub");
                if (hub.RevokedAtUtc is null)
                {
                    // Its key stops working at its next request.
                    hub.RevokedAtUtc = SyncColumns.Now();
                    await db.SaveChangesAsync();
                    revoked = true;
                }
            }
            if (revoked) await new AuditService(api.For(hub.StoreId)).WriteAsync(HubKeyRevoked, $"{hub.Name} ({hub.KeyHint}…)", nameof(RfidHubLink), hub.Id);
            return Results.Ok(HubInfo(hub));
        }).RequireAuthorization(Need(Permissions.StoresManage));

        // A hub asks for its store's readers. It is signed in by its key alone, not by a user.
        api.MapGet("/rfid/readers", async (HttpContext http, DataContext data, CancellationToken cancellationToken) =>
        {
            var key = http.Request.Headers[RfidHubKeys.Header].ToString().Trim();
            if (key.Length is 0 or > 200) return Error(401, $"Send the RFID hub's key in the {RfidHubKeys.Header} header.");
            var hash = RfidHubKeys.Hash(key);
            await using var db = data.Create();
            var link = await db.RfidHubLinks.AsNoTracking().SingleOrDefaultAsync(h => h.KeyHash == hash && h.RevokedAtUtc == null, cancellationToken);
            if (link is null) return Error(401, "The server does not know this RFID hub key, or it was revoked.");
            var store = await db.Stores.AsNoTracking().SingleAsync(s => s.Id == link.StoreId, cancellationToken);
            var hub = http.Request.Query["hub"].FirstOrDefault() is { Length: > 0 } wanted ? wanted.Trim() : null;
            var readers = db.RfidReaders.AsNoTracking().Include(r => r.Antennas.OrderBy(a => a.Port)).Where(r => r.StoreId == link.StoreId);
            if (hub is not null) readers = readers.Where(r => r.HubKey == hub);
            var list = await readers.OrderBy(r => r.Name).ToListAsync(cancellationToken);
            // When and from where the hub last asked, for the list of hubs.
            var now = SyncColumns.Now();
            var address = http.Connection.RemoteIpAddress?.ToString();
            await db.RfidHubLinks.Where(h => h.Id == link.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(h => h.LastSeenAtUtc, now).SetProperty(h => h.LastAddress, address).Touched(), cancellationToken);
            return Results.Ok(new RfidHubReaders(store.Id, store.Code, store.Name, hub, list));
        }).AllowAnonymous();
    }

    private static RfidHubInfo HubInfo(RfidHubLink h) =>
        new(h.Id, h.StoreId, h.Name, h.KeyHint, ApiRequest.Utc(h.CreatedAtUtc), h.CreatedBy, ApiRequest.Utc(h.LastSeenAtUtc), h.LastAddress,
            h.RevokedAtUtc is not null, ApiRequest.Utc(h.RevokedAtUtc));
}

/// <summary>
/// RFID hub keys: <c>rfh_</c> and 43 characters of random base64url (256 bits). The server keeps their SHA-256: a key
/// that random cannot be guessed from its hash, and a plain hash lets the server find the key by an index.
/// </summary>
public static class RfidHubKeys
{
    /// <summary>The header a hub sends its key in.</summary>
    public const string Header = "X-RfidHub-Key";

    public static string New() => "rfh_" + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key.Trim())));
}
