using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace RetailPlus.Server;

/// <summary>
/// The web back office's static export (Next.js <c>output: "export"</c>), served by the server next to the API: the pages
/// take their record as <c>?id=</c>, so every page is a file. <c>/api</c> is never answered from the files.
/// </summary>
public static class WebApp
{
    private const string ImmutableCache = "public, max-age=31536000, immutable";

    /// <summary>The folder to serve, or null when there is none (the server then serves only the API).</summary>
    public static string? FindRoot(ServerOptions options, string contentRoot)
    {
        if (!string.IsNullOrWhiteSpace(options.WebRoot))
        {
            var configured = Path.GetFullPath(options.WebRoot, contentRoot);
            return Directory.Exists(configured) ? configured : null;
        }
        for (var folder = new DirectoryInfo(contentRoot); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "web", "out");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>
    /// Serves the files (before routing, so a page is never taken for an endpoint) and, at the end, the export's
    /// <c>404.html</c> for any other path outside <c>/api</c>; an unknown <c>/api</c> address is a JSON 404 either way.
    /// </summary>
    public static void UseWebApp(this WebApplication app)
    {
        // An address under /api that nothing answers is the API's 404, in its { error } shape.
        app.MapFallback("/api/{**path}", async http =>
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            await http.Response.WriteAsJsonAsync(new { error = "There is no such address in the API." });
        }).AllowAnonymous().ExcludeFromDescription();

        var root = FindRoot(app.Services.GetRequiredService<IOptions<ServerOptions>>().Value, app.Environment.ContentRootPath);
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RetailPlus.Server.WebApp");
        if (root is null)
        {
            ServerLog.NoWebApp(log, app.Environment.ContentRootPath);
            return;
        }
        ServerLog.WebApp(log, root);

        var files = new PhysicalFileProvider(root);

        app.UseWhen(http => !IsApi(http.Request.Path), web =>
        {
            // /customers serves customers.html when the export was made without trailing slashes.
            web.Use((http, next) =>
            {
                var path = http.Request.Path.Value ?? "/";
                if (path.Length > 1 && !path.EndsWith('/') && !Path.HasExtension(path) && !files.GetDirectoryContents(path).Exists
                    && files.GetFileInfo(path + ".html").Exists)
                    http.Request.Path = path + ".html";
                return next(http);
            });
            web.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
            web.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = files,
                OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = CacheControl(context.Context.Request.Path, context.File.Name),
            });
        });

        // Whatever else nothing answered: the export's 404 page.
        app.MapFallback("{**path}", async http =>
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            var page = files.GetFileInfo("404.html");
            if (!page.Exists || !(HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method))) return;
            http.Response.ContentType = "text/html; charset=utf-8";
            http.Response.Headers.CacheControl = "no-cache";
            if (HttpMethods.IsGet(http.Request.Method)) await http.Response.SendFileAsync(page);
        }).AllowAnonymous().ExcludeFromDescription();
    }

    public static bool IsApi(PathString path) => path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);

    /// <summary>Next's hashed files never change; pages are checked again every time so a new version shows at once.</summary>
    public static string CacheControl(PathString path, string fileName) =>
        path.StartsWithSegments("/_next/static", StringComparison.OrdinalIgnoreCase) ? ImmutableCache
        : fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? "no-cache"
        : "public, max-age=3600";
}
