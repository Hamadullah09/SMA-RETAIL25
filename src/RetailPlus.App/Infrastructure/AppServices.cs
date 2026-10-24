using RetailPlus.Core.Model;
using RetailPlus.Data.Services;
using RetailPlus.Devices;
using RetailPlus.Integrations;
using RetailPlus.Reports;
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
