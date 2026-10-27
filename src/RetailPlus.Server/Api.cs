using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Services;

namespace RetailPlus.Server;

/// <summary>A page of a long list: <c>{ items, total, page, pageSize }</c>.</summary>
public sealed record PageOf<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <summary>An error the web app shows as it is: <c>{ error }</c> with the status code.</summary>
public sealed class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;

    public static ApiException NotFound(string what = "That record") => new(404, $"{what} was not found.");
    public static ApiException Forbidden(string message = "You may not do this.") => new(403, message);
    public static ApiException Bad(string message) => new(400, message);
    public static ApiException Conflict(string message = "Someone else changed this record. Load it again and make your changes again.") => new(409, message);
}

/// <summary>
/// What one web request works on: the signed-in user, the stores they may see, the store the request asks for
/// (<c>storeId</c>), and a <see cref="DataContext"/> for it that names the user in the audit log.
/// </summary>
public sealed class ApiRequest(HttpContext http, DataContext root)
{
    /// <summary>Documents the server numbers itself (a client added on the web) come from this station's range, which no till can have (tills are 1–999).</summary>
    public const int ServerStation = 1000;

    public HttpContext Http { get; } = http;
    public UserScope User { get; } = new(http.User);
    public DataContext Root { get; } = root;

    /// <summary>The web user as the services see them: the audit log and the services that check permissions use it.</summary>
    public SessionInfo Session => new()
    {
        UserId = User.UserId, UserName = User.UserName, StationNumber = 0, AccessLevel = 4,
        Permissions = Http.User.FindAll(ServerClaims.Permission).Select(c => c.Value).ToHashSet(StringComparer.Ordinal),
    };

    /// <summary>The <c>storeId</c> of the query string, if any.</summary>
    public Guid? AskedStore => Guid.TryParse(Http.Request.Query["storeId"], out var id) ? id : null;

    /// <summary>
    /// The store a list covers: the one asked for (403 when the user may not see it), else every store for a user with
    /// <see cref="Permissions.StoresAll"/> (null), else the user's own store (an empty id for a user without one: nothing).
    /// </summary>
    public Guid? ListStore()
    {
        if (AskedStore is { } asked)
            return User.CanSee(asked) ? asked : throw ApiException.Forbidden("You may not see that store.");
        return User.AllStores ? null : User.StoreId ?? Guid.Empty;
    }

    /// <summary>Services working for one store (null: every store), reading and writing as the web user.</summary>
    public DataContext For(Guid? store) => Root.For(store is { } s ? new StationIdentity(s, 0) : null, Session);

    /// <summary>
    /// Services for a change: the audit log needs a store, so it is the record's store when it has one, else the store asked
    /// for, else the user's own store, else the company's first store.
    /// </summary>
    public async Task<DataContext> ForChangeAsync(Guid? recordStore = null)
    {
        var store = recordStore ?? AskedStore ?? User.StoreId ?? await FirstStoreAsync();
        return For(store);
    }

    public async Task<Guid?> FirstStoreAsync()
    {
        await using var db = Root.Create();
        return await db.Stores.AsNoTracking().OrderBy(s => s.Code).Select(s => (Guid?)s.Id).FirstOrDefaultAsync();
    }

    /// <summary>
    /// Company-wide records (clients, suppliers, departments, categories, commission rules, roles) are changed by users who
    /// see every store, or by any administrator while the company has one store.
    /// </summary>
    public async Task RequireCompanyChangeAsync()
    {
        if (User.AllStores) return;
        await using var db = Root.Create();
        if (await db.Stores.CountAsync() <= 1) return;
        throw ApiException.Forbidden("Records shared by every store are changed by a user who sees every store.");
    }

    /// <summary>A single record of another store is reported as not found, so ids do not tell what other stores have.</summary>
    public void RequireStore(Guid store, string what = "That record")
    {
        if (!User.CanSee(store)) throw ApiException.NotFound(what);
    }

    // Query string ----------------------------------------------------------------------------------------------------

    public string? Text(string name) => Http.Request.Query[name].FirstOrDefault() is { Length: > 0 } v ? v.Trim() : null;

    public Guid? Id(string name) => Guid.TryParse(Http.Request.Query[name], out var id) ? id : null;

    public int? Number(string name) => int.TryParse(Http.Request.Query[name], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    public bool? Bool(string name) => bool.TryParse(Http.Request.Query[name], out var b) ? b : null;

    /// <summary>
    /// <c>from</c> and <c>to</c> (yyyy-MM-dd, both inclusive), today in the store's time zone when left out; a range the
    /// wrong way round is turned.
    /// </summary>
    public async Task<(DateOnly From, DateOnly To)> PeriodAsync(Guid? store)
    {
        var today = await TodayAsync(store);
        var from = ParseDate("from") ?? ParseDate("to") ?? today;
        var to = ParseDate("to") ?? (ParseDate("from") is null ? today : from);
        return from <= to ? (from, to) : (to, from);
    }

    private DateOnly? ParseDate(string name) =>
        DateOnly.TryParseExact(Http.Request.Query[name], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d
        : Http.Request.Query[name].FirstOrDefault() is { Length: > 0 } bad ? throw ApiException.Bad($"{name} must be a date like 2026-10-28, not {bad}.") : null;

    /// <summary>Today where the store is (the server's own date across several stores).</summary>
    public async Task<DateOnly> TodayAsync(Guid? store) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, await ZoneAsync(store)));

    /// <summary>The period's business days (see <see cref="PeriodAsync"/>) as UTC times, end excluded, for rows kept in UTC.</summary>
    public async Task<(DateTime StartUtc, DateTime EndUtc)> UtcPeriodAsync(Guid? store)
    {
        var (from, to) = await PeriodAsync(store);
        var zone = await ZoneAsync(store);
        return (ToUtc(from.ToDateTime(TimeOnly.MinValue), zone), ToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue), zone));
    }

    /// <summary>The store's time zone (the server's own across several stores, or when the store has none).</summary>
    private async Task<TimeZoneInfo> ZoneAsync(Guid? store)
    {
        if (store is { } s && s != Guid.Empty)
        {
            await using var db = Root.Create();
            var zone = await db.Stores.AsNoTracking().Where(x => x.Id == s).Select(x => x.TimeZoneId).FirstOrDefaultAsync();
            if (zone is not null && TimeZoneInfo.TryFindSystemTimeZoneById(zone, out var tz)) return tz;
        }
        return TimeZoneInfo.Local;
    }

    private static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        // Midnight can fall in a daylight-saving gap; the day then starts an hour later.
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);
    }

    /// <summary>The page asked for: <c>page</c> (from 1) and <c>pageSize</c> (50 by default, at most 500).</summary>
    public (int Page, int Size) Paging() => (Math.Max(1, Number("page") ?? 1), Math.Clamp(Number("pageSize") ?? 50, 1, 500));


    public async Task<PageOf<TOut>> PageAsync<TIn, TOut>(IQueryable<TIn> rows, Func<TIn, TOut> map)
    {
        var (page, size) = Paging();
        int total = await rows.CountAsync();
        var items = await rows.Skip((page - 1) * size).Take(size).ToListAsync();
        return new PageOf<TOut>([.. items.Select(map)], total, page, size);

    }

    /// <summary>A version as the web app gets it back: the row's version token, or for rows without one their change time.</summary>
    public static string Version(Guid version) => version.ToString("N");

    public static string Version(DateTime updatedAtUtc) => updatedAtUtc.Ticks.ToString(CultureInfo.InvariantCulture);

    /// <summary>409 unless the version sent back is the record's current one.</summary>
    public static void CheckVersion(string? sent, string current)
    {
        if (string.IsNullOrWhiteSpace(sent)) throw ApiException.Bad("Send the version of the record you changed.");
        if (!string.Equals(sent, current, StringComparison.OrdinalIgnoreCase)) throw ApiException.Conflict();
    }

    public static Guid ParseVersion(string? sent) =>
        Guid.TryParse(sent, out var v) ? v : throw ApiException.Bad("Send the version of the record you changed.");

    public static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public static DateTime? Utc(DateTime? value) => value is { } v ? Utc(v) : null;
}

/// <summary>Turns the errors of the services into the API's <c>{ error }</c> answers.</summary>
public sealed class ApiErrors(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http)
    {
        try
        {
            await next(http);
        }
        catch (Exception ex) when (!http.Response.HasStarted && Map(ex) is { } answer)
        {
            http.Response.StatusCode = answer.Status;
            await http.Response.WriteAsJsonAsync(new { error = answer.Message });
        }
    }

    private static (int Status, string Message)? Map(Exception ex) => ex switch
    {
        ApiException api => (api.Status, api.Message),
        BusinessRuleException rule => (400, rule.Message),
        ConcurrencyConflictException conflict => (409, conflict.Message),
        DbUpdateConcurrencyException => (409, "Someone else changed this record. Load it again and make your changes again."),
        // A duplicate key or a missing reference the services did not check first.
        DbUpdateException => (400, "The change could not be saved: it repeats a record that must be unique, or names a record that does not exist."),
        BadHttpRequestException bad => (bad.StatusCode, bad.Message),
        _ => null,
    };
}

/// <summary>
/// The session cookie's guard against cross-site requests: a request that changes data, signed in by the
/// <c>rp_session</c> cookie (not by an Authorization header), must carry <c>X-Requested-With: retailplus-web</c>, which a
/// form on another site cannot send. Runs after authentication and routing; endpoints open to anyone (sign-in, sign-out)
/// are not signed-in requests and are let through.
/// </summary>
public sealed class CsrfGuard(RequestDelegate next)
{
    public const string Header = "X-Requested-With";
    public const string Value = "retailplus-web";

    public async Task InvokeAsync(HttpContext http)
    {
        var method = http.Request.Method;
        bool changes = HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsDelete(method) || HttpMethods.IsPatch(method);
        if (changes && http.Request.Cookies.ContainsKey(Session.Cookie) && string.IsNullOrEmpty(http.Request.Headers.Authorization)
            && http.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is null
            && !string.Equals(http.Request.Headers[Header], Value, StringComparison.Ordinal))
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            await http.Response.WriteAsJsonAsync(new { error = "The request did not come from the SMA RETAIL web app." });
            return;
        }
        await next(http);
    }
}

/// <summary>The sign-in cookie.</summary>
public static class Session
{
    public const string Cookie = "rp_session";

    public static CookieOptions Options(IWebHostEnvironment environment, DateTime? expiresUtc) => new()
    {
        HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = !environment.IsDevelopment(), Path = "/", Expires = expiresUtc,
        IsEssential = true,
    };
}
