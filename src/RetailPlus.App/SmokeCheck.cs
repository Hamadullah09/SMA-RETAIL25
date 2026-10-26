using RetailPlus.App.Infrastructure;
using RetailPlus.App.Views;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.App;

/// <summary>
/// <c>--smoke</c>: opens every screen, runs every report and rings up one cash sale through the POS screen's own code,
/// then exits with 0 when all of it worked. Results go to the check log.
/// </summary>
public static class SmokeCheck
{
    public static async Task<int> RunAsync(MainWindow main)
    {
        var failures = new List<string>();
        Log.Write("Smoke check started on " + AppServices.Data.DatabaseName);
        foreach (var entry in MainWindow.AllEntries)
        {
            try
            {
                main.Navigate(entry.Key);
                await MainWindow.Settle();
                Log.Write($"Screen {entry.Key} ok");
            }
            catch (Exception ex)
            {
                failures.Add($"screen {entry.Key}: {ex.Message}");
            }
        }

        failures.AddRange(await ReportsView.RunAllAsync());

        try
        {
            const string code = "SMOKE-TEST";
            var (item, _) = await AppServices.Inventory.FindByCodeAsync(AppServices.Session.LocationId, code);
            if (item is null)
            {
                item = await AppServices.Inventory.SaveAsync(new Item
                {
                    LocationId = AppServices.Session.LocationId, Plu = code, Name = "Smoke test item", Price1 = 9.99m, LastCost = 4m, AverageCost = 4m,
                    Taxable1 = true, Taxable2 = true, OnHand = 100,
                }, "SMOKE", null, null);
            }
            main.Navigate("pos");
            await MainWindow.Settle();
            var pos = main.GetView<PosView>() ?? throw new InvalidOperationException("POS screen did not open");
            var before = (await AppServices.Inventory.GetAsync(item.Id))!.OnHand;
            await pos.Vm.AddCodeAsync(code, 2);
            var total = pos.Vm.Total;
            var cash = Money.RoundToMinimumTender(total, AppServices.Settings.MinimumTender);
            bool posted = await pos.Vm.PostAsync([new TenderEntry("Cash", TenderKind.Cash, cash, cash + 5)]);
            if (!posted) failures.Add("POS sale was not saved");
            var after = (await AppServices.Inventory.GetAsync(item.Id))!.OnHand;
            if (!AppServices.Session.IsTrainee && after != before - 2) failures.Add($"stock went from {before} to {after}, expected {before - 2}");
            var report = await AppServices.Drawer.GetCurrentAsync(AppServices.Session);
            Log.Write($"Sale posted: total {total}, drawer receipts now {report.Session.TotalReceipts}, customers {report.Session.CustomerCount}");
        }
        catch (Exception ex)
        {
            failures.Add("POS sale: " + ex);
        }

        try
        {
            failures.AddRange(await RfidCheckAsync(main));
        }
        catch (Exception ex)
        {
            failures.Add("RFID: " + ex);
        }

        foreach (var f in failures) Log.Write("FAIL " + f);
        Log.Write(failures.Count == 0 ? "Smoke check passed" : $"Smoke check failed ({failures.Count})");
        return failures.Count == 0 ? 0 : 5;
    }

    /// <summary>
    /// RFID end to end: a simulated reader whose antenna 1 serves this station "sees" a registered tag; the POS must
    /// ring the item up by itself, and paying must mark the tag sold.
    /// </summary>
    private static async Task<List<string>> RfidCheckAsync(MainWindow main)
    {
        var failures = new List<string>();
        int station = AppServices.Session.StationNumber;
        const string code = "SMOKE-RFID";
        var (item, _) = await AppServices.Inventory.FindByCodeAsync(AppServices.Session.LocationId, code);
        item ??= await AppServices.Inventory.SaveAsync(new Item
        {
            LocationId = AppServices.Session.LocationId, Plu = code, Name = "Smoke RFID item", Price1 = 5m, LastCost = 2m, AverageCost = 2m, OnHand = 50,
        }, "SMOKE", null, null);
        string epc = "E2801170000" + DateTime.Now.ToString("HHmmssfff", System.Globalization.CultureInfo.InvariantCulture) + "00";
        await AppServices.RfidData.RegisterTagAsync(epc, item.Id);

        var reader = Data.Services.RfidService.NewReader("Smoke sim " + DateTime.Now.ToString("HHmmssfff", System.Globalization.CultureInfo.InvariantCulture));
        reader.Host = "sim";
        reader.Notes = $"1:{epc}";
        reader.Antennas[0].StationNumber = station;
        reader.Antennas[0].IsEnabled = true;
        reader = await AppServices.RfidData.SaveReaderAsync(reader);

        var mode = AppServices.Config.RfidMode;
        AppServices.Config.RfidMode = Settings.RfidMode.ThisComputer;
        AppServices.Config.RfidAutoAdd = true;
        try
        {
            await AppServices.StartRfidAsync();
            main.Navigate("pos");
            await MainWindow.Settle();
            var pos = main.GetView<PosView>() ?? throw new InvalidOperationException("POS screen did not open");
            pos.Vm.StartRfid();
            var until = DateTime.Now.AddSeconds(10);
            while (DateTime.Now < until && pos.Vm.Lines.All(l => l.Epc != epc)) await Task.Delay(200);
            if (pos.Vm.Lines.All(l => l.Epc != epc))
            {
                failures.Add($"RFID tag was not rung up at station {station} (RFID text: {pos.Vm.RfidText}; message: {pos.Vm.Message})");
                return failures;
            }
            Log.Write($"RFID tag {epc} rung up by antenna 1 ({pos.Vm.RfidText})");
            var cash = Money.RoundToMinimumTender(pos.Vm.Total, AppServices.Settings.MinimumTender);
            if (!await pos.Vm.PostAsync([new TenderEntry("Cash", TenderKind.Cash, cash, cash)])) failures.Add("RFID sale was not saved");
            var tag = (await AppServices.RfidData.GetTagsAsync(item.Id)).FirstOrDefault(t => t.Epc == epc);
            if (tag?.Status != ItemTagStatus.Sold) failures.Add($"tag status after the sale is {tag?.Status}, expected Sold");
            await Task.Delay(1500);
            if (pos.Vm.Lines.Any(l => l.Epc == epc)) failures.Add("a sold tag still on the antenna was rung up again");
            Log.Write("RFID check done: tag sold, not added twice");
        }
        finally
        {
            await AppServices.RfidData.DeleteReaderAsync(reader.Id);
            AppServices.Config.RfidMode = mode;
            await AppServices.StartRfidAsync();
        }
        return failures;
    }
}
