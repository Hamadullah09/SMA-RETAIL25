using System.Text.Json;
using System.Text.Json.Serialization;
using RetailPlus.Data;

namespace RetailPlus.Settings;

/// <summary>How this station reaches the RFID readers.</summary>
public enum RfidMode
{
    /// <summary>
    /// The default: use the shop's RFID hub when one is set for all stations, otherwise connect to this station's
    /// readers directly, finding them on the network the first time.
    /// </summary>
    Auto,
    /// <summary>No RFID at this station.</summary>
    Off,
    /// <summary>This PC connects to the readers itself (a single PC, or the PC the readers are wired to).</summary>
    ThisComputer,
    /// <summary>An RFID hub service on another PC owns the readers; this station listens to it.</summary>
    Hub,
}

/// <summary>
/// The workstation's own settings (what Plus50.INI held per station): which database, which station and location,
/// and the devices attached. Kept in a JSON file; everything shared lives in the database.
/// </summary>
public sealed class StationConfig
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// SQLite (the default): this till keeps its own database file and sells without a network. SQL Server: the till
    /// opens a shared database on the network, as before.
    /// </summary>
    public DatabaseProvider DatabaseProvider { get; set; } = DatabaseProvider.Sqlite;
    /// <summary>The SQLite file; empty uses smaretail.db in <see cref="DefaultFolder"/>.</summary>
    public string? DatabaseFile { get; set; }
    /// <summary>SQL Server only: full connection string; when empty, <see cref="LocalDbDatabase"/> on LocalDB is used.</summary>
    public string? ConnectionString { get; set; }
    public string LocalDbDatabase { get; set; } = "RetailPlus_App";
    public int StationNumber { get; set; } = 1;
    public string LocationCode { get; set; } = "POS";

    public string? ReceiptPrinter { get; set; }
    public int ReceiptWidth { get; set; } = 40;
    public bool PrintReceipts { get; set; } = true;
    public bool AskBeforePrinting { get; set; }
    public bool DrawerOnPrinter { get; set; } = true;
    public int DrawerPin { get; set; } = 2;
    public string? DrawerSerialPort { get; set; }
    public string? PoleDisplayPort { get; set; }
    public string? ScalePort { get; set; }

    public string? ReceiptFolder { get; set; }
    public string? BackupFolder { get; set; }
    public string? ExportFolder { get; set; }
    public string? PhotoFolder { get; set; }
    public RfidMode RfidMode { get; set; } = RfidMode.Auto;
    /// <summary>Address of the RFID hub service, e.g. http://192.168.0.10:5280.</summary>
    public string? RfidHubUrl { get; set; }
    public string? RfidHubApiKey { get; set; }
    /// <summary>When this computer talks to the readers itself: which readers (by hub key) it owns.</summary>
    public string RfidHubKey { get; set; } = "main";
    /// <summary>Tags read by this station's antennas are added to the sale without pressing anything.</summary>
    public bool RfidAutoAdd { get; set; } = true;
    /// <summary>The POS beeps once for each tag it rings up (the readers' own buzzers stay quiet).</summary>
    public bool RfidBeep { get; set; } = true;

    /// <summary>Ask for a user name and password when the program starts.</summary>
    public bool RequireLogin { get; set; }

    /// <summary>The server this till sends its data to, e.g. https://pos.example.com; empty while it works on its own.</summary>
    public string? ServerUrl { get; set; }
    /// <summary>The key the server gave this station when it joined, encrypted for this Windows user (DPAPI).</summary>
    public string? StationKeyProtected { get; set; }
    /// <summary>While the server can be reached, the till sends and receives every this many minutes.</summary>
    public int SyncIntervalMinutes { get; set; } = 5;

    [JsonIgnore]
    public string? FilePath { get; private set; }

    /// <summary>The database to open. RETAILPLUS_CONNECTION (a SQL Server connection string) overrides the file.</summary>
    [JsonIgnore]
    public DatabaseConnection EffectiveDatabase =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RETAILPLUS_CONNECTION")) ? DatabaseConnection.SqlServer(Environment.GetEnvironmentVariable("RETAILPLUS_CONNECTION")!)
        : DatabaseProvider == DatabaseProvider.Sqlite ? DatabaseConnection.SqliteFile(DatabaseFileOrDefault)
        : !string.IsNullOrWhiteSpace(ConnectionString) ? DatabaseConnection.SqlServer(ConnectionString!)
        : DatabaseConnection.LocalDb(LocalDbDatabase);

    [JsonIgnore]
    public string DatabaseFileOrDefault => string.IsNullOrWhiteSpace(DatabaseFile) ? DefaultDatabaseFile : DatabaseFile!;

    public static string DefaultDatabaseFile => RetailPlusDb.DefaultSqliteFile;

    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RetailPlus");

    public static string DefaultPath => Path.Combine(DefaultFolder, "station.json");

    [JsonIgnore]
    public string ReceiptFolderOrDefault => ReceiptFolder ?? Path.Combine(DefaultFolder, "Receipts");
    [JsonIgnore]
    public string BackupFolderOrDefault => BackupFolder ?? Path.Combine(DefaultFolder, "Backups");
    [JsonIgnore]
    public string ExportFolderOrDefault => ExportFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Retail Plus");

    /// <summary>Reads the file, or returns defaults (and remembers the path) when there is none.</summary>
    public static StationConfig Load(string? path = null)
    {
        path ??= DefaultPath;
        StationConfig config;
        if (File.Exists(path))
        {
            try
            {
                config = JsonSerializer.Deserialize<StationConfig>(File.ReadAllText(path), Options) ?? new StationConfig();
            }
            catch (JsonException)
            {
                config = new StationConfig();
            }
        }
        else config = new StationConfig();
        config.FilePath = path;
        return config;
    }

    public void Save(string? path = null)
    {
        path ??= FilePath ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
        FilePath = path;
    }
}
