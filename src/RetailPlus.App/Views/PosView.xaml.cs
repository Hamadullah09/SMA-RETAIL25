using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RetailPlus.App.Dialogs;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Reports;

namespace RetailPlus.App.Views;

public partial class PosView : UserControl, IRefreshable
{
    public PosViewModel Vm { get; } = new();

    public PosView()
    {
        InitializeComponent();
        DataContext = Vm;
        Loaded += (_, _) =>
        {
            if (Window.GetWindow(this) is not { } window) return;
            window.PreviewTextInput -= RouteTyping;
            window.PreviewTextInput += RouteTyping;
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Vm.StartRfid();
                if (AppServices.Config.RfidBeep && AppServices.Rfid is not null && Sounds.IsSilenced())
                    Vm.Alert("The computer's sound is off (muted), so you will not hear a beep for each RFID tag. "
                             + "Turn it on with the speaker icon at the bottom right of the screen.");
                Dispatcher.BeginInvoke(() => CodeBox.Focus());
            }
            else Vm.StopRfid();
        };
        AppServices.RfidChanged += () => Dispatcher.BeginInvoke(() => { if (IsVisible) Vm.StartRfid(); else Vm.StopRfid(); });
    }

    public async Task RefreshAsync()
    {
        await Vm.LoadAsync();
        CodeBox.Focus();
    }

    /// <summary>
    /// Whatever is typed or scanned on this screen goes into the code box, wherever the focus is: a barcode scanner
    /// types like a keyboard, so a scan works even right after a click on the list or a button (and while RFID reads).
    /// </summary>
    private void RouteTyping(object sender, TextCompositionEventArgs e)
    {
        if (!IsVisible || e.Text.Length == 0 || char.IsControl(e.Text[0]) || char.IsWhiteSpace(e.Text[0])) return;
        if (Keyboard.FocusedElement is TextBox || CodeBox.IsKeyboardFocused) return;
        e.Handled = true;
        CodeBox.Focus();
        Vm.CodeText += e.Text;
        CodeBox.CaretIndex = CodeBox.Text.Length;
    }

    private async void CodeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && CodeBox.Text.Length > 0)
        {
            e.Handled = true;
            Vm.CodeText = "";
            return;
        }
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (CodeBox.Text.Trim().Length == 0 && Vm.Lines.Count > 0) { await Vm.PayAsync(); return; }
        await Ui.Try(Vm.EnterCodeAsync);
        CodeBox.Focus();
    }

    private async void Find_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ItemPickerWindow(Vm.CodeText);
        if (picker.ShowDialog() == true && picker.Selected is { } row)
        {
            Vm.CodeText = "";
            await Ui.Try(() => Vm.AddCodeAsync(row.Plu));
        }
        CodeBox.Focus();
    }

    private void OpenItem_Click(object sender, RoutedEventArgs e)
    {
        var description = Ui.Prompt("Description:", "Open item", "Miscellaneous");
        if (description is null) return;
        var price = Ui.PromptDecimal("Price:", "Open item");
        if (price is null) return;
        Vm.AddOpenItem(description, price.Value, true, true);
        CodeBox.Focus();
    }

    private async void Client_Click(object sender, RoutedEventArgs e)
    {
        var picker = new PartyPickerWindow();
        if (picker.ShowDialog() == true && picker.Selected is { } row) await Ui.Try(() => Vm.SetClientAsync(row.Id));
        CodeBox.Focus();
    }

    private void ClearClient_Click(object sender, RoutedEventArgs e) { Vm.ClearClient(); CodeBox.Focus(); }
    private void Rfid_Click(object sender, RoutedEventArgs e) { Vm.ToggleRfid(); CodeBox.Focus(); }
    private void Reward_Click(object sender, RoutedEventArgs e) { Vm.TakeReward(); CodeBox.Focus(); }

    private async void FastKey_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is FastKey key) await Ui.Try(() => Vm.FastKeyAsync(key));
        CodeBox.Focus();
    }

    private async void Pay_Click(object sender, RoutedEventArgs e) { await Vm.PayAsync(); CodeBox.Focus(); }
    private async void Layaway_Click(object sender, RoutedEventArgs e) { await Vm.PayAsync(layaway: true); CodeBox.Focus(); }
    private async void Qty_Click(object sender, RoutedEventArgs e) { await Ui.Try(Vm.ChangeQuantityAsync); CodeBox.Focus(); }
    private void Price_Click(object sender, RoutedEventArgs e) { Vm.ChangePrice(); CodeBox.Focus(); }
    private void LineDiscount_Click(object sender, RoutedEventArgs e) { Vm.LineDiscount(); CodeBox.Focus(); }
    private void SubtotalDiscount_Click(object sender, RoutedEventArgs e) { Vm.SubtotalDiscount(); CodeBox.Focus(); }
    private async void Remove_Click(object sender, RoutedEventArgs e) { await Vm.RemoveLineAsync(); CodeBox.Focus(); }
    private async void Suspend_Click(object sender, RoutedEventArgs e) { await Ui.Try(Vm.SuspendAsync); CodeBox.Focus(); }
    private async void Recall_Click(object sender, RoutedEventArgs e) { await Ui.Try(Vm.RecallAsync); CodeBox.Focus(); }
    private async void Quote_Click(object sender, RoutedEventArgs e) { await Vm.SaveQuoteAsync(); CodeBox.Focus(); }
    private async void Reprint_Click(object sender, RoutedEventArgs e) { await Ui.Try(Vm.ReprintLastAsync); CodeBox.Focus(); }
    private async void Clear_Click(object sender, RoutedEventArgs e) { await Vm.ClearSaleAsync(); CodeBox.Focus(); }

    private async void NoSale_Click(object sender, RoutedEventArgs e)
    {
        var reason = Ui.Prompt("Reason for opening the drawer:", "No sale", "Change");
        if (reason is null) return;
        if (await Ui.Try(() => AppServices.Drawer.AddMovementAsync(AppServices.Session, DrawerMovementKind.NoSale, 0, reason))) PosViewModel.OpenDrawer();
        CodeBox.Focus();
    }

    private async void PayIn_Click(object sender, RoutedEventArgs e) => await MovementAsync(DrawerMovementKind.PayIn, "Pay in");
    private async void PayOut_Click(object sender, RoutedEventArgs e) => await MovementAsync(DrawerMovementKind.PayOut, "Pay out");
    private async void Float_Click(object sender, RoutedEventArgs e) => await MovementAsync(DrawerMovementKind.OpeningFloat, "Opening float");

    private async Task MovementAsync(DrawerMovementKind kind, string title)
    {
        var amount = Ui.PromptDecimal("Amount:", title);
        if (amount is null) return;
        var reason = kind == DrawerMovementKind.OpeningFloat ? "Float" : Ui.Prompt("Reason:", title);
        if (reason is null) return;
        if (await Ui.Try(() => AppServices.Drawer.AddMovementAsync(AppServices.Session, kind, amount.Value, reason)))
        {
            PosViewModel.OpenDrawer();
            Vm.Message = $"{title} of {amount.Value:N2} recorded.";
        }
        CodeBox.Focus();
    }

    private async void XReport_Click(object sender, RoutedEventArgs e)
    {
        await Ui.Try(async () =>
        {
            var report = await AppServices.Drawer.GetCurrentAsync(AppServices.Session);
            new TextPreviewWindow("Drawer (X) report", ReceiptFormatter.Drawer(report, AppServices.Settings, AppServices.Config.ReceiptWidth, final: false)).ShowDialog();
        });
        CodeBox.Focus();
    }

    private async void EndOfDay_Click(object sender, RoutedEventArgs e)
    {
        if (!AppServices.Can(3) && !await LoginWindow.SupervisorApprovesAsync("End of Day")) return;
        if (!Ui.Confirm("Close the drawer for today and print the End of Day report?")) return;
        await Ui.Try(async () =>
        {
            var report = await AppServices.Drawer.EndOfDayAsync(AppServices.Session);
            var text = ReceiptFormatter.Drawer(report, AppServices.Settings, AppServices.Config.ReceiptWidth);
            try { AppServices.Printer.Print(text, false); } catch (Exception ex) { Log.Write(ex.Message); }
            new TextPreviewWindow("End of Day (Z) report", text).ShowDialog();
        });
        CodeBox.Focus();
    }

    /// <summary>Used by Invoices &amp; A/R to sell a quote.</summary>
    public Task LoadDraftAsync(SaleDraft draft) => Vm.LoadDraftAsync(draft);
}
