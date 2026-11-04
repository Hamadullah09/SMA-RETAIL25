using RetailPlus.Core.Model;
using RetailPlus.Data.Services;
using RetailPlus.Devices;
using RetailPlus.Integrations;
using RetailPlus.Reports;
using RetailPlus.Rfid;
using RetailPlus.Settings;
using RetailPlus.Sync;

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
    public static StaffService Staff { get; private set; } = null!;
    public static AuditService Audit { get; private set; } = null!;
    public static StockCountService StockCounts { get; private set; } = null!;
    public static TransferService Transfers { get; private set; } = null!;
    public static GiftCardService GiftCards { get; private set; } = null!;
    public static LoyaltyService Loyalty { get; private set; } = null!;
    public static AccountingService Accounting { get; private set; } = null!;
    public static BrandingService Branding { get; private set; } = null!;
    // Supply chain (phase E2)
    public static BatchService Batches { get; private set; } = null!;
    public static RequisitionService Requisitions { get; private set; } = null!;

    /// <summary>The store's logo (Setup › Branding), or null to use the SMA mark.</summary>
    public static System.Windows.Media.ImageSource? Logo { get; private set; }

    /// <summary>The store's watermark for printed slips and reports, or null for none.</summary>
    public static System.Windows.Media.ImageSource? Watermark { get; private set; }

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
        // The audit log and "who did it" columns name whoever is signed in.
        Data = new DataContext(config.EffectiveDatabase) { CurrentUser = Session };
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
        Staff = new StaffService(Data);
        Audit = new AuditService(Data);
        StockCounts = new StockCountService(Data);
        Transfers = new TransferService(Data);
        GiftCards = new GiftCardService(Data);
        Loyalty = new LoyaltyService(Data);
        Accounting = new AccountingService(Data);
        Branding = new BrandingService(Data);
        Batches = new BatchService(Data);
        Requisitions = new RequisitionService(Data);
        Session.StationNumber = config.StationNumber;
        Sync = CreateSync();
    }

    // Server ---------------------------------------------------------------------------------------------------------

    /// <summary>This till's synchronisation with its server; null while it works on its own (or uses a shared SQL Server database).</summary>
    public static TillSync? Sync { get; private set; }

    /// <summary>Where the till stands with its server, for the status line (null when it has not joined one).</summary>
    public static TillSyncStatus? SyncStatus { get; private set; }

    /// <summary>Raised on the UI thread after a send, a receive or a change of server.</summary>
    public static event Action? SyncChanged;

    public static string AppVersion => typeof(AppServices).Assembly.GetName().Version?.ToString() ?? "1.0";

    private static CancellationTokenSource? _syncLoop;

    private static TillSync? CreateSync()
    {
        if (Data.Connection.Provider != RetailPlus.Data.DatabaseProvider.Sqlite || string.IsNullOrWhiteSpace(Config.ServerUrl)) return null;
        if (StationKeyStore.Unprotect(Config.StationKeyProtected) is not { } key) return null;
        return new TillSync(Data, new TillSyncSettings
        {
            ServerUrl = Config.ServerUrl, StationKey = key, Outbox = TillSync.OutboxFor(Data.DatabaseName), AppVersion = AppVersion,
        });
    }

    /// <summary>
    /// After joining a server: keeps the server's address and this station's key (encrypted), takes the station number the
    /// server gave, and starts sending.
    /// </summary>
    public static async Task JoinedAsync(string serverUrl, JoinResult joined)
    {
        Config.ServerUrl = serverUrl.Trim();
        Config.StationKeyProtected = StationKeyStore.Protect(joined.StationKey);
        Config.StationNumber = joined.StationNumber;
        Config.Save();
        Session.StationNumber = joined.StationNumber;
        Sync = CreateSync();
        await RefreshSettingsAsync();
        StartSyncLoop();
        await RefreshSyncStatusAsync();
    }

    /// <summary>After the server address or the interval changed in Setup.</summary>
    public static void RestartSync()
    {
        Sync = CreateSync();
        StartSyncLoop();
    }

    /// <summary>One send and receive, off the UI thread. Never throws: problems show in the status, and selling goes on.</summary>
    public static async Task<SyncRunResult?> SyncNowAsync()
    {
        if (Sync is not { } sync || Unattended) return null;
        SyncRunResult? result = null;
        try
        {
            result = await Task.Run(() => sync.RunAsync());
            if (result.Error is not null) Log.Write("Sync: " + result.Error);
        }
        catch (Exception ex)
        {
            Log.Write("Sync failed: " + ex);
        }
        await RefreshSyncStatusAsync();
        return result;
    }

    public static async Task RefreshSyncStatusAsync()
    {
        try
        {
            SyncStatus = Sync is null ? null : await Sync.GetStatusAsync();
        }
        catch (Exception ex)
        {
            Log.Write("Sync status: " + ex.Message);
        }
        SyncChanged?.Invoke();
    }

    /// <summary>Sends and receives in the background every few minutes (Setup › Server), starting half a minute after start-up.</summary>
    public static void StartSyncLoop()
    {
        _syncLoop?.Cancel();
        _syncLoop = null;
        if (Sync is null || Unattended) return;
        var stop = _syncLoop = new CancellationTokenSource();
        _ = LoopAsync(stop.Token);
    }

    private static async Task LoopAsync(CancellationToken stop)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stop);
            while (!stop.IsCancellationRequested)
            {
                await SyncNowAsync();
                await Task.Delay(TimeSpan.FromMinutes(Math.Clamp(Config.SyncIntervalMinutes, 1, 24 * 60)), stop);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>The short status line: when the till last sent and received, and what is waiting.</summary>
    public static string SyncLine(TillSyncStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        string When(DateTime? utc)
        {
            if (utc is not { } at) return "never";
            var local = DateTime.SpecifyKind(at, DateTimeKind.Utc).ToLocalTime();
            return local.Date == DateTime.Today ? local.ToString("t") : local.ToString("d MMM t");
        }
        var parts = new List<string>();
        if (status.LastError is not null && status.LastAttemptAtUtc is not null && (status.LastSentAtUtc is null || status.LastAttemptAtUtc > status.LastSentAtUtc))
            parts.Add("Server not reached");
        else parts.Add($"Sent {When(status.LastSentAtUtc)}");
        parts.Add($"received {When(status.LastReceivedAtUtc)}");
        if (status.Waiting > 0) parts.Add($"{status.Waiting} waiting");
        if (status.Refused > 0) parts.Add($"{status.Refused} refused");
        return string.Join(" · ", parts);
    }

    /// <summary>After End of Day: sends at once, in the background.</summary>
    public static void SyncSoon()
    {
        if (Sync is not null && !Unattended) _ = SyncNowAsync();
    }

    /// <summary>Reloads the store's logo and watermark after they were changed in Setup › Branding.</summary>
    public static async Task RefreshBrandingAsync()
    {
        try
        {
            Logo = Pictures.ToImage(await Branding.GetAsync(StoreImageKind.Logo));
            Watermark = Pictures.ToImage(await Branding.GetAsync(StoreImageKind.Watermark));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Write("Branding pictures: " + ex.Message);
            Logo = null;
            Watermark = null;
        }
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
        await RefreshBrandingAsync();
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

    /// <summary>
    /// After readers or antennas were changed here, while this station uses a hub on another computer: sends the change to
    /// the server first (waiting up to 15 seconds), so a hub that reads its readers from the server has it when it reloads.
    /// Does nothing on a till that has not joined a server.
    /// </summary>
    public static async Task SendRfidChangesAsync()
    {
        if (RfidHubAddress is null || Sync is null || Unattended) return;
        await Task.WhenAny(SyncNowAsync(), Task.Delay(TimeSpan.FromSeconds(15)));
    }

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

    /// <summary>True when the signed-in user's role has the permission (a key of <see cref="Core.Rules.Permissions"/>).</summary>
    public static bool Can(string permission) => Session.Can(permission);

    /// <summary>True when the signed-in user's role has at least one of the permissions.</summary>
    public static bool CanAny(params string[] permissions) => permissions.Any(Session.Can);
}
