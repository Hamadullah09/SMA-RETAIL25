using System.Windows;
using System.Windows.Threading;
using RetailPlus.App.Dialogs;
using RetailPlus.App.Infrastructure;
using RetailPlus.Settings;

namespace RetailPlus.App;

/// <summary>
/// Start-up: read the station settings, open (and if needed create or upgrade) the database, log in, show the main
/// window. Command line: <c>--config &lt;station.json&gt;</c>, <c>--localdb &lt;name&gt;</c>, <c>--station &lt;n&gt;</c>,
/// <c>--smoke</c> (exercise every screen and exit), <c>--screenshot-tour &lt;folder&gt;</c> (save a PNG of every screen and exit).
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        var args = e.Args;
        string? Arg(string name)
        {
            int i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        var config = StationConfig.Load(Arg("--config"));
        if (Arg("--localdb") is { } localDb) { config.LocalDbDatabase = localDb; config.ConnectionString = null; }
        if (int.TryParse(Arg("--station"), out var station)) config.StationNumber = station;
        var tourFolder = Arg("--screenshot-tour");
        bool smoke = args.Any(a => string.Equals(a, "--smoke", StringComparison.OrdinalIgnoreCase));
        AppServices.Unattended = smoke || tourFolder is not null;
        if (AppServices.Unattended) Log.FilePath = System.IO.Path.Combine(StationConfig.DefaultFolder, "retailplus-check.log");
        AppServices.Initialize(config);

        var splash = new SplashWindow();
        splash.Show();
        bool ready = false;
        while (!ready)
        {
            try
            {
                splash.Status = "Opening the database…";
                await AppServices.Data.MigrateAsync();
                await AppServices.Setup.SeedAsync(config.StationNumber);
                await AppServices.RefreshSettingsAsync();
                ready = true;
            }
            catch (Exception ex)
            {
                Log.Write("Database open failed: " + ex);
                if (AppServices.Unattended) { Shutdown(2); return; }
                splash.Hide();
                var dlg = new DatabaseWindow(config, ex.Message);
                if (dlg.ShowDialog() != true) { Shutdown(1); return; }
                config = dlg.Config;
                config.Save();
                AppServices.Initialize(config);
                splash.Show();
            }
        }

        var users = await AppServices.Setup.GetUsersAsync();
        if (users.Count > 0 && !AppServices.Unattended)
        {
            splash.Hide();
            var login = new LoginWindow();
            if (login.ShowDialog() != true) { Shutdown(0); return; }
        }
        else
        {
            AppServices.Session.UserName = "Admin";
            AppServices.Session.AccessLevel = 4;
        }
        splash.Close();

        var main = new MainWindow();
        MainWindow = main;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        main.Show();

        if (tourFolder is not null)
        {
            int code = await main.RunScreenshotTourAsync(tourFolder);
            Shutdown(code);
        }
        else if (smoke)
        {
            int code = await SmokeCheck.RunAsync(main);
            Shutdown(code);
        }
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Write("Unhandled: " + e.Exception);
        if (AppServices.Unattended)
        {
            e.Handled = true;
            Shutdown(3);
            return;
        }
        MessageBox.Show("An unexpected error occurred:\n\n" + e.Exception.Message + "\n\nDetails were written to " + Log.FilePath, "Retail Plus",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
