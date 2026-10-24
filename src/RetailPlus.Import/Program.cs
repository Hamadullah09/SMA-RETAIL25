using System.Globalization;
using Microsoft.Data.SqlClient;
using RetailPlus.Data;
using RetailPlus.Migration.Import;

// RetailPlus.Import: one-time import of a Retail Plus 5.2 data folder into SQL Server.
// Exit codes: 0 imported and all control totals match, 1 failed or refused, 2 imported but totals differ, 64 usage.

const string Usage = """
    Usage: RetailPlus.Import --source <data folder> [--connection <connection string> | --localdb <database>]
                             [--report <folder>] [--ini <Plus50.INI>] [--year <yyyy>] [--force]

      --source      Retail Plus data root (holds SETUP.DBF and one folder per location). Use a copy taken while
                    Retail Plus was closed.
      --connection  SQL Server connection string of the target database.
      --localdb     Database name on (localdb)\MSSQLLocalDB, for development. Default: RetailPlus_App.
      --report      Folder for report.md and report.json. Default: %LOCALAPPDATA%\RetailPlus\ImportReports\<time>.
      --ini         A workstation's Plus50.INI, imported as that station's settings.
      --year        Year the inventory's monthly and weekly sales columns belong to. Default: the table date.
      --force       Replace the data of a database that already holds items or clients.
    """;

string? source = null, connection = null, localDb = null, report = null, ini = null;
int? year = null;
bool force = false;
for (int i = 0; i < args.Length; i++)
{
    string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
    try
    {
        switch (args[i].ToLowerInvariant())
        {
            case "--source": source = Value(); break;
            case "--connection": connection = Value(); break;
            case "--localdb": localDb = Value(); break;
            case "--report": report = Value(); break;
            case "--ini": ini = Value(); break;
            case "--year": year = int.Parse(Value(), CultureInfo.InvariantCulture); break;
            case "--force": force = true; break;
            case "-h" or "--help" or "/?": Console.WriteLine(Usage); return 0;
            default: throw new ArgumentException($"Unknown option {args[i]}");
        }
    }
    catch (Exception ex) when (ex is ArgumentException or FormatException)
    {
        Console.Error.WriteLine(ex.Message);
        Console.Error.WriteLine(Usage);
        return 64;
    }
}
if (source is null || (connection is not null && localDb is not null))
{
    Console.Error.WriteLine(source is null ? "--source is required." : "Give either --connection or --localdb, not both.");
    Console.Error.WriteLine(Usage);
    return 64;
}

connection ??= RetailPlusDb.LocalDbConnectionString(localDb ?? "RetailPlus_App");
report ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RetailPlus", "ImportReports",
    DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
var target = new SqlConnectionStringBuilder(connection);
Console.WriteLine($"Importing {Path.GetFullPath(source)} into {target.DataSource}/{target.InitialCatalog}{(force ? " (replacing existing data)" : "")}");

var importer = new DbfImporter(new ImportOptions
{
    SourceFolder = source,
    ConnectionString = connection,
    Overwrite = force,
    StationIniPath = ini,
    SalesYear = year,
    Progress = message => Console.WriteLine("  " + message),
});

ImportReport result;
try
{
    result = await importer.RunAsync();
}
catch (ImportRefusedException ex)
{
    Console.Error.WriteLine("Import refused: " + ex.Message);
    return 1;
}
#pragma warning disable CA1031 // The command line reports any failure and exits with a code.
catch (Exception ex)
#pragma warning restore CA1031
{
    Console.Error.WriteLine("Import failed and was rolled back: " + ex.Message);
    if (ex.InnerException is not null) Console.Error.WriteLine("  " + ex.InnerException.Message);
    return 1;
}

var markdown = result.WriteTo(report);
Console.WriteLine();
Console.WriteLine("Control totals:");
foreach (var total in result.Totals)
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"  {(total.Matches ? "OK  " : "DIFF")}  {total.Name,-52} source {total.Source,14:0.###}  database {total.Database,14:0.###}"));
Console.WriteLine();
Console.WriteLine($"{result.Warnings.Count} warnings. Report: {markdown}");
if (!result.VerificationPassed)
{
    Console.Error.WriteLine("Imported, but some control totals differ. Check the report before using this database.");
    return 2;
}
Console.WriteLine("Import succeeded.");
return 0;
