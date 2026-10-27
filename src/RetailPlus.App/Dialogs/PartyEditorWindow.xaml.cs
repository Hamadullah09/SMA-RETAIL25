using System.Windows;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;

namespace RetailPlus.App.Dialogs;

/// <summary>The client or supplier detail window.</summary>
public partial class PartyEditorWindow : Window
{
    private readonly Party _party;

    public PartyEditorWindow(Party party)
    {
        InitializeComponent();
        Views.Layout.AddIcons(this);
        _party = party;
        DataContext = party;
        bool isClient = party is Client;
        string what = isClient ? "client" : "supplier";
        HeaderText.Text = party.Id == Guid.Empty ? $"New {what}" : (party.Company ?? $"{party.FirstName} {party.LastName}".Trim());
        Title = party.Id == Guid.Empty ? $"New {what}" : $"{(isClient ? "Client" : "Supplier")} {party.Number}";
        if (!isClient)
        {
            AccountTab.Visibility = Visibility.Collapsed;
            HistoryTab.Visibility = Visibility.Collapsed;
            PointsTab.Visibility = Visibility.Collapsed;
            BirthdayRow.Visibility = Visibility.Collapsed;
            NumberLabel.Text = "Supplier number";
        }
        PriceLevelBox.ItemsSource = new[] { 0, 1, 2, 3, 4 };
        UnitBox.ItemsSource = new[] { "Days", "Weeks", "Months" };
        Exempt1Label.Text = AppServices.Settings.Tax1Name ?? "Tax 1";
        Exempt2Label.Text = AppServices.Settings.Tax2Name ?? "Tax 2";
        if (isClient && party.Id != Guid.Empty)
            Loaded += async (_, _) => await Ui.Try(async () =>
            {
                HistoryGrid.ItemsSource = await AppServices.Parties.GetPurchaseHistoryAsync(party.Id);
                await LoadPointsAsync();
            });
        else PointsTab.Visibility = Visibility.Collapsed;
    }

    /// <summary>Opens the window on the Points tab.</summary>
    public void ShowPoints() => PointsTab.IsSelected = true;

    private async Task LoadPointsAsync()
    {
        var history = await AppServices.Loyalty.HistoryAsync(_party.Id);
        PointsGrid.ItemsSource = history;
        decimal total = history.Sum(h => h.Points);
        PointsText.Text = $"{total.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture)} points";
        if (_party is Client client && client.Points != total)
        {
            client.Points = total;
            DataContext = null;
            DataContext = _party;
        }
    }

    private async void AdjustPoints_Click(object sender, RoutedEventArgs e)
    {
        if (_party.Id == Guid.Empty) return;
        if (!AppServices.Can(Core.Rules.Permissions.ClientsEdit)) { Ui.Error("Your role cannot change clients."); return; }
        var points = Ui.PromptDecimal("Points to add (a negative number takes points off):", "Adjust points");
        if (points is null || points == 0) return;
        var reason = Ui.Prompt("Reason:", "Adjust points");
        if (string.IsNullOrWhiteSpace(reason)) return;
        await Ui.Try(async () =>
        {
            await AppServices.Loyalty.AdjustAsync(_party.Id, points.Value, reason, AppServices.Session.UserName);
            // The client's version changed with the points; read it again so Save does not see a conflict.
            if (await AppServices.Parties.GetClientAsync(_party.Id) is { } fresh) _party.Version = fresh.Version;
            await LoadPointsAsync();
        });
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        bool ok = _party is Client c
            ? await Ui.Try(() => AppServices.Parties.SaveClientAsync(c))
            : await Ui.Try(() => AppServices.Parties.SaveSupplierAsync((Supplier)_party));
        if (ok) DialogResult = true;
    }
}
