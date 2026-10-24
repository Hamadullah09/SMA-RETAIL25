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

        foreach (var f in failures) Log.Write("FAIL " + f);
        Log.Write(failures.Count == 0 ? "Smoke check passed" : $"Smoke check failed ({failures.Count})");
        return failures.Count == 0 ? 0 : 5;
    }
}
