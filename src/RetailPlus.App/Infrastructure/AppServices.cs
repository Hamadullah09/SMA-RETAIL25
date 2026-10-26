using RetailPlus.Core.Model;
using RetailPlus.Data.Services;
using RetailPlus.Devices;
using RetailPlus.Integrations;
using RetailPlus.Reports;
using RetailPlus.Rfid;
using RetailPlus.Settings;

namespace RetailPlus.App.Infrastructure;

/// <summary>The services of the running program. The data services are stateless, so one instance of each is shared.</summary>
public static class AppServices
{
    public static StationConfig Config { get; private set; } = new();
    public static DataContext Data { get; private set; } = null!;
    public static SessionInfo Session { get; } = new();
    public static StoreSettings Settings { get; private set; } = new();
    public static List<Location> Locations { get; private set; } = [];

    public static SetupService Setup { get; private set; } = null!;
    public static InventoryService Inventory { get; private set; } = null!;
    public static PartyService Parties { get; private set; } = null!;
    public static SalesService Sales { get; private set; } = null!;
    public static DrawerService Drawer { get; private set; } = null!;
    public static InvoiceService Invoices { get; private set; } = null!;
    public static PurchasingService Purchasing { get; private set; } = null!;
    public static BackupService Backup { get; private set; } = null!;
    public static ReportService Reports { get; private set; } = null!;
    public static CsvExchange Csv { get; private set; } = null!;
    public static RfidService RfidData { get; private set; } = null!;

    /// <summary>The RFID readers as this station reaches them (null when RFID is off at this station).</summary>
    public static IRfidControl? Rfid { get; private set; }

    /// <summary>Raised after RFID was started, stopped or switched to another hub.</summary>
    public static event Action? RfidChanged;

    private static RfidHub? _localHub;

    /// <summary>True when the program was started for an automated check (no printing, no message boxes).</summary>
    public static bool Unattended { get; set; }

    public static void Initialize(StationConfig config)
    {
        Config = config;
        Data = new DataContext(config.EffectiveConnectionString);
        Setup = new SetupService(Data);
        Inventory = new InventoryService(Data);
        Parties = new PartyService(Data);
        Sales = new SalesService(Data);
        Drawer = new DrawerService(Data);
        Invoices = new InvoiceService(Data);
        Purchasing = new PurchasingService(Data);
        Backup = new BackupService(Data);
        Reports = new ReportService(Data);
        Csv = new CsvExchange(Data);
        RfidData = new RfidService(Data);
        Session.StationNumber = config.StationNumber;
    }

    public static async Task RefreshSettingsAsync()
    {
        Settings = await Setup.GetSettingsAsync();
        Locations = await Setup.GetLocationsAsync();
        var location = Locations.FirstOrDefault(l => string.Equals(l.Code, Config.LocationCode, StringComparison.OrdinalIgnoreCase)) ?? Locations.FirstOrDefault();
        if (location is not null)
        {
            Session.LocationId = location.Id;
            Session.LocationCode = location.Code;
        }
    }

    /// <summary>Connects to the RFID readers as the station settings say (this computer, a hub, or not at all).</summary>
    public static async Task StartRfidAsync()
    {
        await StopRfidAsync();
        try
        {
            var siteHub = Config.RfidMode == RfidMode.Auto ? await Setup.GetExtraAsync(SiteHubSetting) : null;
            switch (Config.RfidMode)
            {
                case RfidMode.Auto when !string.IsNullOrWhiteSpace(siteHub):
                    Rfid = new RfidHubClient(siteHub!, Config.RfidHubApiKey);
                    RfidHubAddress = siteHub;
                    break;
                case RfidMode.Auto:
                case RfidMode.ThisComputer:
                    var key = Config.RfidHubKey;
                    _localHub = new RfidHub(async ct => await RfidData.GetReadersAsync(key, ct)) { Log = m => Log.Write("RFID " + m) };
                    await _localHub.StartAsync();
                    Rfid = _localHub;
                    break;
                case RfidMode.Hub when !string.IsNullOrWhiteSpace(Config.RfidHubUrl):
                    Rfid = new RfidHubClient(Config.RfidHubUrl!, Config.RfidHubApiKey);
                    RfidHubAddress = Config.RfidHubUrl;
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Write("RFID could not start: " + ex.Message);
            Rfid = null;
        }
        RfidChanged?.Invoke();
    }

    /// <summary>Name of the shop-wide setting that sends every Automatic station to one RFID hub.</summary>
    public const string SiteHubSetting = "RfidHubUrl";

    /// <summary>The hub this station listens to, when it uses one.</summary>
    public static string? RfidHubAddress { get; private set; }

    /// <summary>What the start-up search for readers is doing (null when it is not running).</summary>
    public static string? RfidSearchText { get; private set; }

    /// <summary>True when this computer connects to the readers itself (Automatic or This computer, without a hub).</summary>
    public static bool RfidIsLocal => _localHub is not null;

    /// <summary>
    /// First start with RFID on Automatic and no readers set up yet: look for readers on this PC's networks, add
    /// what answers and connect. One reader found is given to this station (its connected antennas); several are
    /// added without stations for the administrator to assign.
    /// </summary>
    public static async Task<int> DiscoverReadersAsync(bool force = false)
    {
        if (!RfidIsLocal) return 0;
        var known = await RfidData.GetReadersAsync();
        if (!force && known.Any(r => r.HubKey == Config.RfidHubKey)) return 0;
        RfidSearchText = "Looking for RFID readers…";
        RfidChanged?.Invoke();
        try
        {
            var usedHosts = known.Select(r => r.Host).Where(h => h is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var factory = await RfidDiscovery.FindAsync(RfidDiscovery.CandidateHosts(false).Where(h => !usedHosts.Contains(h)), connectTimeout: TimeSpan.FromSeconds(1));
            var found = factory.Count > 0 ? factory
                : await RfidDiscovery.FindAsync(RfidDiscovery.CandidateHosts(true).Where(h => !usedHosts.Contains(h)));
            int n = known.Count;
            foreach (var reader in found)
            {
                var record = RfidDiscovery.ToReader(reader, $"Reader {++n}", found.Count == 1 ? Session.StationNumber : null);
                record.HubKey = Config.RfidHubKey;
                await RfidData.SaveReaderAsync(record);
                Log.Write($"RFID reader found at {reader.Host}:{reader.Port} (firmware {reader.Info.Firmware}, antennas {string.Join(",", reader.ConnectedAntennas)})");
            }
            if (found.Count > 0 && Rfid is { } rfid) await rfid.ReloadAsync();
            return found.Count;
        }
        catch (Exception ex)
        {
            Log.Write("RFID search: " + ex.Message);
            return 0;
        }
        finally
        {
            RfidSearchText = null;
            RfidChanged?.Invoke();
        }
    }

    public static async Task StopRfidAsync()
    {
        RfidHubAddress = null;
        if (_localHub is not null)
        {
            await _localHub.DisposeAsync();
            _localHub = null;
        }
        (Rfid as IDisposable)?.Dispose();
        Rfid = null;
    }

    public static string RfidModeText =>
        Rfid is null ? "RFID is off at this station"
        : RfidHubAddress is not null ? $"RFID hub at {RfidHubAddress}"
        : "This computer connects to the readers";

    public static IReceiptPrinter Printer =>
        Unattended || string.IsNullOrWhiteSpace(Config.ReceiptPrinter)
            ? new FileReceiptPrinter(Config.ReceiptFolderOrDefault)
            : new WindowsReceiptPrinter(Config.ReceiptPrinter!, Config.DrawerPin);

    /// <summary>Shows text on the pole display when one is set up; display errors never stop a sale.</summary>
    public static void Pole(string line1, string line2 = "")
    {
        if (string.IsNullOrWhiteSpace(Config.PoleDisplayPort) || Unattended) return;
        try
        {
            _pole ??= new PoleDisplay(Config.PoleDisplayPort!);
            _pole.Show(line1, line2);
        }
        catch (Exception)
        {
            _pole?.Dispose();
            _pole = null;
        }
    }

    private static PoleDisplay? _pole;

    public static bool Can(int level) => Session.AccessLevel >= level;
}
