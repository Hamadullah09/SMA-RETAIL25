using System.Globalization;
using System.Text;
using RetailPlus.Cutover;
using RetailPlus.Data;

// RetailPlus.Cutover: carries a Retail25 database (the old web system) over into the new model, on the server's SQL Server
// database or a SQLite file. Exit codes: 0 done and every check matches, 2 done but a check differs, 1 failed or refused,
// 64 usage. See docs/CUTOVER.md.

const string Usage = """
    Usage: RetailPlus.Cutover --source "<Retail25 SQL Server connection>" --target "<connection string or .db file>"
                              [--provider sqlserver|sqlite] [--dry-run] [--report <file.md>] [--store-code <code>]

      --source      Connection string of the Retail25 database. It is only read.
      --target      The new database: a SQL Server connection string, or a SQLite file. It is created or upgraded first.
      --provider    sqlserver or sqlite. Default: sqlite for a file name, sqlserver for a connection string.
      --dry-run     Read, map and check everything; write nothing. The report is made all the same.
      --report      Where to write the report (Markdown; a .json copy is written beside it).
                    Default: %LOCALAPPDATA%\RetailPlus\CutoverReports\cutover-<time>.md
      --store-code  Carry over only the Retail25 location with this code (TST…), as one store.
    """;

string? source = null, target = null, provider = null, report = null, storeCode = null;
bool dryRun = false;
for (int i = 0; i < args.Length; i++)
{
    string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
    try
    {
        switch (args[i].ToLowerInvariant())
        {
            case "--source": source = Value(); break;
            case "--target": target = Value(); break;
            case "--provider": provider = Value().ToLowerInvariant(); break;
            case "--dry-run": dryRun = true; break;
            case "--report": report = Value(); break;
            case "--store-code": storeCode = Value(); break;
            case "-h" or "--help" or "/?": Console.WriteLine(Usage); return 0;
            default: throw new ArgumentException($"Unknown option {args[i]}");
        }
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine(ex.Message);
        Console.Error.WriteLine(Usage);
        return 64;
    }
}
if (source is null || target is null || provider is not (null or "sqlserver" or "sqlite"))
{
    Console.Error.WriteLine(source is null || target is null ? "--source and --target are required." : "--provider is sqlserver or sqlite.");
    Console.Error.WriteLine(Usage);
    return 64;
}

provider ??= target.Contains('=', StringComparison.Ordinal) ? "sqlserver" : "sqlite";
// A SQLite target may be given as a file or as a connection string ("Data Source=…").
var database = provider == "sqlite"
    ? DatabaseConnection.SqliteFile(target.Contains('=', StringComparison.Ordinal) ? new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(target).DataSource : target)
    : DatabaseConnection.SqlServer(target);
report ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RetailPlus", "CutoverReports",
    "cutover-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".md");
Console.WriteLine($"Retail25 cutover into {database.Description}{(dryRun ? " (dry run: nothing is written)" : "")}");

CutoverReport result;
try
{
    result = await new CutoverRunner(new CutoverOptions
    {
        SourceConnectionString = source,
        Target = database,
        DryRun = dryRun,
        StoreCode = storeCode,
        Progress = message => Console.WriteLine("  " + message),
    }).RunAsync();
}
catch (CutoverRefusedException ex)
{
    Console.Error.WriteLine("Cutover refused: " + ex.Message);
    return 1;
}
#pragma warning disable CA1031 // The command line reports any failure and exits with a code.
catch (Exception ex)
#pragma warning restore CA1031
{
    Console.Error.WriteLine("Cutover failed and was rolled back: " + ex.Message);
    if (ex.InnerException is not null) Console.Error.WriteLine("  " + ex.InnerException.Message);
    return 1;
}

var markdown = result.ToMarkdown();
var folder = Path.GetDirectoryName(Path.GetFullPath(report));
if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
File.WriteAllText(report, markdown, Encoding.UTF8);
File.WriteAllText(Path.ChangeExtension(report, ".json"), result.ToJson(), Encoding.UTF8);
Console.WriteLine();
Console.WriteLine(markdown);
Console.WriteLine($"Report: {Path.GetFullPath(report)}");
if (!result.ChecksPassed)
{
    Console.Error.WriteLine("Some checks differ. Read the report before using this database.");
    return 2;
}
Console.WriteLine(dryRun ? "Dry run done; every check matches." : "Cutover done; every check matches.");
return 0;
