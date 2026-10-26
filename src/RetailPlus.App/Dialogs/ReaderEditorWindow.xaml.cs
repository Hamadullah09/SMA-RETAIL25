using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.IO.Ports;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using RetailPlus.App.Infrastructure;
using RetailPlus.App.Views;
using RetailPlus.Core.Model;
using RetailPlus.Rfid;
using RetailPlus.Rfid.D2184;

namespace RetailPlus.App.Dialogs;

/// <summary>Settings of one RFID reader: connection, the four antennas, radio settings, and live device commands.</summary>
public partial class ReaderEditorWindow : Window
{
    /// <summary>A frequency choice: table index and its MHz label.</summary>
    public sealed record FrequencyChoice(int Value, string Text);

    /// <summary>One antenna port as edited in the window.</summary>
    public sealed class AntennaVm : INotifyPropertyChanged
    {
        private int _power;
        public event PropertyChangedEventHandler? PropertyChanged;
        public int Port { get; init; }
        public string Title => $"Port {Port}";
        public bool IsEnabled { get; set; }
        public string? Name { get; set; }
        public int PowerDbm
        {
            get => _power;
            set
            {
                _power = value;
                Notify();
                Notify(nameof(PowerText));
            }
        }
        public string PowerText => $"{_power} dBm";
        public RfidAntennaPurpose Purpose { get; set; }
        public int? Station { get; set; }
        public static IReadOnlyList<PurposeChoice> Purposes => PurposeChoice.All;
        public IReadOnlyList<StationChoice> Stations { get; init; } = [];

        private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private readonly RfidReader _reader;
    private List<AntennaVm> _antennas = [];

    public ReaderEditorWindow(RfidReader reader)
    {
        InitializeComponent();
        _reader = reader;
        DataContext = reader;
        HeaderText.Text = reader.Id == 0 ? "New RFID reader" : $"Reader {reader.Name}";
        Title = HeaderText.Text;
        PortBox.ItemsSource = SerialPort.GetPortNames();
        BaudBox.ItemsSource = new[] { 9600, 19200, 38400, 57600, 115200 };
        BeeperBox.ItemsSource = Enum.GetValues<RfidBeeperMode>();
        AddressBox.Text = reader.Address.ToString("X2", CultureInfo.InvariantCulture);
        (reader.Transport == RfidTransport.Serial ? SerialRadio : TcpRadio).IsChecked = true;
        (reader.Region switch { RfidFrequencyRegion.Etsi => EtsiRadio, RfidFrequencyRegion.Chn => ChnRadio, RfidFrequencyRegion.Custom => CustomRadio, _ => FccRadio }).IsChecked = true;
        foreach (var radio in ProfilePanel.Children.OfType<RadioButton>())
            radio.IsChecked = (string)radio.Tag == ((int)reader.LinkProfile).ToString(CultureInfo.InvariantCulture);
        RssiSlider.ValueChanged += (_, _) => RssiText.Text = $"{RssiSlider.Value:0} dBm";
        RssiText.Text = $"{reader.RssiThresholdDbm} dBm";
        RlAntenna.ItemsSource = new[] { 1, 2, 3, 4 };
        RlAntenna.SelectedIndex = 0;
        RlFrequency.ItemsSource = Enumerable.Range(0, FrequencyTable.MaxIndex + 1).Select(i => new FrequencyChoice(i, FrequencyTable.Label(i))).ToList();
        RlFrequency.SelectedValue = 33;
        Loaded += async (_, _) => await Ui.Try(LoadAntennasAsync);
    }

    private async Task LoadAntennasAsync()
    {
        var stations = await RfidView.StationChoicesAsync(true);
        _antennas = Enumerable.Range(1, 4).Select(port =>
        {
            var a = _reader.Antennas.FirstOrDefault(x => x.Port == port) ?? new RfidAntenna { Port = port, IsEnabled = false };
            return new AntennaVm { Port = port, IsEnabled = a.IsEnabled, Name = a.Name, PowerDbm = a.PowerDbm, Purpose = a.Purpose, Station = a.StationNumber, Stations = stations };
        }).ToList();
        AntennaList.ItemsSource = _antennas;
    }

    private void Transport_Changed(object sender, RoutedEventArgs e)
    {
        bool serial = SerialRadio.IsChecked == true;
        _reader.Transport = serial ? RfidTransport.Serial : RfidTransport.Tcp;
        TcpPanel.Visibility = serial ? Visibility.Collapsed : Visibility.Visible;
        SerialPanel.Visibility = serial ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Region_Changed(object sender, RoutedEventArgs e)
    {
        var region = EtsiRadio.IsChecked == true ? RfidFrequencyRegion.Etsi
            : ChnRadio.IsChecked == true ? RfidFrequencyRegion.Chn
            : CustomRadio.IsChecked == true ? RfidFrequencyRegion.Custom
            : RfidFrequencyRegion.Fcc;
        _reader.Region = region;
        bool custom = region == RfidFrequencyRegion.Custom;
        CustomPanel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        RangePanel.Visibility = custom ? Visibility.Collapsed : Visibility.Visible;
        if (custom) return;
        var (min, max) = FrequencyTable.Range(region);
        var choices = Enumerable.Range(min, max - min + 1).Select(i => new FrequencyChoice(i, FrequencyTable.Label(i))).ToList();
        int start = Math.Clamp(_reader.StartFrequency, min, max), end = Math.Clamp(_reader.EndFrequency, min, max);
        if (end < start || (_reader.StartFrequency < min || _reader.EndFrequency > max)) (start, end) = (min, max);
        StartBox.ItemsSource = choices;
        EndBox.ItemsSource = choices;
        _reader.StartFrequency = start;
        _reader.EndFrequency = end;
        StartBox.SelectedValue = start;
        EndBox.SelectedValue = end;
    }

    private void Profile_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag }) _reader.LinkProfile = (RfidLinkProfile)int.Parse(tag, CultureInfo.InvariantCulture);
    }

    private bool Collect()
    {
        if (!int.TryParse(AddressBox.Text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address) || address is < 0 or > 255)
        {
            Ui.Error("The RS-485 address is a hexadecimal number from 00 to FF.");
            return false;
        }
        _reader.Address = address;
        if (StartBox.SelectedValue is int s) _reader.StartFrequency = s;
        if (EndBox.SelectedValue is int en) _reader.EndFrequency = en;
        if (_reader.EndFrequency < _reader.StartFrequency)
        {
            Ui.Error("The end frequency must not be below the start frequency.");
            return false;
        }
        _reader.Antennas = _antennas.Select(a =>
        {
            var existing = _reader.Antennas.FirstOrDefault(x => x.Port == a.Port);
            return new RfidAntenna
            {
                Id = existing?.Id ?? 0, ReaderId = _reader.Id, Port = a.Port, Name = a.Name, IsEnabled = a.IsEnabled, PowerDbm = a.PowerDbm,
                Purpose = a.Purpose, StationNumber = a.Station,
            };
        }).ToList();
        return true;
    }

    private async Task<bool> SaveAsync()
    {
        if (!Collect()) return false;
        RfidReader? saved = null;
        if (!await Ui.Try(async () => saved = await AppServices.RfidData.SaveReaderAsync(_reader)) || saved is null) return false;
        _reader.Id = saved.Id;
        _reader.RowVersion = saved.RowVersion;
        foreach (var a in saved.Antennas)
            if (_reader.Antennas.FirstOrDefault(x => x.Port == a.Port) is { } mine) mine.Id = a.Id;
        HeaderText.Text = $"Reader {_reader.Name}";
        if (AppServices.Rfid is { } rfid)
        {
            try { await rfid.ReloadAsync(); } catch (Exception ex) when (ex is RfidException or HttpRequestException) { Log.Write("RFID reload: " + ex.Message); }
        }
        return true;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (await SaveAsync()) DialogResult = true;
    }

    private async void SaveApply_Click(object sender, RoutedEventArgs e)
    {
        if (!await SaveAsync()) return;
        if (AppServices.Rfid is not { } rfid)
        {
            Ui.Info("Saved. RFID is off at this station, so the settings were not sent; send them from a station that reaches the reader.");
            DialogResult = true;
            return;
        }
        await Ui.Try(async () =>
        {
            var problems = await rfid.ApplySettingsAsync(_reader.Id);
            if (AppServices.Config.RfidMode == Settings.RfidMode.ThisComputer)
                await AppServices.RfidData.MarkAppliedAsync(_reader.Id, (await rfid.GetStatusAsync()).FirstOrDefault(s => s.ReaderId == _reader.Id)?.Firmware);
            if (problems.Count > 0) Ui.Error("Saved, but the reader did not take every setting:\n\n" + string.Join("\n", problems));
            DialogResult = true;
        });
    }

    private IRfidControl? Device()
    {
        if (_reader.Id == 0)
        {
            Ui.Info("Save the reader first.");
            return null;
        }
        if (AppServices.Rfid is { } rfid) return rfid;
        Ui.Error("RFID is off at this station. Choose how this computer reaches the readers in Setup › This station.");
        return null;
    }

    private async void ReadInfo_Click(object sender, RoutedEventArgs e)
    {
        if (Device() is not { } rfid) return;
        await Ui.Try(async () => DeviceText.Text = RfidView.Describe(_reader.Name, await rfid.ReadInfoAsync(_reader.Id)));
    }

    private async void ReturnLoss_Click(object sender, RoutedEventArgs e)
    {
        if (Device() is not { } rfid || RlAntenna.SelectedItem is not int antenna || RlFrequency.SelectedValue is not int frequency) return;
        await Ui.Try(async () =>
        {
            int rl = await rfid.MeasureReturnLossAsync(_reader.Id, antenna, frequency);
            double ratio = Math.Pow(10, rl / 20.0);
            double vswr = (ratio + 1) / (ratio - 1);
            string verdict = rl >= 15 ? "good" : rl >= 10 ? "acceptable" : "poor — check the cable and antenna";
            DeviceText.Text = $"Antenna {antenna} at {FrequencyTable.Label(frequency)}\nReturn loss {rl} dB (VSWR {vswr:0.00}): {verdict}\n\n" + DeviceText.Text;
        });
    }

    private async void Gpio_Click(object sender, RoutedEventArgs e)
    {
        if (Device() is not { } rfid || sender is not Button { Tag: string tag }) return;
        var parts = tag.Split(',');
        int gpio = int.Parse(parts[0], CultureInfo.InvariantCulture);
        bool high = parts[1] == "1";
        await Ui.Try(async () =>
        {
            await rfid.WriteGpioAsync(_reader.Id, gpio, high);
            DeviceText.Text = $"GPIO {gpio} set {(high ? "high" : "low")}\n" + DeviceText.Text;
        });
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (Device() is not { } rfid || !Ui.Confirm($"Restart reader {_reader.Name}?")) return;
        await Ui.Try(async () =>
        {
            await rfid.ResetAsync(_reader.Id);
            DeviceText.Text = "Reader restarted.\n" + DeviceText.Text;
        });
    }
}
