using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.App.Dialogs;

/// <summary>
/// The exit-gate alarm at the till: a red window on top of everything, with every item of this station that went
/// through the gate without being sold (several items passing together are in one window), and the siren until someone
/// presses Silence. Each item is resolved here: brought back (back in stock, flag 1) or gone (left the store: Lost),
/// which needs a supervisor. It works on every screen and while nobody is signed in; closing it leaves the alarms open,
/// and the next pass of one of their items brings it back.
/// </summary>
public sealed class GateAlarmWindow : Window
{
    private readonly StackPanel _rows = new();
    private readonly TextBlock _summary = new() { FontSize = 18, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _sound = new() { FontSize = 15, Foreground = Look.Brush("Danger"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    private readonly Button _silence = new() { Content = "Silence", MinWidth = 170, MinHeight = 58, Margin = new Thickness(0, 0, 10, 0), FontSize = 18 };
    /// <summary>True while this window's own approval or message is open: then it stays switched off like any owner.</summary>
    private bool _resolving;

    /// <summary>The window while it is open (the automated check looks at it).</summary>
    public static GateAlarmWindow? Current { get; private set; }

    /// <summary>The open alarms shown.</summary>
    public IReadOnlyList<GateAlarm> Alarms { get; private set; } = [];

    /// <summary>Called once at start-up: from then on, every alarm of this station's exit gate opens the window.</summary>
    public static void Attach() => AppServices.GateAlarmRaised += result =>
        Application.Current?.Dispatcher.InvokeAsync(async () => await RaiseAsync(result));

    /// <summary>Brings the window up (or back) with this station's open alarms and sounds the siren.</summary>
    public static async Task RaiseAsync(GateReadResult? result = null)
    {
        if (await OpenAlarmsAsync() is not { } alarms) return;
        if (alarms.Count == 0)
        {
            Current?.Close();
            return;
        }
        var window = Current ?? new GateAlarmWindow();
        window.Fill(alarms);
        Sounds.StartSiren();
        window.ShowSound();
        window.Bring();
        if (result?.Alarm is { } alarm) Log.Write($"Gate alarm window: {alarms.Count} open, latest {alarm.Epc}");
    }

    /// <summary>
    /// Shows this station's open alarms without the siren (Ctrl+K › Exit gate alarms), to resolve one after the window was
    /// closed.
    /// </summary>
    public static async Task ShowOpenAsync()
    {
        if (await OpenAlarmsAsync() is not { } alarms) return;
        if (alarms.Count == 0)
        {
            Current?.Close();
            Ui.Info("No item is waiting at this station's exit gate.", "Exit gate alarms");
            return;
        }
        var window = Current ?? new GateAlarmWindow();
        window.Fill(alarms);
        window.ShowSound();
        window.Bring();
    }

    /// <summary>After alarms were resolved elsewhere (a sale of the piece): the open window shows what is left, or closes.</summary>
    public static async Task UpdateOpenAsync()
    {
        if (Current is not { } window) return;
        await window.RefreshAsync();
        if (window.Alarms.Count == 0) window.Close();
    }

    /// <summary>This station's open alarms, oldest first; null when they cannot be read (the window stays as it is).</summary>
    private static async Task<IReadOnlyList<GateAlarm>?> OpenAlarmsAsync()
    {
        try
        {
            return await AppServices.GateAlarms.GetOpenAsync(AppServices.Session.StationNumber);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Write("Gate alarms: " + ex.Message);
            return null;
        }
    }

    private void Bring()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Topmost = true;
        Activate();
    }

    private GateAlarmWindow()
    {
        Current = this;
        Title = "Exit gate alarm";
        Width = 860;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanMinimize;
        Topmost = true;
        ShowInTaskbar = true;
        Background = Look.Brush("Cream");

        var root = new DockPanel();
        var header = new Border { Background = Look.Brush("Danger"), Padding = new Thickness(28, 20, 28, 18) };
        var title = new StackPanel();
        title.Children.Add(new TextBlock
        {
            Text = "Exit gate alarm", Foreground = Brushes.White, FontSize = 32, FontWeight = FontWeights.Bold,
            FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"),
        });
        title.Children.Add(_summary);
        header.Child = title;
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(28, 12, 28, 22) };
        _silence.Style = (Style)Application.Current.FindResource("AccentButton");
        Look.SetIcon(_silence, "");
        Look.SetShortcut(_silence, "Esc");
        _silence.Click += (_, _) => Silence();
        buttons.Children.Add(_silence);
        var close = new Button { Content = "Close", MinWidth = 150, MinHeight = 58 };
        Look.SetIcon(close, "");
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var body = new DockPanel { Margin = new Thickness(28, 18, 28, 0) };
        DockPanel.SetDock(_sound, Dock.Top);
        body.Children.Add(_sound);
        body.Children.Add(new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        root.Children.Add(body);
        Content = root;

        // Closing silences the siren; the alarms stay open until they are resolved.
        Closed += (_, _) =>
        {
            Sounds.StopSiren();
            if (ReferenceEquals(Current, this)) Current = null;
        };
    }

    /// <summary>Stops the siren; the window and the alarms stay. Needs no approval.</summary>
    public void Silence()
    {
        Sounds.StopSiren();
        ShowSound();
    }

    private void ShowSound()
    {
        _silence.IsEnabled = Sounds.SirenOn;
        _silence.Content = Sounds.SirenOn ? "Silence" : "Silenced";
        _sound.Text = Sounds.SirenOn && !AppServices.Unattended && Sounds.IsSilenced()
            ? "This computer's sound is off or turned right down, so the siren cannot be heard. Turn the volume up."
            : "";
        _sound.Visibility = _sound.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Reads this station's open alarms again.</summary>
    public async Task RefreshAsync()
    {
        if (await OpenAlarmsAsync() is { } alarms) Fill(alarms);
    }

    private void Fill(IReadOnlyList<GateAlarm> alarms)
    {
        Alarms = alarms;
        _rows.Children.Clear();
        foreach (var alarm in alarms) _rows.Children.Add(Row(alarm));
        _summary.Text = alarms.Count == 1
            ? "This item went through the exit gate without being sold."
            : $"These {alarms.Count} items went through the exit gate without being sold.";
    }

    private Border Row(GateAlarm alarm)
    {
        var c = CultureInfo.CurrentCulture;
        var card = new Border
        {
            Background = Look.Brush("CardSolid"), BorderBrush = Look.Brush("Danger"), BorderThickness = new Thickness(6, 0, 0, 0),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 0, 12),
        };
        var grid = new DockPanel();
        var actions = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        var back = new Button { Content = "Back in stock", MinWidth = 190, MinHeight = 48, Margin = new Thickness(0, 0, 0, 8), Style = (Style)Application.Current.FindResource("AccentButton") };
        Look.SetIcon(back, "");
        back.Click += async (_, _) => await ResolveAsync(alarm, GateAlarmResolution.BackInStock);
        var gone = new Button { Content = "Left the store", MinWidth = 190, MinHeight = 48 };
        Look.SetIcon(gone, "");
        gone.Click += async (_, _) => await ResolveAsync(alarm, GateAlarmResolution.LeftStore);
        actions.Children.Add(back);
        actions.Children.Add(gone);
        DockPanel.SetDock(actions, Dock.Right);
        grid.Children.Add(actions);

        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = alarm.ItemName, FontSize = 24, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock
        {
            Text = $"PLU {alarm.Plu}   ·   {alarm.Price.ToString("N2", c)}", FontSize = 18, Margin = new Thickness(0, 4, 0, 0),
        });
        text.Children.Add(new TextBlock { Text = "Tag " + alarm.Epc, FontFamily = new FontFamily("Consolas"), FontSize = 16, Margin = new Thickness(0, 4, 0, 0) });
        var seen = alarm.Passes > 1
            ? $"At the gate {alarm.RaisedAt.ToString("T", c)}, again {alarm.LastSeenAt.ToString("T", c)} ({alarm.Passes} times)"
            : $"At the gate {alarm.RaisedAt.ToString("T", c)}";
        if (alarm.RaisedAt.Date != DateTime.Today) seen += " on " + alarm.RaisedAt.ToString("d", c);
        text.Children.Add(new TextBlock
        {
            Text = seen + (alarm.ReaderName is null ? "" : $"   ·   {alarm.ReaderName} antenna {alarm.Antenna}") + $"   ·   flag {TagFlags.Text(ItemTagStatus.Unsold)}",
            FontSize = 15, Foreground = Look.Brush("Muted"), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap,
        });
        grid.Children.Add(text);
        card.Child = grid;
        return card;
    }

    /// <summary>Resolves one item (by a supervisor: the signed-in one, or one who approves here), then shows what is left.</summary>
    public async Task<bool> ResolveAsync(GateAlarm alarm, GateAlarmResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(alarm);
        var action = resolution == GateAlarmResolution.BackInStock
            ? $"Putting {alarm.ItemName} back in stock after the gate alarm"
            : $"Writing off {alarm.ItemName} as gone from the store";
        if (_resolving) return false;
        _resolving = true;
        try
        {
            if (Approval.SignedInFor(Core.Rules.Permissions.PosApprove, action) is not { } who) return false;
            bool done = await Ui.Try(() => AppServices.GateAlarms.ResolveAsync(alarm.Id, resolution, who));
            await RefreshAsync();
            if (Alarms.Count == 0) Close();
            return done;
        }
        finally
        {
            _resolving = false;
        }
    }

    // Another window opened with ShowDialog (the Pay window, a message) switches off every other window of the program
    // until it closes. The alarm must stay usable on top of it, so it switches itself on again.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source) source.AddHook(KeepEnabled);
    }

    private const int WmEnable = 0x000A;

    private IntPtr KeepEnabled(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmEnable && wParam == IntPtr.Zero && !_resolving)
            Dispatcher.BeginInvoke(() => EnableWindow(hwnd, true));
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool enable);
}
