using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Retail25.Infrastructure.Catalog;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Rfid;
using Retail25.Application.Rfid.Commands;
using Retail25.Infrastructure.Persistence;
using Retail25.Infrastructure.Services;

/*
 * Operations that belong at a command line rather than in a browser.
 *
 * Right now that is one thing: loading a tag export. The same work is available from the RFID admin
 * screen and most of the time that is the right place for it — but a first cut-over runs before
 * anybody has an account on the new system, and a quarter of a million tags is not an upload anyone
 * should be watching a progress bar for.
 *
 * The handler is called directly rather than through MediatR. There is no HTTP request here, no
 * signed-in user and therefore nothing for the authorisation behaviour to check: whoever can run
 * this already holds the database's credentials, which is strictly more than the permission would
 * have granted them. Skipping the pipeline is honest about that rather than fabricating a principal
 * to satisfy it.
 */

return await Run(args);

static async Task<int> Run(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
    {
        Usage();
        return args.Length == 0 ? 1 : 0;
    }

    return args[0] switch
    {
        "import-tags" => await ImportTags(args.Skip(1).ToArray()),
        _ => Unknown(args[0]),
    };
}

static void Usage()
{
    Console.WriteLine("""
        Retail25 operations.

          import-tags <file.csv> --location <id> [--dry-run] [--keep-states]

            Loads a tag export: one item per stock code, one tag per EPC. Items that already
            exist are matched, never overwritten, and running the same file twice adds nothing.

            --dry-run      Report what the file would do and write nothing.
            --keep-states  Import each tag in the state the file gives it. Without this every
                           tag arrives in stock, which is almost always what is wanted: the
                           state column in an export is a snapshot of some earlier session, and
                           a tag imported as Sold cannot be scanned.

        The connection string comes from RETAIL25_DESIGN_CONNECTION, or from the API's
        appsettings.Development.json when that is not set.
        """);
}

static int Unknown(string verb)
{
    Console.Error.WriteLine($"Unknown command '{verb}'. Run with --help.");
    return 1;
}

static async Task<int> ImportTags(string[] args)
{
    var path = args.FirstOrDefault(a => !a.StartsWith('-'));

    if (path is null)
    {
        Console.Error.WriteLine("A CSV file is required. Run with --help.");
        return 1;
    }

    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"No such file: {path}");
        return 1;
    }

    var locationIndex = Array.IndexOf(args, "--location");

    if (locationIndex < 0
        || locationIndex + 1 >= args.Length
        || !long.TryParse(args[locationIndex + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var locationId))
    {
        Console.Error.WriteLine("--location <id> is required. Run with --help.");
        return 1;
    }

    var connection = ConnectionString();

    if (connection is null)
    {
        Console.Error.WriteLine(
            "No connection string. Set RETAIL25_DESIGN_CONNECTION, or run from a tree where "
            + "src/Retail25.Api/appsettings.Development.json has one.");

        return 1;
    }

    var dryRun = args.Contains("--dry-run");

    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlServer(connection)
        .UseSnakeCaseNamingConvention()
        .Options;

    await using var db = new ApplicationDbContext(options);

    var csv = await File.ReadAllTextAsync(path);

    // The same fetcher the API uses, so a CSV imported from the command line behaves identically.
    // Redirects off for the reason the fetcher documents: it revalidates each hop itself.
    using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });

    var handler = new ImportEpcCatalogHandler(
        db,
        new SystemClock(),
        new TagStreamRegistry(),
        new HttpRemoteImageFetcher(http, NullLogger<HttpRemoteImageFetcher>.Instance));

    var result = await handler.Handle(
        new ImportEpcCatalogCommand(locationId, csv, dryRun, ResetToInStock: !args.Contains("--keep-states")),
        CancellationToken.None);

    if (result.IsFailure)
    {
        Console.Error.WriteLine($"{result.Error.Code}: {result.Error.Message}");
        return 1;
    }

    var summary = result.Value;

    Console.WriteLine(dryRun ? "Rehearsal — nothing was written." : "Imported.");
    Console.WriteLine($"  rows read          {summary.RowsRead}");
    Console.WriteLine($"  tags created       {summary.TagsCreated}");
    Console.WriteLine($"  tags already known {summary.TagsAlreadyMapped}");
    Console.WriteLine($"  items created      {summary.ProductsCreated}");
    Console.WriteLine($"  items matched      {summary.ProductsMatched}");

    var dropped = summary.Problems.Where(p => p.RowDropped).ToList();
    var noted = summary.Problems.Count - dropped.Count;

    if (noted > 0)
    {
        Console.WriteLine($"  rows defaulted     {noted}");
    }

    if (dropped.Count > 0)
    {
        // Every rejected row, not a count. A file that quietly loses eleven tags is a shop that
        // finds out eleven items will not scan, one customer at a time.
        Console.WriteLine();
        Console.WriteLine($"{dropped.Count} rows were not imported:");

        foreach (var problem in dropped)
        {
            Console.WriteLine($"  line {problem.LineNumber,5}  {problem.Value,-30}  {problem.Message}");
        }
    }

    return 0;
}

/// <summary>
/// The same environment variable <c>DesignTimeDbContextFactory</c> reads, so pointing the tooling
/// and this tool at a database is one decision rather than two. Falling back to the API's own
/// development settings means the common case — a developer, their own machine — needs neither.
/// </summary>
static string? ConnectionString()
{
    var fromEnvironment = Environment.GetEnvironmentVariable("RETAIL25_DESIGN_CONNECTION");

    if (!string.IsNullOrWhiteSpace(fromEnvironment))
    {
        return fromEnvironment;
    }

    var directory = new DirectoryInfo(AppContext.BaseDirectory);

    while (directory is not null)
    {
        var settings = Path.Combine(directory.FullName, "src", "Retail25.Api", "appsettings.Development.json");

        if (File.Exists(settings))
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(settings));

            if (document.RootElement.TryGetProperty("ConnectionStrings", out var strings)
                && strings.TryGetProperty("DefaultConnection", out var value)
                && value.GetString() is { Length: > 0 } found)
            {
                return found;
            }
        }

        directory = directory.Parent;
    }

    return null;
}
