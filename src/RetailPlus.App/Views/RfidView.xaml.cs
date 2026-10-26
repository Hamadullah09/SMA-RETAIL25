using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RetailPlus.App.Dialogs;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Data.Services;
using RetailPlus.Rfid;
using RetailPlus.Rfid.D2184;

namespace RetailPlus.App.Views;

/// <summary>A choice in a station drop-down; Value null means "not assigned".</summary>
public sealed record StationChoice(int? Value, string Text);

/// <summary>An antenna use, with the words shown for it.</summary>
public sealed record PurposeChoice(RfidAntennaPurpose Value, string Text)
{
    public static IReadOnlyList<PurposeChoice> All { get; } =
    [
        new(RfidAntennaPurpose.PointOfSale, "Point of sale"),
        new(RfidAntennaPurpose.TagRegistration, "Tag registration"),
        new(RfidAntennaPurpose.Gate, "Exit gate"),
        new(RfidAntennaPurpose.Shelf, "Shelf"),
    ];
}

/// <summary>Administration of RFID readers: status, settings, antenna-to-station assignment and a live tag monitor.</summary>
public partial class RfidView : UserControl, IRefreshable
{
    public sealed record ReaderRow(int Id, string Name, string? Location, string Endpoint, string State, Brush StatusBrush, string ActiveAntennas,
        long TagsRead, string LastTag, string? Firmware, string? Problem);

    /// <summary>An editable row of the antenna grid.</summary>
    public sealed class AntennaEdit
    {
        public int AntennaId { get; init; }
        public int ReaderId { get; init; }
        public string ReaderName { get; init; } = "";
        public string Endpoint { get; init; } = "";
        public int Port { get; init; }
        public string? Name { get; set; }
        public bool IsEnabled { get; set; }
        public int PowerDbm { get; set; }
        public RfidAntennaPurpose Purpose { get; set; }
        public int? Station { get; set; }
    }

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly ObservableCollection<string> _monitorLines = [];
    private readonly Dictionary<string, string> _names = [];
    private List<RfidReader> _readers = [];
    private CancellationTokenSource? _monitor;

    public RfidView()
    {
        InitializeComponent();
        PurposeColumn.ItemsSource = PurposeChoice.All;
        MonitorList.ItemsSource = _monitorLines;
        _timer.Tick += async (_, _) => await RefreshStatusAsync();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _timer.Start();
            else
            {
                _timer.Stop();
                StopMonitor();
            }
        };
    }

    private int? SelectedReaderId => (ReadersGrid.SelectedItem as ReaderRow)?.Id;

    public static async Task<List<StationChoice>> StationChoicesAsync(bool includeNone)
    {
        var stations = await AppServices.Setup.GetStationsAsync();
        var numbers = stations.Select(s => s.Number).Union(Enumerable.Range(1, 10)).Order().ToList();
        var list = new List<StationChoice>();
        if (includeNone) list.Add(new StationChoice(null, "Not used"));
        list.AddRange(numbers.Select(n => new StationChoice(n, stations.FirstOrDefault(s => s.Number == n)?.Name ?? $"Station {n:000}")));
        return list;
    }

    public async Task RefreshAsync()
    {
        _readers = await AppServices.RfidData.GetReadersAsync();
        var choices = await StationChoicesAsync(true);
        StationColumn.ItemsSource = choices;
        if (MonitorStation.ItemsSource is null)
        {
            MonitorStation.ItemsSource = choices.Where(c => c.Value is not null).ToList();
            MonitorStation.SelectedValue = AppServices.Session.StationNumber;
        }
        AntennaGrid.ItemsSource = _readers.SelectMany(r => r.Antennas.OrderBy(a => a.Port).Select(a => new AntennaEdit
        {
            AntennaId = a.Id, ReaderId = r.Id, ReaderName = r.Name, Endpoint = r.Endpoint, Port = a.Port, Name = a.Name, IsEnabled = a.IsEnabled,
            PowerDbm = a.PowerDbm, Purpose = a.Purpose, Station = a.StationNumber,
        })).ToList();
        await RefreshStatusAsync();
        _timer.Start();
    }

    private async Task RefreshStatusAsync()
    {
        var on = AppServices.Rfid is not null;
        ModeText.Text = AppServices.RfidModeText;
        ModeDot.Fill = on ? Look.Brush("Ok") : Look.Brush("Stone");
        IReadOnlyList<ReaderStatus> status = [];
        string? hubProblem = null;
        if (AppServices.Rfid is { } rfid)
        {
            try
            {
                status = await rfid.GetStatusAsync();
            }
            catch (Exception ex) when (ex is RfidException or HttpRequestException or TaskCanceledException)
            {
                hubProblem = "RFID hub not reachable: " + ex.Message;
                ModeDot.Fill = Look.Brush("Danger");
            }
        }
        var selected = SelectedReaderId;
        var rows = _readers.Select(r =>
        {
            var s = status.FirstOrDefault(x => x.ReaderId == r.Id);
            string state = !r.IsEnabled ? "Disabled" : s?.State ?? (on ? "Starting" : "RFID off");
            Brush brush = !r.IsEnabled || s is null ? Look.Brush("Stone")
                : s.Connected ? Look.Brush("Ok")
                : state == "Idle" ? Look.Brush("Sun") : Look.Brush("Danger");
            int stations = r.Antennas.Count(a => a.IsEnabled && a.StationNumber is not null);
            return new ReaderRow(r.Id, r.Name, r.Location, r.Endpoint, state, brush,
                s is null ? $"{stations} assigned" : $"{s.ActiveAntennas} of {stations}",
                s?.TagsRead ?? 0, s?.LastTagAt?.ToString("T", CultureInfo.CurrentCulture) ?? "", s?.Firmware ?? r.FirmwareVersion,
                hubProblem ?? s?.LastError);
        }).ToList();
        ReadersGrid.ItemsSource = rows;
        ReadersGrid.SelectedItem = rows.FirstOrDefault(r => r.Id == selected) ?? rows.FirstOrDefault();
    }

    private async Task ReloadHubAsync()
    {
        if (AppServices.Rfid is { } rfid)
        {
            try { await rfid.ReloadAsync(); } catch (Exception ex) when (ex is RfidException or HttpRequestException) { Log.Write("RFID reload: " + ex.Message); }
        }
        await RefreshAsync();
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var reader = RfidService.NewReader($"Reader {_readers.Count + 1}");
        if (_readers.Count > 0) reader.Host = NextAddress(_readers.Select(r => r.Host).ToList());
        if (new ReaderEditorWindow(reader) { Owner = Window.GetWindow(this) }.ShowDialog() == true) await Ui.Try(ReloadHubAsync);
    }

    /// <summary>Suggests the next free address in the same network (192.168.0.178, .179, …).</summary>
    private static string NextAddress(List<string?> used)
    {
        var last = used.LastOrDefault(h => h is not null && h.Count(c => c == '.') == 3);
        if (last is null) return "192.168.0.178";
        var parts = last.Split('.');
        for (int n = int.Parse(parts[3], CultureInfo.InvariantCulture) + 1; n < 255; n++)
        {
            var candidate = $"{parts[0]}.{parts[1]}.{parts[2]}.{n}";
            if (!used.Contains(candidate)) return candidate;
        }
        return last;
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedReaderId is not int id) return;
        var reader = await AppServices.RfidData.GetReaderAsync(id);
        if (reader is null) return;
        if (new ReaderEditorWindow(reader) { Owner = Window.GetWindow(this) }.ShowDialog() == true) await Ui.Try(ReloadHubAsync);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ReadersGrid.SelectedItem is not ReaderRow row || !Ui.Confirm($"Delete reader {row.Name}? Its antennas stop working at their stations.")) return;
        await Ui.Try(async () =>
        {
            await AppServices.RfidData.DeleteReaderAsync(row.Id);
            await ReloadHubAsync();
        });
    }

    private IRfidControl? Control()
    {
        if (AppServices.Rfid is { } rfid) return rfid;
        Ui.Error("RFID is off at this station. Open Station RFID settings and choose how this computer reaches the readers.");
        return null;
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (ReadersGrid.SelectedItem is not ReaderRow row || Control() is not { } rfid) return;
        await Ui.Try(async () =>
        {
            var problems = await rfid.ApplySettingsAsync(row.Id);
            if (AppServices.Config.RfidMode == Settings.RfidMode.ThisComputer)
                await AppServices.RfidData.MarkAppliedAsync(row.Id, (await rfid.GetStatusAsync()).FirstOrDefault(s => s.ReaderId == row.Id)?.Firmware);
            if (problems.Count == 0) Ui.Info($"The settings were sent to {row.Name}.", "Reader updated");
            else Ui.Error($"{row.Name} did not take every setting:\n\n" + string.Join("\n", problems), "Some settings refused");
            await RefreshAsync();
        });
    }

    private async void Info_Click(object sender, RoutedEventArgs e)
    {
        if (ReadersGrid.SelectedItem is not ReaderRow row || Control() is not { } rfid) return;
        await Ui.Try(async () => new TextPreviewWindow($"Reader {row.Name}", Describe(row.Name, await rfid.ReadInfoAsync(row.Id))).ShowDialog());
    }

    public static string Describe(string name, ReaderInfo info)
    {
        var sb = new StringBuilder();
        sb.AppendLine(name);
        sb.AppendLine(new string('-', 40));
        sb.AppendLine($"Firmware          {info.Firmware ?? "?"}");
        sb.AppendLine($"Temperature       {(info.TemperatureC is int t ? t + " °C" : "?")}");
        if (info.PowerDbm is { } power) sb.AppendLine($"Power (dBm)       {string.Join(" / ", power)}   (antennas 1-4)");
        if (info.Region is { } region)
        {
            sb.AppendLine(region == RfidFrequencyRegion.Custom
                ? $"Frequency         custom: {info.CustomStartKHz / 1000.0:0.000} MHz, {info.CustomChannels} channel(s) every {info.CustomSpacing * 10} kHz"
                : $"Frequency         {region.ToString().ToUpperInvariant()} {FrequencyTable.Label(info.StartFrequency ?? 0)} – {FrequencyTable.Label(info.EndFrequency ?? 0)}");
        }
        if (info.LinkProfile is { } profile) sb.AppendLine($"Link profile      {(int)profile}");
        if (info.DrmEnabled is { } drm) sb.AppendLine($"DRM               {(drm ? "open" : "closed")}");
        if (info.AntennaDetectorDb is { } det) sb.AppendLine($"Antenna detection {(det == 0 ? "off" : det + " dB")}");
        if (info.FastTid is { } tid) sb.AppendLine($"Fast TID          {(tid ? "open" : "closed")}");
        if (info.Gpio1High is { } g1) sb.AppendLine($"GPIO 1 / 2        {(g1 ? "high" : "low")} / {(info.Gpio2High == true ? "high" : "low")}");
        foreach (var p in info.Problems) sb.AppendLine("! " + p);
        return sb.ToString();
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (ReadersGrid.SelectedItem is not ReaderRow row || Control() is not { } rfid) return;
        if (!Ui.Confirm($"Restart reader {row.Name}? Its antennas stop for a few seconds.")) return;
        await Ui.Try(async () =>
        {
            await rfid.ResetAsync(row.Id);
            Ui.Info($"{row.Name} restarted.");
        });
    }

    private async void Station_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not MainWindow main) return;
        main.Navigate("setup");
        await MainWindow.Settle();
        main.GetView<SetupView>()?.ShowStationTab();
    }

    private async void SaveAssignments_Click(object sender, RoutedEventArgs e)
    {
        AntennaGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (AntennaGrid.ItemsSource is not List<AntennaEdit> rows) return;
        await Ui.Try(async () =>
        {
            await AppServices.RfidData.SaveAssignmentsAsync(rows.Select(r => new AntennaRow(r.AntennaId, r.ReaderId, r.ReaderName, r.Endpoint, r.Port, r.Name,
                r.IsEnabled, r.PowerDbm, r.Purpose, r.Station)));
            await ReloadHubAsync();
            ((MainWindow)Window.GetWindow(this)).SetStatus("Antenna assignments saved. Send settings to a reader to change its power.");
        });
    }

    private async void Monitor_Click(object sender, RoutedEventArgs e)
    {
        if (_monitor is not null)
        {
            StopMonitor();
            return;
        }
        if (Control() is not { } rfid || MonitorStation.SelectedValue is not int station) return;
        _monitor = new CancellationTokenSource();
        MonitorButton.Content = "Stop";
        Look.SetIcon(MonitorButton, "");
        _monitorLines.Insert(0, $"Listening to station {station:000}…");
        try
        {
            await foreach (var tag in rfid.ListenAsync(station, _monitor.Token))
            {
                if (!_names.TryGetValue(tag.Epc, out var name))
                {
                    var found = await AppServices.RfidData.ResolveAsync(AppServices.Session.LocationId, tag.Epc);
                    name = _names[tag.Epc] = found is null ? "(unknown tag)" : found.Item.Name;
                }
                _monitorLines.Insert(0, $"{tag.SeenAt:HH:mm:ss} {tag.ReaderName}/{tag.Antenna} {tag.RssiDbm,4} dBm  {name}\n         {tag.Epc}");
                while (_monitorLines.Count > 100) _monitorLines.RemoveAt(_monitorLines.Count - 1);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _monitorLines.Insert(0, "Stopped: " + ex.Message);
        }
        finally
        {
            StopMonitor();
        }
    }

    private void StopMonitor()
    {
        _monitor?.Cancel();
        _monitor?.Dispose();
        _monitor = null;
        MonitorButton.Content = "Start";
        Look.SetIcon(MonitorButton, "");
    }
}
