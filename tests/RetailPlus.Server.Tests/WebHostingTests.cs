using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using RetailPlus.Sync.Tests;

namespace RetailPlus.Server.Tests;

/// <summary>
/// What the web app relies on besides the data: the session cookie, the guard against cross-site requests, and the
/// server serving the app's static export next to the API.
/// </summary>
public sealed class WebHostingTests(SyncWorld world) : IClassFixture<SyncWorld>
{
    private void Require() => Skip.IfNot(world.Available, world.UnavailableReason);

    /// <summary>A client that keeps cookies and never sends a token, as the browser does.</summary>
    private HttpClient Browser(WebApplicationFactory<Program>? server = null) =>
        (server ?? world.Server).CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });

    private static HttpRequestMessage FromTheApp(HttpMethod method, string url, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Add(CsrfGuard.Header, CsrfGuard.Value);
        return request;
    }

    [SkippableFact]
    public async Task Signing_in_sets_the_session_cookie_that_signs_in_the_next_requests()
    {
        Require();
        using var browser = Browser();
        using var signIn = await browser.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(SyncWorld.AdminUser, SyncWorld.AdminPassword));
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        var cookie = Assert.Single(signIn.Headers.GetValues("Set-Cookie"), c => c.StartsWith(Session.Cookie + "=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        // The tests run as Development, where the cookie may travel over plain http.
        Assert.DoesNotContain("; secure", cookie, StringComparison.OrdinalIgnoreCase);


        var me = await (await browser.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<UserInfo>(SyncWorld.Json);
        Assert.Equal(SyncWorld.AdminUser, me!.UserName);

        using var signOut = await browser.SendAsync(FromTheApp(HttpMethod.Post, "/api/auth/sign-out"));
        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        Assert.Contains(signOut.Headers.GetValues("Set-Cookie"), c => c.StartsWith(Session.Cookie + "=;", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/auth/me")).StatusCode);
    }

    [SkippableFact]
    public async Task A_change_signed_in_by_the_cookie_needs_the_web_apps_header()
    {
        Require();
        using var browser = Browser();
        (await browser.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(SyncWorld.AdminUser, SyncWorld.AdminPassword))).EnsureSuccessStatusCode();
        var code = "W" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        // A form posted from another site carries the cookie but not the header.
        using var forged = await browser.PostAsJsonAsync("/api/stores", new StoreRequest(code, "Forged", null));
        Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);
        Assert.Contains("error", await forged.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.DeleteAsync($"/api/users/{Guid.NewGuid()}")).StatusCode);

        // Reading needs no header; the web app's own request goes through.
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/stores")).StatusCode);
        using var made = await browser.SendAsync(FromTheApp(HttpMethod.Post, "/api/stores", JsonContent.Create(new StoreRequest(code, "From the app", null))));
        Assert.Equal(HttpStatusCode.Created, made.StatusCode);

        // A request with a token in the Authorization header is not a browser's: no header needed.
        using var tokenClient = await world.SignInAsync();
        using var withToken = await tokenClient.PutAsJsonAsync($"/api/stores/{(await made.Content.ReadFromJsonAsync<StoreInfo>(SyncWorld.Json))!.Id}",
            new StoreRequest(code, "Renamed", null));
        Assert.Equal(HttpStatusCode.OK, withToken.StatusCode);
    }

    [SkippableFact]
    public async Task The_server_serves_the_web_apps_files_but_never_for_the_api()
    {
        Require();
        var root = Path.Combine(world.Folder, "web-" + Guid.NewGuid().ToString("N")[..6]);
        void Page(string path, string text)
        {
            var file = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }
        Page("index.html", "<html>dashboard</html>");
        Page("sales/view/index.html", "<html>sale detail</html>");
        Page("clients.html", "<html>clients</html>");
        Page("404.html", "<html>not here</html>");
        Page("_next/static/chunks/app-1234.js", "console.log('app')");
        Page("api/health", "{\"status\":\"a file\"}");

        await using var server = world.Server.WithWebHostBuilder(b => b.UseSetting("Server:WebRoot", root));
        using var browser = Browser(server);

        using (var home = await browser.GetAsync("/"))
        {
            Assert.Equal(HttpStatusCode.OK, home.StatusCode);
            Assert.Equal("<html>dashboard</html>", await home.Content.ReadAsStringAsync());
            Assert.Equal("text/html", home.Content.Headers.ContentType!.MediaType);
            Assert.True(home.Headers.CacheControl!.NoCache);
        }
        using (var detail = await browser.GetAsync("/sales/view/?id=6f1c2c4e-0000-0000-0000-000000000000"))
            Assert.Equal("<html>sale detail</html>", await detail.Content.ReadAsStringAsync());
        using (var withoutSlash = await browser.GetAsync("/clients"))
            Assert.Equal("<html>clients</html>", await withoutSlash.Content.ReadAsStringAsync());
        using (var script = await browser.GetAsync("/_next/static/chunks/app-1234.js"))
        {
            Assert.Equal(HttpStatusCode.OK, script.StatusCode);
            Assert.Equal(TimeSpan.FromDays(365), script.Headers.CacheControl!.MaxAge);
            Assert.Contains("immutable", script.Headers.CacheControl.ToString(), StringComparison.Ordinal);
        }
        using (var missing = await browser.GetAsync("/no/such/page"))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Equal("<html>not here</html>", await missing.Content.ReadAsStringAsync());
        }

        // /api is the API's, even where the export has a file of that name.
        using (var health = await browser.GetAsync("/api/health"))
            Assert.Contains("\"database\"", await health.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using (var unknown = await browser.GetAsync("/api/no-such-endpoint"))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            Assert.Equal("application/json", unknown.Content.Headers.ContentType!.MediaType);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/stores")).StatusCode);
    }

    [SkippableFact]
    public async Task Without_the_web_apps_files_the_server_serves_the_api_only()
    {
        Require();
        await using var server = world.Server.WithWebHostBuilder(b => b.UseSetting("Server:WebRoot", Path.Combine(world.Folder, "not-built-yet")));
        using var browser = Browser(server);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/health")).StatusCode);
        using var unknown = await browser.GetAsync("/api/no-such-endpoint");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Contains("\"error\"", await unknown.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_export_is_looked_for_above_the_content_root()
    {
        var top = Path.Combine(Path.GetTempPath(), "RetailPlus_WebRoot_" + Guid.NewGuid().ToString("N")[..8]);
        var content = Path.Combine(top, "src", "RetailPlus.Server");
        Directory.CreateDirectory(content);
        try
        {
            Assert.Null(WebApp.FindRoot(new ServerOptions(), content));
            var export = Directory.CreateDirectory(Path.Combine(top, "web", "out")).FullName;
            Assert.Equal(export, WebApp.FindRoot(new ServerOptions(), content));
            // A configured folder wins, relative to the content root; one that is not there means none.
            var mine = Directory.CreateDirectory(Path.Combine(content, "site")).FullName;
            Assert.Equal(mine, WebApp.FindRoot(new ServerOptions { WebRoot = "site" }, content));
            Assert.Null(WebApp.FindRoot(new ServerOptions { WebRoot = "elsewhere" }, content));
        }
        finally
        {
            Directory.Delete(top, true);
        }
    }
}
