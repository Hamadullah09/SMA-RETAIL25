using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Services;
using RetailPlus.Sync;

namespace RetailPlus.Server;

public sealed record SignInRequest(string UserName, string Password);
/// <summary>The signed-in user. <c>CanChangeShared</c>: may change the records every store shares (see <see cref="ApiRequest.RequireCompanyChangeAsync"/>).</summary>
public sealed record UserInfo(Guid Id, string UserName, string? FullName, string? Role, IReadOnlyList<string> Permissions, Guid? StoreId, string? StoreName, bool MustChangePassword,
    bool CanChangeShared = false);
public sealed record SignInResponse(string Token, DateTime ExpiresAtUtc, UserInfo User);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record StoreInfo(Guid Id, string Code, string Name, string? TimeZoneId, string? CurrencySymbol = null);
public sealed record LocationInfo(Guid Id, string Code, string? Name);
public sealed record StaffInfo(Guid Id, string UserName, string? FullName);
public sealed record StoreRequest(string Code, string Name, string? TimeZoneId);
public sealed record BatchInfo(Guid Id, Guid BatchId, Guid? StoreId, int StationNumber, SyncDirection Direction, SyncBatchStatus Status, string? FileName,
    int Rows, DateTime ReceivedAtUtc, DateTime? AppliedAtUtc, string? AppVersion, string? Error, string? Notes);
public sealed record StationInfo(Guid Id, Guid StoreId, int Number, string? Name, bool Enrolled, DateTime? EnrolledAtUtc, bool Retired,
    DateTime? EnrollmentExpiresAtUtc, DateTime? LastUploadAtUtc, DateTime? LastDownloadAtUtc, string? AppVersion, BatchInfo? LastBatch, int FailedBatches, bool Behind);
public sealed record NewStationRequest(int? Number, string? Name);
public sealed record EnrollmentCodeInfo(StationInfo Station, string Code, DateTime ExpiresAtUtc);

/// <summary>The server's HTTP API (docs/API.md), one file per area.</summary>
public static partial class Endpoints
{
    private static IResult Error(int status, string message) => Results.Json(new { error = message }, statusCode: status);

    private static string Need(string permission) => ServerClaims.Policy(permission);

    public static void MapRetailPlusApi(this WebApplication app)
    {
        app.MapGet("/api/health", HealthAsync).AllowAnonymous().WithTags("Health");
        MapAuth(app.MapGroup("/api/auth").WithTags("Sign-in"));
        MapStores(app.MapGroup("/api").WithTags("Stores and tills"));
        MapSync(app.MapGroup("/api/sync").WithTags("Sync"));
        MapDashboard(app.MapGroup("/api").WithTags("Dashboard and tills"));
        MapSales(app.MapGroup("/api").WithTags("Sales"));
        MapCatalog(app.MapGroup("/api").WithTags("Items, clients and suppliers"));
        MapStock(app.MapGroup("/api").WithTags("Stock and gift cards"));
        MapReports(app.MapGroup("/api").WithTags("Reports"));
        MapUsers(app.MapGroup("/api").WithTags("Users and roles"));
        MapStaff(app.MapGroup("/api").WithTags("Audit and staff"));
        MapSettings(app.MapGroup("/api").WithTags("Store settings"));
        MapRfid(app.MapGroup("/api").WithTags("RFID hubs"));
    }

    private static async Task<IResult> HealthAsync(DataContext data, CancellationToken cancellationToken)
    {
        bool database;
        try
        {
            await using var db = data.Create();
            database = await db.Database.CanConnectAsync(cancellationToken);
        }
        catch (Microsoft.Data.SqlClient.SqlException)
        {
            database = false;
        }
        return Results.Json(new { status = database ? "ok" : "degraded", database = database ? "ok" : "unreachable", version = ServerSync.AppVersion },
            statusCode: database ? 200 : 503);
    }

    // Sign-in ----------------------------------------------------------------------------------------------------------

    private static void MapAuth(RouteGroupBuilder auth)
    {
        auth.MapPost("/sign-in", async (SignInRequest request, HttpContext http, DataContext data, TokenService tokens, IWebHostEnvironment environment) =>
        {
            // The sign-in goes to the audit log like one at a till, under the user's store (or the first store), station 0.
            var name = (request.UserName ?? "").Trim();
            Guid? store;
            await using (var db = data.Create())
                store = await db.UserAccounts.Where(u => u.UserName == name).Select(u => u.StoreId).FirstOrDefaultAsync()
                        ?? await db.Stores.OrderBy(s => s.Code).Select(s => (Guid?)s.Id).FirstOrDefaultAsync();
            var audited = data.For(store is { } s ? new StationIdentity(s, 0) : null, null);
            var user = (await new SetupService(audited).SignInAsync(name, request.Password ?? "")).User;
            // Users without a password yet (brought in from Retail Plus) choose one at a till first.
            if (user is null || user.PasswordHash is null) return Error(401, "Wrong user name or password, or the account is locked for a few minutes.");
            var permissions = SetupService.PermissionsOf(user);
            if (!permissions.Contains(Permissions.WebSignIn)) return Error(403, "This user may not sign in to the web back office.");
            var (token, expires) = tokens.Create(user, permissions);
            http.Response.Cookies.Append(Session.Cookie, token, Session.Options(environment, expires));
            return Results.Ok(new SignInResponse(token, expires, (await UserInfoAsync(data, user.Id))!));
        }).AllowAnonymous();

        auth.MapPost("/sign-out", (HttpContext http, IWebHostEnvironment environment) =>
        {
            http.Response.Cookies.Delete(Session.Cookie, Session.Options(environment, null));
            return Results.NoContent();
        }).AllowAnonymous();

        auth.MapGet("/me", async (HttpContext http, DataContext data) =>
            await UserInfoAsync(data, new UserScope(http.User).UserId) is { } info ? Results.Ok(info) : Error(401, "Sign in again."))
            .RequireAuthorization(Need(Permissions.WebSignIn));

        auth.MapPost("/change-password", async (ChangePasswordRequest request, ApiRequest api) =>
        {
            if (string.IsNullOrEmpty(request.NewPassword) || request.NewPassword.Length < 8) return Error(400, "A password has at least 8 characters.");
            var id = api.User.UserId;
            Guid? home;
            await using (var read = api.Root.Create())
                home = await read.UserAccounts.Where(u => u.Id == id).Select(u => u.StoreId).FirstOrDefaultAsync();
            // Saved as a change for the user's store, so it reaches the audit log (which shows only that the password changed).
            await using var db = (await api.ForChangeAsync(home)).Create();
            var user = await db.UserAccounts.SingleOrDefaultAsync(u => u.Id == id);
            if (user?.PasswordHash is null || !SetupService.VerifyPassword(request.CurrentPassword ?? "", user.PasswordHash))
                return Error(400, "The current password is not right.");
            user.PasswordHash = SetupService.HashPassword(request.NewPassword);
            user.MustChangePassword = false;
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.WebSignIn));
    }

    private static async Task<UserInfo?> UserInfoAsync(DataContext data, Guid userId)
    {
        await using var db = data.Create();
        var user = await db.UserAccounts.AsNoTracking().Include(u => u.Role).ThenInclude(r => r!.Permissions).SingleOrDefaultAsync(u => u.Id == userId);
        if (user is null) return null;
        var store = user.StoreId is { } id ? await db.Stores.AsNoTracking().Where(s => s.Id == id).Select(s => s.Name).FirstOrDefaultAsync() : null;
        var permissions = SetupService.PermissionsOf(user);
        bool shared = permissions.Contains(Permissions.StoresAll) || await db.Stores.CountAsync() <= 1;
        return new UserInfo(user.Id, user.UserName, user.FullName, user.Role?.Name, [.. permissions.Order(StringComparer.Ordinal)],
            user.StoreId, store, user.MustChangePassword, shared);
    }

    // Stores and tills ------------------------------------------------------------------------------------------------

    private static void MapStores(RouteGroupBuilder api)
    {
        api.MapGet("/stores", async (HttpContext http, DataContext data) =>
        {
            await using var db = data.Create();
            var stores = await new UserScope(http.User).Visible(db.Stores.AsNoTracking()).OrderBy(s => s.Code).ToListAsync();
            var ids = stores.Select(s => s.Id).ToList();
            var symbols = await db.StoreSettingExtras.AsNoTracking().Where(x => x.Name == CurrencyExtra && ids.Contains(x.StoreId))
                .ToDictionaryAsync(x => x.StoreId, x => x.Value);
            return Results.Ok(stores.Select(s => new StoreInfo(s.Id, s.Code, s.Name, s.TimeZoneId, symbols.GetValueOrDefault(s.Id))));
        }).RequireAuthorization(Need(Permissions.WebSignIn));

        api.MapGet("/stores/{id:guid}/locations", async (Guid id, ApiRequest api) =>
        {
            api.RequireStore(id, "That store");
            await using var db = api.Root.Create();
            return Results.Ok(await db.Locations.AsNoTracking().Where(l => l.StoreId == id).OrderBy(l => l.Code)
                .Select(l => new LocationInfo(l.Id, l.Code, l.Name)).ToListAsync());
        }).RequireAuthorization(Need(Permissions.WebSignIn));

        // The people a list can be filtered by: the store's users and those who work at every store.
        api.MapGet("/staff", async (ApiRequest api) =>
        {
            if (!new[] { Permissions.ReportsView, Permissions.ReportsStaff, Permissions.AuditView, Permissions.SetupUsers }.Any(api.User.Can))
                throw ApiException.Forbidden("Your role cannot see the staff list.");
            var store = api.ListStore();
            await using var db = api.Root.Create();
            var users = db.UserAccounts.AsNoTracking();
            if (store is { } s) users = users.Where(u => u.StoreId == s || u.StoreId == null);
            return Results.Ok(await users.OrderBy(u => u.UserName).Select(u => new StaffInfo(u.Id, u.UserName, u.FullName)).ToListAsync());
        }).RequireAuthorization(Need(Permissions.WebSignIn));

        api.MapPost("/stores", async (StoreRequest request, HttpContext http, DataContext data) =>
        {
            if (!new UserScope(http.User).AllStores) return Error(403, "Only a super administrator adds stores.");
            if (Validate(request) is { } problem) return Error(400, problem);
            await using var db = data.Create();
            var code = request.Code.Trim().ToUpperInvariant();
            if (await db.Stores.AnyAsync(s => s.Code == code)) return Error(409, $"Store code {code} is taken.");
            // A new store starts with its settings row and the POS location its tills sell from.
            var store = new Store { Code = code, Name = request.Name.Trim(), TimeZoneId = request.TimeZoneId };
            db.Stores.Add(store);
            db.StoreSettings.Add(new StoreSettings { StoreId = store.Id, CompanyName = Cut(store.Name, 30), Tax1Name = "Tax 1", Tax2Name = "Tax 2", MinimumTender = 0.01m, GracePeriodDays = 30 });
            db.Locations.Add(new Location { StoreId = store.Id, Code = "POS", Name = Cut(store.Name, 50) });
            await db.SaveChangesAsync();
            return Results.Created($"/api/stores/{store.Id}", new StoreInfo(store.Id, store.Code, store.Name, store.TimeZoneId));
        }).RequireAuthorization(Need(Permissions.StoresManage));

        api.MapPut("/stores/{id:guid}", async (Guid id, StoreRequest request, HttpContext http, DataContext data) =>
        {
            if (!new UserScope(http.User).CanSee(id)) return Error(404, "No such store.");
            if (Validate(request) is { } problem) return Error(400, problem);
            await using var db = data.Create();
            var store = await db.Stores.SingleOrDefaultAsync(s => s.Id == id);
            if (store is null) return Error(404, "No such store.");
            var code = request.Code.Trim().ToUpperInvariant();
            if (await db.Stores.AnyAsync(s => s.Code == code && s.Id != id)) return Error(409, $"Store code {code} is taken.");
            store.Code = code;
            store.Name = request.Name.Trim();
            store.TimeZoneId = request.TimeZoneId;
            await db.SaveChangesAsync();
            return Results.Ok(new StoreInfo(store.Id, store.Code, store.Name, store.TimeZoneId));
        }).RequireAuthorization(Need(Permissions.StoresManage));

        api.MapGet("/stores/{id:guid}/stations", async (Guid id, HttpContext http, DataContext data, IOptions<ServerOptions> options) =>
        {
            if (!new UserScope(http.User).CanSee(id)) return Error(404, "No such store.");
            await using var db = data.Create();
            if (!await db.Stores.AnyAsync(s => s.Id == id)) return Error(404, "No such store.");
            var stations = await db.Stations.AsNoTracking().Where(s => s.StoreId == id).OrderBy(s => s.Number).ToListAsync();
            var list = new List<StationInfo>();
            foreach (var s in stations) list.Add(await StationInfoAsync(db, s, options.Value));
            return Results.Ok(list);
        }).RequireAuthorization(Need(Permissions.WebSignIn));

        api.MapPost("/stores/{id:guid}/stations", async (Guid id, NewStationRequest request, HttpContext http, DataContext data, IOptions<ServerOptions> options) =>
        {
            if (!new UserScope(http.User).CanSee(id)) return Error(404, "No such store.");
            await using var db = data.Create();
            if (!await db.Stores.AnyAsync(s => s.Id == id)) return Error(404, "No such store.");
            int number = request.Number ?? (await db.Stations.MaxAsync(s => (int?)s.Number) ?? 0) + 1;
            if (number is < 1 or > 999) return Error(400, "Station numbers are 1 to 999.");
            if (await db.Stations.AnyAsync(s => s.Number == number)) return Error(409, $"Station {number} exists already; station numbers are unique across the company.");
            var station = new Station { StoreId = id, Number = number, Name = string.IsNullOrWhiteSpace(request.Name) ? $"Station {number:000}" : request.Name.Trim() };
            db.Stations.Add(station);
            var code = StationKeys.NewEnrollmentCode();
            var link = new StationLink { StationId = station.Id, StoreId = id, StationNumber = number };
            Arm(link, code);
            db.StationLinks.Add(link);
            await db.SaveChangesAsync();
            return Results.Created($"/api/stations/{station.Id}", new EnrollmentCodeInfo(await StationInfoAsync(db, station, options.Value), code, ApiRequest.Utc(link.EnrollmentExpiresAtUtc!.Value)));
        }).RequireAuthorization(Need(Permissions.StoresManage));

        api.MapPost("/stations/{id:guid}/enrollment-code", async (Guid id, HttpContext http, DataContext data, IOptions<ServerOptions> options) =>
        {
            await using var db = data.Create();
            var station = await db.Stations.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id);
            if (station is null || !new UserScope(http.User).CanSee(station.StoreId)) return Error(404, "No such till.");
            var link = await db.StationLinks.SingleOrDefaultAsync(l => l.StationId == id);
            if (link is null) db.StationLinks.Add(link = new StationLink { StationId = id, StoreId = station.StoreId, StationNumber = station.Number });
            if (link.RetiredAtUtc is not null) return Error(409, "This till was retired.");
            var code = StationKeys.NewEnrollmentCode();
            Arm(link, code);
            await db.SaveChangesAsync();
            return Results.Ok(new EnrollmentCodeInfo(await StationInfoAsync(db, station, options.Value), code, ApiRequest.Utc(link.EnrollmentExpiresAtUtc!.Value)));

        }).RequireAuthorization(Need(Permissions.StoresManage));

        api.MapPost("/stations/{id:guid}/retire", async (Guid id, HttpContext http, DataContext data, IOptions<ServerOptions> options) =>
        {
            await using var db = data.Create();
            var station = await db.Stations.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id);
            if (station is null || !new UserScope(http.User).CanSee(station.StoreId)) return Error(404, "No such till.");
            var link = await db.StationLinks.SingleOrDefaultAsync(l => l.StationId == id);
            if (link is null) db.StationLinks.Add(link = new StationLink { StationId = id, StoreId = station.StoreId, StationNumber = station.Number });
            // Its key stops working at once; its data stays.
            link.RetiredAtUtc = SyncColumns.Now();
            link.ProtectedKey = null;
            link.EnrollmentCodeHash = null;
            link.EnrollmentExpiresAtUtc = null;
            await db.SaveChangesAsync();
            return Results.Ok(await StationInfoAsync(db, station, options.Value));
        }).RequireAuthorization(Need(Permissions.StoresManage));
    }

    private static string? Validate(StoreRequest request) =>
        string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 10 ? "A store code has 1 to 10 characters."
        : string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 50 ? "A store name has 1 to 50 characters."
        : request.TimeZoneId is { Length: > 0 } zone && !TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _) ? $"{zone} is not a time zone."
        : null;

    private static string Cut(string text, int length) => text.Length <= length ? text : text[..length];

    /// <summary>A new one-time enrolment code for the till, valid for 24 hours.</summary>
    private static void Arm(StationLink link, string code)
    {
        link.EnrollmentCodeHash = StationKeys.HashCode(code);
        link.EnrollmentExpiresAtUtc = SyncColumns.Now().AddHours(24);
    }

    private static async Task<StationInfo> StationInfoAsync(RetailPlusDbContext db, Station station, ServerOptions options)
    {
        var link = await db.StationLinks.AsNoTracking().SingleOrDefaultAsync(l => l.StationId == station.Id);
        var batches = db.SyncBatches.AsNoTracking().Where(b => b.StationNumber == station.Number && b.StoreId == station.StoreId);
        var last = await batches.Where(b => b.Direction == SyncDirection.Upload).OrderByDescending(b => b.ReceivedAtUtc).FirstOrDefaultAsync();
        var lastApplied = await batches.Where(b => b.Direction == SyncDirection.Upload && b.Status == SyncBatchStatus.Applied).MaxAsync(b => (DateTime?)b.ReceivedAtUtc);
        // Failures since the last package that went in: what still needs looking at.
        int failed = await batches.CountAsync(b => b.Direction == SyncDirection.Upload && b.Status == SyncBatchStatus.Failed && (lastApplied == null || b.ReceivedAtUtc > lastApplied));
        bool enrolled = link?.EnrolledAtUtc is not null && link.ProtectedKey is not null;
        bool retired = link?.RetiredAtUtc is not null;
        var since = link?.LastUploadAtUtc ?? link?.EnrolledAtUtc;
        bool behind = enrolled && !retired && since < DateTime.UtcNow.AddHours(-options.BehindAfterHours);
        return new StationInfo(station.Id, station.StoreId, station.Number, station.Name, enrolled, ApiRequest.Utc(link?.EnrolledAtUtc), retired,
            link?.EnrollmentCodeHash is null ? null : ApiRequest.Utc(link.EnrollmentExpiresAtUtc), ApiRequest.Utc(link?.LastUploadAtUtc),
            ApiRequest.Utc(link?.LastDownloadAtUtc), link?.AppVersion,
            last is null ? null : ToInfo(last), failed, behind);
    }

    private static BatchInfo ToInfo(SyncBatch b) =>
        new(b.Id, b.BatchId, b.StoreId, b.StationNumber, b.Direction, b.Status, b.FileName, b.Rows, ApiRequest.Utc(b.ReceivedAtUtc), ApiRequest.Utc(b.AppliedAtUtc),
            b.AppVersion, b.Error, b.Notes);

    // Sync -------------------------------------------------------------------------------------------------------------

    private static void MapSync(RouteGroupBuilder sync)
    {
        sync.MapPost("/enroll", async (EnrollRequest request, ServerSync server, CancellationToken cancellationToken) =>
            await server.EnrollAsync(request, cancellationToken) is { } joined ? Results.Ok(joined)
                : Error(404, "The enrolment code is not valid, was used already or has expired. Ask for a new one."))
            .AllowAnonymous();

        // A till sends a package, signed with its key (the request and the package).
        sync.MapPost("/upload", async (HttpContext http, ServerSync server, IOptions<ServerOptions> options, CancellationToken cancellationToken) =>
        {
            var (file, sha) = await ReceiveAsync(http, server, options.Value, http.Request.Body, cancellationToken);
            if (file is null) return Error(413, sha);
            try
            {
                var station = await server.AuthenticateAsync(http.Request, sha, cancellationToken);
                if (station is null) return await RefuseStationAsync(http, file, sha, cancellationToken);
                var result = await server.ApplyUploadAsync(station.Link, station.Key, file, sha, http.Request.Headers["X-File-Name"].FirstOrDefault() ?? "upload", cancellationToken);
                return result.Status == nameof(SyncBatchStatus.Failed) ? Results.Json(result, statusCode: 422) : Results.Ok(result);
            }
            finally
            {
                File.Delete(file);
            }
        }).AllowAnonymous();

        // A till asks for what changed on the server since its cursor.
        sync.MapGet("/download", async (HttpContext http, long? cursor, ServerSync server, ServerFolders folders, CancellationToken cancellationToken) =>
        {
            var station = await server.AuthenticateAsync(http.Request, StationAuth.EmptyBodySha256, cancellationToken);
            if (station is null) return Error(401, "This till is not known here, was retired, or signed the request with another key.");
            return await PackageFileAsync(server, folders, station.Link, station.Key, cursor ?? 0, fromTill: true, cancellationToken);
        }).AllowAnonymous();

        // A package from a till brought on a USB stick, uploaded by a signed-in administrator. It is still checked with the till's key.
        sync.MapPost("/upload-file", async (HttpContext http, ServerSync server, IOptions<ServerOptions> options, CancellationToken cancellationToken) =>
        {
            Stream body = http.Request.Body;
            string name = http.Request.Headers["X-File-Name"].FirstOrDefault() ?? "upload" + SyncPackage.Extension;
            if (http.Request.HasFormContentType)
            {
                var form = await http.Request.ReadFormAsync(cancellationToken);
                if (form.Files.Count == 0) return Error(400, "Choose a package file.");
                var upload = form.Files[0];
                body = upload.OpenReadStream();
                name = upload.FileName;
            }
            var (file, sha) = await ReceiveAsync(http, server, options.Value, body, cancellationToken);
            if (file is null) return Error(413, sha);
            try
            {
                PackageManifest manifest;
                try
                {
                    await using var stream = File.OpenRead(file);
                    using var package = PackageReader.Open(stream);
                    manifest = package.Manifest;
                }
                catch (PackageRejectedException ex)
                {
                    return Error(422, ex.Message);
                }
                var link = await server.LinkAsync(manifest.StationNumber, cancellationToken);
                if (link is null || server.KeyOf(link) is not { } key) return Error(404, $"Station {manifest.StationNumber} has not joined this server, or was retired.");
                if (!new UserScope(http.User).CanSee(link.StoreId)) return Error(403, "The package belongs to a till of another store.");
                var result = await server.ApplyUploadAsync(link, key, file, sha, name, cancellationToken);
                return result.Status == nameof(SyncBatchStatus.Failed) ? Results.Json(result, statusCode: 422) : Results.Ok(result);
            }
            finally
            {
                File.Delete(file);
            }
        }).RequireAuthorization(Need(Permissions.SyncSend)).DisableAntiforgery();

        // A package for a till that has no connection, to take there on a USB stick.
        sync.MapGet("/download-file", async (int station, long? cursor, HttpContext http, ServerSync server, ServerFolders folders, CancellationToken cancellationToken) =>
        {
            var link = await server.LinkAsync(station, cancellationToken);
            if (link is null || server.KeyOf(link) is not { } key) return Error(404, $"Station {station} has not joined this server, or was retired.");
            if (!new UserScope(http.User).CanSee(link.StoreId)) return Error(404, $"Station {station} has not joined this server, or was retired.");
            return await PackageFileAsync(server, folders, link, key, cursor ?? link.DownloadCursor, fromTill: false, cancellationToken);
        }).RequireAuthorization(Need(Permissions.SyncSend));

        sync.MapGet("/batches", async (Guid? storeId, int? station, SyncBatchStatus? status, int? take, HttpContext http, DataContext data) =>
        {
            var scope = new UserScope(http.User);
            if (storeId is { } wanted && !scope.CanSee(wanted)) return Error(404, "No such store.");
            await using var db = data.Create();
            var q = db.SyncBatches.AsNoTracking();
            if (!scope.AllStores) q = q.Where(b => b.StoreId == scope.StoreId);
            if (storeId is { } s) q = q.Where(b => b.StoreId == s);
            if (station is { } n) q = q.Where(b => b.StationNumber == n);
            if (status is { } st) q = q.Where(b => b.Status == st);
            var rows = await q.OrderByDescending(b => b.ReceivedAtUtc).Take(Math.Clamp(take ?? 200, 1, 1000)).ToListAsync();
            return Results.Ok(rows.Select(ToInfo));
        }).RequireAuthorization(Need(Permissions.WebSignIn));
    }

    /// <summary>Takes the request body into a file; (null, reason) when it is too large.</summary>
    private static async Task<(string? File, string Sha)> ReceiveAsync(HttpContext http, ServerSync server, ServerOptions options, Stream body, CancellationToken cancellationToken)
    {
        long limit = options.MaxPackageMegabytes * 1024L * 1024L;
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size) size.MaxRequestBodySize = limit;
        try
        {
            return await server.ReceiveAsync(body, limit, cancellationToken);
        }
        catch (PackageRejectedException ex)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>
    /// A request naming a till but not signed with its key: refused, and recorded against that till when it exists, so a
    /// till with an old key (or someone else) shows up in its history.
    /// </summary>
    private static async Task<IResult> RefuseStationAsync(HttpContext http, string file, string sha, CancellationToken cancellationToken)
    {
        const string message = "This till is not known here, was retired, or signed the package with another key.";
        if (int.TryParse(http.Request.Headers[StationAuth.StationHeader], out var number))
        {
            var data = http.RequestServices.GetRequiredService<DataContext>();
            await using var db = data.Create();
            if (await db.Stations.AsNoTracking().SingleOrDefaultAsync(s => s.Number == number, cancellationToken) is { } station)
            {
                Guid batch = Guid.Empty;
                try
                {
                    await using var stream = File.OpenRead(file);
                    using var package = PackageReader.Open(stream);
                    batch = package.Manifest.BatchId;
                }
                catch (PackageRejectedException)
                {
                }
                db.SyncBatches.Add(new SyncBatch
                {
                    BatchId = batch, StoreId = station.StoreId, StationNumber = number, Direction = SyncDirection.Upload, FileSha256 = sha,
                    ReceivedAtUtc = SyncColumns.Now(), Status = SyncBatchStatus.Failed, Error = "The request was not signed with this till's key.",
                });
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        return Error(401, message);
    }

    /// <summary>Writes a till's package to a temporary file and sends it; the file goes when the response is done.</summary>
    private static async Task<IResult> PackageFileAsync(ServerSync server, ServerFolders folders, StationLink link, byte[] key, long cursor, bool fromTill, CancellationToken cancellationToken)
    {
        var file = Path.Combine(folders.Incoming, "out-" + Guid.NewGuid().ToString("N") + SyncPackage.Extension);
        PackageManifest manifest;
        await using (var output = File.Create(file))
            manifest = await server.WriteDownloadAsync(link, key, cursor, output, fromTill, cancellationToken);
        var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        return Results.File(stream, "application/octet-stream", SyncPackage.FileName(manifest));
    }
}
