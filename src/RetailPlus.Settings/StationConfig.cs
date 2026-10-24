using System.Text.Json;
using System.Text.Json.Serialization;
using RetailPlus.Data;

namespace RetailPlus.Settings;

/// <summary>
/// The workstation's own settings (what Plus50.INI held per station): which database, which station and location,
/// and the devices attached. Kept in a JSON file; everything shared lives in the database.
/// </summary>
public sealed class StationConfig
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Full connection string; when empty, <see cref="LocalDbDatabase"/> on LocalDB is used.</summary>
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
    /// <summary>Ask for a user name and password when the program starts.</summary>
    public bool RequireLogin { get; set; }

    [JsonIgnore]
    public string? FilePath { get; private set; }

    public string EffectiveConnectionString =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RETAILPLUS_CONNECTION")) ? Environment.GetEnvironmentVariable("RETAILPLUS_CONNECTION")!
        : !string.IsNullOrWhiteSpace(ConnectionString) ? ConnectionString!
        : RetailPlusDb.LocalDbConnectionString(LocalDbDatabase);

    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RetailPlus");

    public static string DefaultPath => Path.Combine(DefaultFolder, "station.json");

    public string ReceiptFolderOrDefault => ReceiptFolder ?? Path.Combine(DefaultFolder, "Receipts");
    public string BackupFolderOrDefault => BackupFolder ?? Path.Combine(DefaultFolder, "Backups");
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
