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
        _party = party;
        DataContext = party;
        bool isClient = party is Client;
        string what = isClient ? "client" : "supplier";
        HeaderText.Text = party.Id == 0 ? $"New {what}" : (party.Company ?? $"{party.FirstName} {party.LastName}".Trim());
        Title = party.Id == 0 ? $"New {what}" : $"{(isClient ? "Client" : "Supplier")} {party.Number}";
        if (!isClient)
        {
            AccountTab.Visibility = Visibility.Collapsed;
            HistoryTab.Visibility = Visibility.Collapsed;
            BirthdayRow.Visibility = Visibility.Collapsed;
            NumberLabel.Text = "Supplier number";
        }
        PriceLevelBox.ItemsSource = new[] { 0, 1, 2, 3, 4 };
        UnitBox.ItemsSource = new[] { "Days", "Weeks", "Months" };
        Exempt1Label.Text = AppServices.Settings.Tax1Name ?? "Tax 1";
        Exempt2Label.Text = AppServices.Settings.Tax2Name ?? "Tax 2";
        if (isClient && party.Id != 0)
            Loaded += async (_, _) => await Ui.Try(async () => HistoryGrid.ItemsSource = await AppServices.Parties.GetPurchaseHistoryAsync(party.Id));
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        bool ok = _party is Client c
            ? await Ui.Try(() => AppServices.Parties.SaveClientAsync(c))
            : await Ui.Try(() => AppServices.Parties.SaveSupplierAsync((Supplier)_party));
        if (ok) DialogResult = true;
    }
}
